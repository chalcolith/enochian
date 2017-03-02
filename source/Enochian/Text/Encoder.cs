using System;
using System.Collections.Generic;
using System.Linq;
using Verophyle.Regexp;
using Verophyle.Regexp.InputSet;

namespace Enochian.Text
{
    class Encoder
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

        public Segment ProcessSegment(Segment input)
        {
            foreach (var pat in Patterns)
                pat.Reset();

            var allSegs = new List<Segment>();
            var source = input.Text;

            PatternRec lastRepl = null;
            int start = 0, cur = 0;
            while (cur < source.Length)
            {
                char ch = source[cur];

                bool inRepl = false;
                PatternRec firstRepl = null;
                foreach (var pr in Replacements)
                {
                    if (pr.Regexp.Failed)
                        continue;

                    pr.Regexp.ProcessInput(ch);
                    if (!pr.Regexp.Failed)
                        inRepl = true;

                    if (pr.Regexp.Succeeded && firstRepl == null)
                        firstRepl = pr;
                }

                if (inRepl)
                {
                    if (firstRepl != null && cur + 1 >= source.Length)
                    {
                        var text = !string.IsNullOrEmpty(firstRepl.Pattern.Output)
                            ? firstRepl.Pattern.Output
                            : firstRepl.Pattern.Input;

                        allSegs.Add(new Segment
                        {
                            Text = text,
                            Vectors = firstRepl.Pattern.Vectors,
                        });
                    }
                    lastRepl = firstRepl;
                    cur++;
                }
                else
                {
                    if (lastRepl != null)
                    {
                        var text = !string.IsNullOrEmpty(lastRepl.Pattern.Output)
                            ? lastRepl.Pattern.Output
                            : lastRepl.Pattern.Input;

                        allSegs.Add(new Segment
                        {
                            Text = text,
                            Vectors = lastRepl.Pattern.Vectors,
                        });
                        lastRepl = null;
                        start = cur; // start again on this character
                    }
                    else
                    {
                        var vectors = !Modifiers.Contains(ch)
                            ? new double[][] { Features.GetUnsetVector() }
                            : new double[][] { new double[0] };

                        allSegs.Add(new Segment
                        {
                            Text = source.Substring(start, 1 + cur - start),
                            Vectors = vectors,
                        });

                        start = ++cur; // go to the next character
                    }

                    foreach (var pr in Replacements)
                        pr.Reset();
                }
            }

            PatternRec lastTempl = null;
            start = cur = 0;
            while (cur < allSegs.Count)
            {
                char ch = allSegs[cur].Text[0];

                bool inTempl = false;
                PatternRec firstTempl = null;
                foreach (var pr in Templates)
                {
                    if (pr.Regexp.Failed)
                        continue;

                    pr.Regexp.ProcessInput(ch);
                    if (!pr.Regexp.Failed)
                        inTempl = true;

                    if (pr.Regexp.Succeeded && firstTempl == null)
                        firstTempl = pr;
                }

                if (inTempl)
                {
                    if (firstTempl != null && cur + 1 >= source.Length)
                    {
                        var offset = firstTempl.Pattern.Input.IndexOf('_');
                        var seg = allSegs[start + offset];
                        seg.Vectors = seg.Vectors
                            .SelectMany(orig =>
                                firstTempl.Pattern.Vectors.Select(pat =>
                                    Features.Override(orig, pat)))
                            .ToArray();
                    }
                    lastTempl = firstTempl;
                    cur++;
                }
                else
                {
                    if (lastTempl != null)
                    {
                        var offset = lastTempl.Pattern.Input.IndexOf('_');
                        var seg = allSegs[start + offset];
                        seg.Vectors = seg.Vectors
                            .SelectMany(orig =>
                                lastTempl.Pattern.Vectors.Select(pat =>
                                    Features.Override(orig, pat)))
                            .ToArray();
                        lastTempl = null;
                        start = cur;
                    }
                    else
                    {
                        start = ++cur;
                    }

                    foreach (var pr in Templates)
                        pr.Reset();
                }
            }

            return new Segment
            {
                Text = string.Join("", allSegs.Select(s => s.Text)),
                Vectors = allSegs.SelectMany(s => s.Vectors.Where(v => v.Length > 0)).ToArray(),
            };
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
