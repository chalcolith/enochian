using System;
using System.Collections.Generic;
using System.Linq;
using Enochian.Text;
using Verophyle.Regexp;
using Verophyle.Regexp.InputSet;

namespace Enochian.Flow.Steps
{
    public class Transducer : FlowStep<TextChunk, TextChunk>
    {
        Lazy<PatternRec[]> patterns;
        PatternRec[] Patterns => patterns.Value;

        Lazy<PatternRec[]> replacements;
        PatternRec[] Replacements => replacements.Value;

        Lazy<PatternRec[]> templates;
        PatternRec[] Templates => templates.Value;

        Lazy<HashSet<char>> modifiers;
        HashSet<char> Modifiers => modifiers.Value;

        public Transducer(IFlowResources resources)
            : base(resources)
        {
            patterns = new Lazy<PatternRec[]>(() => OutputEncoding.Patterns
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

        public FeatureSet Features { get; private set; }

        public Encoding InputEncoding { get; private set; }
        public Encoding OutputEncoding { get; private set; }

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            if (Resources != null)
            {
                var features = config.Get<string>("features", this);
                Features = Resources.FeatureSets.FirstOrDefault(fs => fs.Name == features);
                if (Features == null)
                    AddError("invalid features name '{0}'", features);

                var inputEncoding = config.Get<string>("inputEncoding", this);
                InputEncoding = Resources.Encodings.FirstOrDefault(enc => enc.Name == inputEncoding);
                if (InputEncoding == null)
                    AddError("invalid inputEncoding name '{0}'", inputEncoding);

                var outputEncoding = config.Get<string>("outputEncoding", this);
                OutputEncoding = Resources.Encodings.FirstOrDefault(enc => enc.Name == outputEncoding);
                if (OutputEncoding == null)
                    AddError("invalid outputEncoding name '{0}'", outputEncoding);
            }
            else
            {
                AddError("no resources specified for Transducer");
            }

            return this;
        }

        protected override TextChunk ProcessTyped(TextChunk input)
        {
            var inputLines = input.Lines.Where(line => line.Encoding == InputEncoding);
            var outputLines = inputLines.Select(line => new Interline
            {
                Encoding = OutputEncoding,
                Segments = line.Segments.Select(seg => ProcessSegment(seg, Patterns)).ToList(),
            });
            var output = new TextChunk
            {
                Lines = input.Lines.Concat(outputLines).ToList(),
            };
            return output;
        }

        Segment ProcessSegment(Segment input, PatternRec[] patterns)
        {
            foreach (var pat in patterns)
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
}
