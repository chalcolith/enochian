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
        static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public DTWMatcher(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }

        public override NLog.Logger Log => logger;

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

        public override string GenerateReport(ReportType reportType)
        {
            return string.Format("Lexicon: {0}: {1}<br/>{2}", Lexicon.Id, Lexicon.Description, Lexicon.AbsoluteFilePath);
        }

        protected override TextChunk Process(TextChunk input)
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
                    Log.Info("matching " + srcLine.Text);

                    return new TextLine
                    {
                        SourceStep = this,
                        SourceLine = srcLine,
                        Text = srcLine.Text,                        
                        Segments = srcLine.Segments
                            .Select(srcSegment => new TextSegment
                            {
                                Text = srcSegment.Options?.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o.Text))?.Text,
                                SourceSegments = new List<TextSegment> { srcSegment },
                                Options = srcSegment.Options
                                    .Where(srcOption => !string.IsNullOrWhiteSpace(srcOption.Text))
                                    .SelectMany(srcOption =>
                                    {
                                        if ((++numTokens % 1000) == 0)
                                            Log.Info("matched {0} tokens", numTokens);

                                        if (cache.TryGetValue(srcOption.Text, out IList<SegmentOption> cached))
                                            return cached;

                                        if (srcOption.Phones != null && srcOption.Phones.Any())
                                        {
                                            double leastBestDistance = double.MaxValue;
                                            var bestEntries =
                                                new List<(double, LexiconEntry)>();
                                            foreach (var entry in Lexicon.Entries)
                                            {
                                                double distance = Math.DynamicTimeWarp
                                                    .GetSequenceDistance(srcOption.Phones, entry.Phones,
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
                                                cache[srcOption.Text] = newOptions;
                                                return newOptions;
                                            }
                                        }

                                        return new[] { srcOption };
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
