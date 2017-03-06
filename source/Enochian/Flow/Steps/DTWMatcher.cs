using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Enochian.Lexicons;
using Enochian.Text;

namespace Enochian.Flow.Steps
{
    public class DTWMatcher : TextFlowStep
    {
        public DTWMatcher(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }

        public Lexicon Lexicon { get; protected set; }

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            var lexicon = config.Get<string>("lexicon", this);
            if (!string.IsNullOrWhiteSpace(lexicon))
            {
                Lexicon = Resources.Lexicons.FirstOrDefault(lex => lex.Id == lexicon);
                if (Lexicon == null)
                    AddError("unable to find lexicon '{0}", lexicon);
            }
            else
            {
                AddError("no lexicon specified");
            }

            return this;
        }

        protected override TextChunk ProcessTyped(TextChunk input)
        {
            if (Lexicon == null)
            {
                AddError("no lexicon");
                return input;
            }

            if (Lexicon.Entries == null || Lexicon.Entries.Count == 0)
            {
                AddError("lexicon has no entries");
                return input;
            }

            var cache = new Dictionary<string, Segment>();
            var newLines = input.Lines
                .Where(line => line.SourceStep == Previous)
                .Select(oldLine =>
            {
                return new Interline
                {
                    Segments = oldLine.Segments.Select(oldSegment =>
                    {
                        if (cache.TryGetValue(oldSegment.Text, out Segment cached))
                        {
                            return cached;
                        }
                        else if (oldSegment.Vectors != null && oldSegment.Vectors.Any())
                        {
                            double bestDistance = double.MaxValue;
                            LexiconEntry bestEntry = null;

                            foreach (var entry in Lexicon.Entries)
                            {
                                double distance = Math.DynamicTimeWarp
                                    .GetSequenceDistance(oldSegment.Vectors, entry.Vectors,
                                        Math.DynamicTimeWarp.EuclideanDistance);

                                if (distance < bestDistance)
                                {
                                    bestDistance = distance;
                                    bestEntry = entry;
                                }
                            }

                            if (bestEntry != null)
                            {
                                var newSegment = new Segment
                                {
                                    SourceSegment = oldSegment,
                                    Lexicon = Lexicon,
                                    Entry = bestEntry,
                                    Text = bestEntry.Lemma,
                                    Vectors = bestEntry.Vectors,
                                };

                                cache[oldSegment.Text] = newSegment;
                                return newSegment;
                            }
                        }
                        return oldSegment;
                    })
                    .ToList()
                };
            });

            return new TextChunk
            {
                Lines = input.Lines.Concat(newLines).ToList(),
            };
        }
    }
}
