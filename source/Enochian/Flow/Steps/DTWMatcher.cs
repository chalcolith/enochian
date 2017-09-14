using System;
using System.Collections.Generic;
using System.Linq;
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

        public IList<Lexicon> Lexicons { get; protected set; }

        public HypothesisFile Hypotheses { get; protected set; }

        public int NumOptions { get; protected set; }

        public double Tolerance { get; set; }

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            Lexicons = new List<Lexicon>();

            var lexName = config.Get<string>("lexicon", this);
            if (!string.IsNullOrWhiteSpace(lexName))
            {
                var lexicon = Resources.Lexicons.FirstOrDefault(lex => lex.Id == lexName);
                if (lexicon != null)
                    Lexicons.Add(lexicon);
                else
                    AddError("unable to find lexicon '{0}", lexicon);
            }

            var lexNames = config.Get<IEnumerable<string>>("lexicons", this);
            foreach (var lexsName in lexNames ?? Enumerable.Empty<string>())
            {
                var lexicon = Resources.Lexicons.FirstOrDefault(lex => lex.Id == lexName);
                if (lexicon != null)
                    Lexicons.Add(lexicon);
                else
                    AddError("unable to find lexicon '{0}'", lexicon);
            }

            if (Lexicons.Count == 0)
            {
                AddError("no lexicon specified");
            }

            var hypotheses = config.Get<string>("hypotheses", this);
            if (!string.IsNullOrWhiteSpace(hypotheses))
            {
                var hypothesesFile = new HypothesisFile(this, Resources)
                {
                    RelativePath = hypotheses,
                };
                Hypotheses = Load(this, hypothesesFile, hypotheses);
            }

            NumOptions = config.Get<int>("numOptions", this);
            if (NumOptions <= 0)
                NumOptions = 1;
            if (NumOptions > 20)
                NumOptions = 20;

            Tolerance = config.Get<double>("tolerance", this);
            if (Tolerance < 0.0) Tolerance = 0.0;
            if (Tolerance > 1.0) Tolerance = 1.0;

            return this;
        }

        public override string GenerateReport(ReportType reportType)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var lexicon in Lexicons)
            {
                sb.AppendFormat("&nbsp;&nbsp;Lexicon: {0}: {1}<br/>&nbsp;&nbsp;Path: {2}",
                lexicon.Id, lexicon.Description, GetChildPath(lexicon.AbsoluteFilePath, lexicon.SourcePath));
            }
            if (Hypotheses != null)
            {
                sb.AppendFormat("&nbsp;&nbsp;Hypotheses: {0}", Hypotheses.AbsoluteFilePath);
            }
            sb.AppendFormat("&nbsp;&nbsp;Tolerance: {0}", Tolerance);
            return sb.ToString();
        }

        protected override TextChunk Process(TextChunk input)
        {
            if (Lexicons == null || Lexicons.Count == 0)
            {
                AddError("no lexicon");
                return input;
            }

            int numTokens = 0;
            var cache = new Dictionary<string, IEnumerable<SegmentOption>>();
            var optionComparer = new OptionComparer();
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
                                        if ((++numTokens % 10) == 0)
                                            Log.Info("matched {0} tokens", numTokens);

                                        if (cache.TryGetValue(srcOption.Text, out IEnumerable<SegmentOption> cached))
                                            return cached;
                                        var newOptions = GetOptions(srcOption);
                                        cache[srcOption.Text] = newOptions;
                                        return newOptions;
                                    })
                                    .OrderBy(o => o, optionComparer)
                                    .ToList()
                            })
                            .ToList(),
                    };
                });

            var newChunk = new TextChunk
            {                
                Description = input.Description,
                Lines = input.Lines.Concat(newLines).ToList(),
            };

            Log.Info("matched {0} total tokens", numTokens);
            return newChunk;
        }

        IEnumerable<SegmentOption> GetOptions(SegmentOption srcOption)
        {
            if (Hypotheses != null && Hypotheses.Groups != null)
            {
                foreach (var hypothesis in Hypotheses.Groups.Where(g => g.Entries != null).SelectMany(g => g.Entries))
                {
                    if (srcOption.Text == hypothesis.Input)
                    {
                        yield return new SegmentOption
                        {
                            Encoding = Hypotheses.Encoding,
                            Text = srcOption.Text,
                            Entry = new LexiconEntry { Lemma = hypothesis.Lemma, Definition = hypothesis.Definition },
                            Tags = TextTag.Hypo,
                        };
                    }
                }
            }

            if (!(string.IsNullOrEmpty(srcOption.Text) || srcOption.Phones == null || srcOption.Phones.Count == 0))
            {
                var entryComparer = new EntryComparer();
                var srcConsonantIndex = GetConsonantIndex(srcOption.Encoding ?? Encoding.Default);
                var srcPhones = ExpandPhones(srcOption.Phones, srcConsonantIndex);
               
                double leastBestDistance = double.MaxValue;
                var bestEntries = new List<(double, LexiconEntry)>();
                foreach (var lexicon in Lexicons)
                {
                    int consonantIndex = GetConsonantIndex(lexicon.Encoding ?? Encoding.Default);

                    foreach (var entry in lexicon.Entries)
                    {
                        var entryPhones = ExpandPhones(entry.Phones, consonantIndex);

                        double distance = Math.DynamicTimeWarp
                            .GetSequenceDistance(srcPhones, entryPhones,
                                Math.DynamicTimeWarp.EuclideanDistance, Tolerance);

                        if (distance < leastBestDistance || bestEntries.Count < NumOptions)
                        {
                            bestEntries.Add((distance, entry));
                            bestEntries.Sort(entryComparer);
                            while (bestEntries.Count > NumOptions)
                                bestEntries.RemoveAt(bestEntries.Count - 1);
                            leastBestDistance = bestEntries.Last().Item1;
                        }
                    }
                }

                if (bestEntries.Any())
                {
                    foreach (var de in bestEntries)
                    {
                        yield return new SegmentOption
                        {
                            Text = de.Item2.Lemma,
                            Entry = de.Item2,
                            Phones = de.Item2.Phones,
                            Tags = TextTag.Match,
                        };
                    }
                }

                yield break;
            }

            yield return new SegmentOption
            {
                Text = srcOption.Text,
                Encoding = srcOption.Encoding,
                Entry = srcOption.Entry,
                Phones = srcOption.Phones,
                Tags = srcOption.Tags,
            };
        }

        int GetConsonantIndex(Encoding encoding)
        {
            return encoding?.Features?.FeatureList?.IndexOf("Consonantal,Cons") ?? -1;
        }

        static IList<double[]> ExpandPhones(IEnumerable<double[]> phones, int consonantIndex)
        {
            var result = new List<double[]>();
            foreach (var phone in phones)
            {
                result.Add(phone);
                result.Add(phone);
                if (!(consonantIndex >= 0 && consonantIndex < phone.Length && phone[consonantIndex] == 1.0))
                    result.Add(phone);
            }
            return result;
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
