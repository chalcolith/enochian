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
                pat.Regexp.Reset();

            var replacements = patterns.Where(p => p.Pattern.IsReplacement).ToArray();

            PatternRec lastRepl = null;
            var allSegs = new List<Segment>();
            var source = input.Text;
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
                    if (pr.Regexp.Succeeded)
                    {
                        inRepl = true;
                        if (firstRepl == null)
                            firstRepl = pr;
                    }
                }

                if (inRepl)
                {
                    if (cur + 1 >= source.Length)
                    {
                        allSegs.Add(new Segment
                        {
                            Text = source.Substring(start, 1 + cur - start),
                            Vectors = new[] { firstRepl.Pattern.Vector },
                        });
                    }
                    lastRepl = firstRepl;
                    cur++;
                }
                else
                {
                    if (lastRepl != null)
                    {
                        allSegs.Add(new Segment
                        {
                            Text = source.Substring(start, cur - start),
                            Vectors = new[] { lastRepl.Pattern.Vector },
                        });
                        lastRepl = null;
                        start = cur; // start again on this character
                    }
                    else
                    {
                        allSegs.Add(new Segment
                        {
                            Text = source.Substring(start, cur - start),
                            Vectors = new[] { new double[0] },
                        });

                        start = ++cur; // go to the next character
                    }

                    foreach (var pr in replacements)
                        pr.Reset();
                }
            }

            return new Segment
            {
                Text = string.Join("", allSegs.Select(s => s.Text)),
                Vectors = allSegs.SelectMany(s => s.Vectors.Where(v => v.Length > 0)).ToArray(),
            };
        }

        private void ApplyTemplateVector(EncodingPattern pattern, List<double[]> allVecs, IList<char> templChars, IList<double[]> templVecs)
        {
            var index = pattern.Input.IndexOf('_');
            if (index < templVecs.Count)
                AddError("unable to apply template '{0}' to '{1}'", pattern.Input, new string(templChars.ToArray()));
            else
                templVecs[index] = Features.Override(templVecs[index], pattern.Vector);
            allVecs.AddRange(templVecs);
            templVecs.Clear();
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
