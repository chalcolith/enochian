using Enochian.Lexicons;
using Enochian.Text;

namespace Enochian.Flow.Steps;

public class DTWMatcher(IConfigurable parent, IFlowResources resources) : TextFlowStep(parent, resources)
{
    private static readonly ILogger Logger = Logging.CreateLogger<DTWMatcher>();

    public override ILogger Log => Logger;

    public IList<Lexicon> Lexicons { get; protected set; } = [];

    public HypothesisFile? Hypotheses { get; protected set; }

    public int NumOptions { get; protected set; }

    public double Tolerance { get; set; }

    public override IConfigurable Configure(JsonObject config)
    {
        _ = base.Configure(config);

        Lexicons = [];

        var lexName = config.Get<string>("lexicon", this);
        if (!string.IsNullOrWhiteSpace(lexName))
        {
            var lexicon = Resources.Lexicons.FirstOrDefault(lex => lex.Id == lexName);
            if (lexicon != null)
            {
                Lexicons.Add(lexicon);
            }
            else
            {
                _ = AddError("unable to find lexicon '{0}'", lexName);
            }
        }

        var lexNames = config.Get<IEnumerable<string>>("lexicons", this);
        foreach (var lexsName in lexNames ?? [])
        {
            var lexicon = Resources.Lexicons.FirstOrDefault(lex => lex.Id == lexsName);
            if (lexicon != null)
            {
                Lexicons.Add(lexicon);
            }
            else
            {
                _ = AddError("unable to find lexicon '{0}'", lexsName);
            }
        }

        if (Lexicons.Count == 0)
        {
            _ = AddError("no lexicon specified");
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
        {
            NumOptions = 1;
        }

        if (NumOptions > 20)
        {
            NumOptions = 20;
        }

        Tolerance = config.Get<double>("tolerance", this);
        if (Tolerance < 0.0)
        {
            Tolerance = 0.0;
        }

        if (Tolerance > 1.0)
        {
            Tolerance = 1.0;
        }

        return this;
    }

    public override string GenerateReport(ReportType reportType)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var lexicon in Lexicons)
        {
            var sourcePath = string.IsNullOrWhiteSpace(lexicon.SourcePath)
                ? string.Empty
                : GetChildPath(lexicon.AbsoluteFilePath, lexicon.SourcePath);
            _ = sb.AppendFormat(CultureInfo.InvariantCulture, "&nbsp;&nbsp;Lexicon: {0}: {1}<br/>&nbsp;&nbsp;Path: {2}",
                lexicon.Id, lexicon.Description, sourcePath);
        }
        if (Hypotheses != null)
        {
            _ = sb.AppendFormat(CultureInfo.InvariantCulture, "<br/>&nbsp;&nbsp;Hypotheses: {0}", Hypotheses.AbsoluteFilePath);
        }
        _ = sb.AppendFormat(CultureInfo.InvariantCulture, "<br/>&nbsp;&nbsp;Tolerance: {0}", Tolerance);
        return sb.ToString();
    }

    protected override TextChunk Process(TextChunk input)
    {
        if (Lexicons.Count == 0)
        {
            _ = AddError("no lexicon");
            return input;
        }

        int numTokens = 0;
        var cache = new Dictionary<string, IEnumerable<SegmentOption>>();
        var optionComparer = new OptionComparer();
        var newLines = input.Lines
            .Where(line => ReferenceEquals(line.SourceStep, Previous))
            .Select(srcLine =>
            {
                Log.LogInformation("matching {Text}", srcLine.Text);

                return new TextLine
                {
                    SourceStep = this,
                    SourceLine = srcLine,
                    Text = srcLine.Text,
                    Segments = [.. srcLine.Segments
                        .Select(srcSegment => new TextSegment
                        {
                            Text = srcSegment.Options?.FirstOrDefault(o => !string.IsNullOrWhiteSpace(o.Text))?.Text,
                            SourceSegments = [srcSegment],
                            Options = [.. (srcSegment.Options ?? [])
                                .Where(srcOption => !string.IsNullOrWhiteSpace(srcOption.Text))
                                .SelectMany(srcOption =>
                                {
                                    if ((++numTokens % 10) == 0)
                                    {
                                        Log.LogInformation("matched {Count} tokens", numTokens);
                                    }

                                    var text = srcOption.Text ?? string.Empty;
                                    if (cache.TryGetValue(text, out var cached))
                                    {
                                        return cached;
                                    }

                                    var newOptions = GetOptions(srcOption);
                                    cache[text] = newOptions;
                                    return newOptions;
                                })
                                .OrderBy(o => o, optionComparer)]
                        })],
                };
            });

        var newChunk = new TextChunk
        {
            Description = input.Description,
            Lines = [.. input.Lines, .. newLines],
        };

        Log.LogInformation("matched {Count} total tokens", numTokens);
        return newChunk;
    }

    private IEnumerable<SegmentOption> GetOptions(SegmentOption srcOption)
    {
        if (Hypotheses != null)
        {
            foreach (var hypothesis in Hypotheses.Groups.SelectMany(g => g.Entries))
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

        if (!string.IsNullOrEmpty(srcOption.Text) && srcOption.Phones.Count != 0)
        {
            var entryComparer = new EntryComparer();
            var srcConsonantIndex = GetConsonantIndex(srcOption.Encoding ?? Encoding.Default);
            var srcPhones = ExpandPhones(srcOption.Phones, srcConsonantIndex);

            double leastBestDistance = double.MaxValue;
            var bestEntries = new List<(Math.DynamicTimeWarpResult Result, LexiconEntry Entry)>();
            foreach (var lexicon in Lexicons)
            {
                int consonantIndex = GetConsonantIndex(lexicon.Encoding ?? Encoding.Default);

                foreach (var entry in lexicon.Entries)
                {
                    var entryPhones = ExpandPhones(entry.Phones, consonantIndex);

                    var result = Math.DynamicTimeWarp
                        .GetSequenceResult(srcPhones, entryPhones,
                            Math.DynamicTimeWarp.EuclideanDistance, Tolerance);

                    if (result.Cost < leastBestDistance || bestEntries.Count < NumOptions)
                    {
                        bestEntries.Add((result, entry));
                        bestEntries.Sort(entryComparer);
                        while (bestEntries.Count > NumOptions)
                        {
                            bestEntries.RemoveAt(bestEntries.Count - 1);
                        }

                        leastBestDistance = bestEntries.Last().Result.Cost;
                    }
                }
            }

            if (bestEntries.Count != 0)
            {
                foreach (var (result, entry) in bestEntries)
                {
                    yield return new SegmentOption
                    {
                        Text = entry.Lemma,
                        Entry = entry,
                        Phones = entry.Phones,
                        MatchResult = result,
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

    private static int GetConsonantIndex(Encoding encoding)
    {
        return encoding.Features?.FeatureList.IndexOf("Consonantal,Cons") ?? -1;
    }

    private static List<double[]> ExpandPhones(IEnumerable<double[]> phones, int consonantIndex)
    {
        var result = new List<double[]>();
        foreach (var phone in phones)
        {
            result.Add(phone);
            result.Add(phone);
            if (!(consonantIndex >= 0 && consonantIndex < phone.Length && phone[consonantIndex] == 1.0))
            {
                result.Add(phone);
            }
        }
        return result;
    }

    private sealed class EntryComparer : IComparer<(Math.DynamicTimeWarpResult Result, LexiconEntry Entry)>
    {
        public int Compare(
            (Math.DynamicTimeWarpResult Result, LexiconEntry Entry) x,
            (Math.DynamicTimeWarpResult Result, LexiconEntry Entry) y)
        {
            return x.Result.Cost.CompareTo(y.Result.Cost);
        }
    }
}
