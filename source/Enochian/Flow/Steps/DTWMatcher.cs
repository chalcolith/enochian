﻿using System;
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
        public int NumOptions { get; protected set; } = 6;

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

            var numOptions = config.Get<int>("numOptions", null);
            if (numOptions > 0)
                NumOptions = numOptions;

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

            int numTokens = 0;
            var cache = new Dictionary<string, IList<SegmentOption>>();
            var comparer = new EntryComparer();
            var newLines = input.Lines
                .Where(line => object.ReferenceEquals(line.SourceStep, Previous))
                .Select(srcLine =>
                {
                    return new TextLine
                    {
                        SourceStep = this,
                        SourceLine = srcLine,
                        Text = srcLine.Text,                        
                        Segments = srcLine.Segments
                            .Select(oldSegment => new TextSegment
                            {
                                SourceSegments = new List<TextSegment> { oldSegment },
                                Options = oldSegment.Options
                                    .Where(oldOption => !string.IsNullOrWhiteSpace(oldOption.Text))
                                    .SelectMany(oldOption =>
                                    {
                                        if ((++numTokens % 1000) == 0)
                                            Log.Info("matched {0} tokens", numTokens);

                                        if (cache.TryGetValue(oldOption.Text, out IList<SegmentOption> cached))
                                            return cached;

                                        if (oldOption.Phones != null && oldOption.Phones.Any())
                                        {
                                            double leastBestDistance = double.MaxValue;
                                            var bestEntries =
                                                new List<(double, LexiconEntry)>();
                                            foreach (var entry in Lexicon.Entries)
                                            {
                                                double distance = Math.DynamicTimeWarp
                                                    .GetSequenceDistance(oldOption.Phones, entry.Phones,
                                                        Math.DynamicTimeWarp.EuclideanDistance);

                                                if (distance < leastBestDistance
                                                    || bestEntries.Count < NumOptions)
                                                {
                                                    bestEntries.Add((distance, entry));
                                                    bestEntries.Sort(comparer);
                                                    if (bestEntries.Count > NumOptions)
                                                        bestEntries.RemoveAt(bestEntries.Count - 1);
                                                    leastBestDistance = bestEntries.Last().Item1;
                                                }
                                            }

                                            if (bestEntries.Any())
                                            {
                                                var newOptions = bestEntries
                                                    .Select(de => new SegmentOption
                                                    {
                                                        Lexicon = Lexicon,
                                                        Entry = de.Item2,
                                                        Text = de.Item2.Lemma,
                                                        Phones = de.Item2.Phones,
                                                    })
                                                    .ToList();
                                                cache[oldOption.Text] = newOptions;
                                                return newOptions;
                                            }
                                        }

                                        return new[] { oldOption };
                                    })
                                    .ToList()
                            })
                            .ToList(),
                    };
                });

            return new TextChunk
            {                
                Lines = input.Lines.Concat(newLines).ToList(),
            };
        }

        class EntryComparer : IComparer<(double, LexiconEntry)>
        {
            public int Compare((double, LexiconEntry) x, (double, LexiconEntry) y)
            {
                return x.Item1.CompareTo(y.Item1);
            }
        }
    }
}
