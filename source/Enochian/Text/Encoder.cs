using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verophyle.Regexp;
using Verophyle.Regexp.InputSet;

namespace Enochian.Text
{
    public class Encoder
    {
        public Encoder(FeatureSet features, Encoding encoding)
        {
            Features = features;
            Encoding = encoding;

            patterns = new Lazy<PatternRec[]>(() => Encoding.Patterns
                .OrderByDescending(p => p.Input.Length)
                .Select((p, i) => new PatternRec { Id = i, Pattern = p, Regexp = p.GetRegexp() })
                .ToArray());

            replacements = new Lazy<PatternRec[]>(() =>
                Patterns.Where(p => p.Pattern.IsReplacement).ToArray());

            templates = new Lazy<PatternRec[]>(() =>
                Patterns.Where(p => !p.Pattern.IsReplacement).ToArray());

            modifiers = new Lazy<HashSet<char>>(() => new HashSet<char>(
                Templates.SelectMany(pr => pr.Pattern.Input.Where(c => c != '_'))));
        }

        public FeatureSet Features { get; }
        public Encoding Encoding { get; }

        Lazy<PatternRec[]> patterns;
        PatternRec[] Patterns => patterns.Value;

        Lazy<PatternRec[]> replacements;
        PatternRec[] Replacements => replacements.Value;

        Lazy<PatternRec[]> templates;
        PatternRec[] Templates => templates.Value;

        Lazy<HashSet<char>> modifiers;
        HashSet<char> Modifiers => modifiers.Value;

        static readonly object processSegmentLock = new object();

        public TextSegment ProcessSegment(TextSegment input)
        {
            lock (processSegmentLock)
            {
                var result = new TextSegment
                {
                    SourceSegments = new List<TextSegment> { input },
                    Options = input.Options
                        .SelectMany(srcOption =>
                        {
                            (var text, var repr, var phones) = GetTextAndPhones(srcOption.Text);
                            var options = new List<SegmentOption>
                            {
                                new SegmentOption
                                {
                                    Encoding = this.Encoding,
                                    Entry = srcOption.Entry,
                                    Text = text,
                                    Phones = phones,
                                }
                            };

                            if (!string.IsNullOrEmpty(repr))
                            {
                                options.Add(new SegmentOption
                                {
                                    Text = repr,
                                });
                            }

                            return options;
                        })
                        .ToList()
                };
                return result;
            }
        }

        public (string, string, IList<double[]>) GetTextAndPhones(string source)
        {
            foreach (var pattern in Patterns)
                pattern.Reset();

            // replace the characters of the original with text + phones
            (var texts, var reprs, var phones) = GetReplacements(source);

            // merge the phones of existing characters with phones from templates
            MergeTemplates((texts, phones));

            var allTexts = string.Join("", texts);
            var allReprs = string.Join("", reprs);
            var allPhones = phones
                .SelectMany(ph => ph.Where(p => p.Length > 0))
                .ToArray();

            return (allTexts, allReprs, allPhones);
        }

        (IList<string>, IList<string>, IList<IList<double[]>>) GetReplacements(string source)
        {
            var texts = new List<string>();
            var reprs = new List<string>();
            var phonesPerText = new List<IList<double[]>>();

            // try replacements first
            PatternRec lastReplacement = null;
            int start = 0, cur = 0;
            while (cur < source.Length)
            {
                char ch = source[cur];

                bool inReplacement = false;
                PatternRec firstReplacement = null;
                foreach (var replacement in Replacements)
                {
                    if (replacement.Regexp.Failed)
                        continue;
                    replacement.Regexp.ProcessInput(ch);
                    if (!replacement.Regexp.Failed)
                        inReplacement = true;
                    if (replacement.Regexp.Succeeded && firstReplacement == null)
                        firstReplacement = replacement;
                }

                if (inReplacement)
                {
                    if (firstReplacement != null && cur + 1 >= source.Length)
                    {
                        var text = !string.IsNullOrEmpty(firstReplacement.Pattern.Output)
                            ? firstReplacement.Pattern.Output
                            : source.Substring(start, 1 + cur - start);
                        texts.Add(text);
                        if (!string.IsNullOrEmpty(firstReplacement.Pattern.Repr))
                            reprs.Add(firstReplacement.Pattern.Repr);
                        phonesPerText.Add(firstReplacement.Pattern.Phones);
                    }
                    lastReplacement = firstReplacement;
                    cur++;
                }
                else
                {
                    if (lastReplacement != null)
                    {
                        var text = !string.IsNullOrEmpty(lastReplacement.Pattern.Output)
                            ? lastReplacement.Pattern.Output
                            : source.Substring(start, cur - start);
                        texts.Add(text);
                        if (!string.IsNullOrEmpty(lastReplacement.Pattern.Repr))
                            reprs.Add(lastReplacement.Pattern.Repr);
                        phonesPerText.Add(lastReplacement.Pattern.Phones);
                        lastReplacement = null;
                        start = cur; // start again on this character
                    }
                    else
                    {
                        texts.Add(source.Substring(start, 1 + cur - start));
                        phonesPerText.Add(Modifiers.Contains(ch) 
                            ? new double[][] { new double[0] } 
                            : new double[][] { Features.GetUnsetVector() });
                        start = ++cur; // go to the next character
                    }

                    foreach (var replacement in Replacements)
                        replacement.Reset();
                }
            }

            return (texts, reprs, phonesPerText);
        }

        void MergeTemplates((IList<string>, IList<IList<double[]>>) textsAndPhones)
        {
            (var texts, var phonesPerText) = textsAndPhones;

            int startText = 0;
            int curText = 0;
            int startChar = 0;
            int curChar = 0;

            var toMerge = new List<(int, int, IList<double[]>)>();
            PatternRec lastTemplate = null;
            while (curText < texts.Count && curChar < texts[curText].Length)
            {
                char ch = texts[curText][curChar];

                bool inTemplate = false;
                PatternRec firstTemplate = null;
                foreach (var template in Templates)
                {
                    if (template.Regexp.Failed)
                        continue;
                    template.Regexp.ProcessInput(ch);
                    if (!template.Regexp.Failed)
                        inTemplate = true;
                    if (template.Regexp.Succeeded && firstTemplate == null)
                        firstTemplate = template;
                }

                if (inTemplate)
                {
                    if (firstTemplate != null && curText + 1 >= texts.Count && curChar + 1>= texts[curText].Length)
                    {
                        MergeTemplatePhones(texts, startText, startChar, firstTemplate, toMerge);
                    }
                    lastTemplate = firstTemplate;
                    if (++curChar >= texts[curText].Length)
                    {
                        curText++;
                        curChar = 0;
                    }
                }
                else
                {
                    if (lastTemplate != null)
                    {
                        MergeTemplatePhones(texts, startText, startChar, lastTemplate, toMerge);

                        lastTemplate = null;
                        startText = curText;
                        startChar = curChar;
                    }
                    else
                    {
                        if (++curChar >= texts[curText].Length)
                        {
                            curText++;
                            curChar = 0;
                        }
                    }

                    foreach (var template in Templates)
                        template.Reset();
                }
            }

            for (int i = toMerge.Count - 1; i >= 0; i--)
            {
                int textIndex = toMerge[i].Item1;
                int charIndex = toMerge[i].Item2;
                var patt = toMerge[i].Item3;

                var span = phonesPerText[textIndex];
                var orig = span[charIndex];
                var repl = patt.Select(pat => Features.Override(orig, pat)).ToArray();

                if (repl.Length == 1)
                {
                    span[charIndex] = repl[0];
                }
                else if (repl.Length > 1)
                {
                    span.RemoveAt(charIndex);
                    foreach (var rep in repl.Reverse())
                        span.Insert(charIndex, rep);
                }
            }
        }

        private static void MergeTemplatePhones(IList<string> texts, int startText, int startChar, PatternRec template, List<(int, int, IList<double[]>)> toMerge)
        {
            int offset = template.Pattern.Input.IndexOf('_');
            if (offset < 0) throw new Exception("invalid template pattern " + template.Pattern.Input);

            int textIndex = startText;
            int charIndex = startChar;
            for (int i = 0; i < offset; i++)
            {
                if (++charIndex > texts[textIndex].Length)
                {
                    textIndex++;
                    charIndex = 0;
                }
            }

            toMerge.Add((textIndex, charIndex, template.Pattern.Phones));
        }
    }

    class PatternRec
    {
        public int Id;
        public EncodingPattern Pattern;
        public DeterministicAutomaton<char, UnicodeCategoryMatcher> Regexp;

        public void Reset()
        {
            Regexp.Reset();
        }
    }
}
