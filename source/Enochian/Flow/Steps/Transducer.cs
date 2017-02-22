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
        public Transducer(IFlowResources resources)
            : base(resources)
        {
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
            var patterns = OutputEncoding.Patterns
                .OrderByDescending(p => p.Input.Length)
                .Select((p, i) => new PatternRec { Id = i, Pattern = p, Regexp = p.GetRegexp() })
                .ToArray();

            var inputLines = input.Lines.Where(line => line.Encoding == InputEncoding);
            var outputLines = inputLines.Select(line => new Interline
            {
                Encoding = OutputEncoding,
                Segments = line.Segments.Select(seg => ProcessSegment(seg, patterns)).ToList(),
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

            var replacements = patterns.Where(p => p.Pattern.IsReplacement).ToArray();

            var allSegs = new List<Segment>();
            var source = input.Text;

            PatternRec lastRepl = null;
            int start = 0, cur = 0;
            while (cur < source.Length)
            {
                char ch = source[cur];

                bool inRepl = false;
                PatternRec firstRepl = null;
                foreach (var pr in replacements)
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
                        allSegs.Add(new Segment
                        {
                            Text = source.Substring(start, 1 + cur - start),
                            Vectors = new[] { Features.GetUnsetVector() },
                        });

                        start = ++cur; // go to the next character
                    }

                    foreach (var pr in replacements)
                        pr.Reset();
                }
            }

            var templates = patterns.Where(p => !p.Pattern.IsReplacement).ToArray();

            PatternRec lastTempl = null;
            start = cur = 0;
            while (cur < allSegs.Count)
            {
                char ch = allSegs[cur].Text[0];

                bool inTempl = false;
                PatternRec firstTempl = null;
                foreach (var pr in templates)
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

                    foreach (var pr in templates)
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
