using Enochian.Lexicons;
using Enochian.Text;

namespace Enochian.Flow.Steps;

public class DTWMatcher(IConfigurable parent, IFlowResources resources) : TextFlowStep(parent, resources)
{
    private static readonly ILogger Logger = Logging.CreateLogger<DTWMatcher>();
    private readonly List<ScoredMatchDefinition> definitions = [];
    private readonly List<ScoredMatchRecord> scoredRecords = [];
    private int processedChunkCount;

    public override ILogger Log => Logger;

    public IList<Lexicon> Lexicons { get; protected set; } = [];

    public HypothesisFile? Hypotheses { get; protected set; }

    public int NumOptions { get; protected set; }

    public double Tolerance { get; set; }

    public ScoredMatchExportOptions? ScoredExport { get; private set; }

    public IReadOnlyList<ScoredMatchRecord> ScoredRecords => scoredRecords;

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

        var export = config.Get<JsonObject>("scoredExport", this);
        if (export != null)
        {
            ScoredExport = ConfigureExport(export);
        }

        return this;
    }

    public override IEnumerable<TextChunk> GetOutputs()
    {
        scoredRecords.Clear();
        definitions.Clear();
        processedChunkCount = 0;
        foreach (var output in base.GetOutputs())
        {
            yield return output;
        }
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
        var chunkIndex = ++processedChunkCount;
        var cache = new Dictionary<string, IEnumerable<SegmentOption>>();
        var optionComparer = new OptionComparer();
        var newLines = new List<TextLine>();
        var sourceLines = input.Lines.Where(line => ReferenceEquals(line.SourceStep, Previous)).ToArray();
        for (var lineIndex = 0; lineIndex < sourceLines.Length; lineIndex++)
        {
            var srcLine = sourceLines[lineIndex];
            Log.LogInformation("matching {Text}", srcLine.Text);
            var segments = new List<TextSegment>();
            for (var segmentIndex = 0; segmentIndex < srcLine.Segments.Count; segmentIndex++)
            {
                var srcSegment = srcLine.Segments[segmentIndex];
                var outputOptions = new List<SegmentOption>();
                var sourceOptions = (srcSegment.Options ?? [])
                    .Where(option => !string.IsNullOrWhiteSpace(option.Text))
                    .ToArray();
                for (var optionIndex = 0; optionIndex < sourceOptions.Length; optionIndex++)
                {
                    var srcOption = sourceOptions[optionIndex];
                    if ((++numTokens % 10) == 0)
                    {
                        Log.LogInformation("matched {Count} tokens", numTokens);
                    }

                    var text = srcOption.Text ?? string.Empty;
                    if (!cache.TryGetValue(text, out var cached))
                    {
                        cached = [.. GetOptions(srcOption)];
                        cache[text] = cached;
                    }

                    var queryId = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}:chunk-{1:D6}:line-{2:D6}:segment-{3:D4}:option-{4:D2}",
                        Id ?? "matcher",
                        chunkIndex,
                        lineIndex + 1,
                        segmentIndex + 1,
                        optionIndex + 1);
                    foreach (var option in cached)
                    {
                        var queryOption = CreateQueryOption(option, srcOption, queryId);
                        outputOptions.Add(queryOption);
                    }
                }

                segments.Add(new TextSegment
                {
                    Text = srcSegment.Options?.FirstOrDefault(option => !string.IsNullOrWhiteSpace(option.Text))?.Text,
                    SourceSegments = [srcSegment],
                    Options = [.. outputOptions.OrderBy(option => option, optionComparer)],
                });
            }

            newLines.Add(new TextLine
            {
                SourceStep = this,
                SourceLine = srcLine,
                Text = srcLine.Text,
                Segments = segments,
            });
        }

        var newChunk = new TextChunk
        {
            Description = input.Description,
            Lines = [.. input.Lines, .. newLines],
        };

        WriteScoredExports();
        Log.LogInformation("matched {Count} total tokens", numTokens);
        return newChunk;
    }

    private ScoredMatchExportOptions? ConfigureExport(JsonObject config)
    {
        var jsonl = GetRequiredExportPath(config, "jsonl");
        var csv = GetRequiredExportPath(config, "csv");
        var metadata = GetRequiredExportPath(config, "metadata");
        var schema = GetRequiredExportPath(config, "schema");
        var definitions = GetOptionalExportPath(config, "definitions");
        return jsonl == null || csv == null || metadata == null || schema == null
            ? null
            : new ScoredMatchExportOptions
            {
                Jsonl = jsonl,
                Csv = csv,
                Metadata = metadata,
                Schema = schema,
                Definitions = definitions,
            };
    }

    private string? GetRequiredExportPath(JsonObject config, string propertyName)
    {
        var path = GetOptionalExportPath(config, propertyName);
        if (path == null)
        {
            _ = AddError("scored export has no '{0}' path", propertyName);
        }

        return path;
    }

    private string? GetOptionalExportPath(JsonObject config, string propertyName)
    {
        var path = config.Get<string>(propertyName, this);
        return string.IsNullOrWhiteSpace(path) ? null : GetChildPath(AbsoluteFilePath, path);
    }

    private SegmentOption CreateQueryOption(SegmentOption option, SegmentOption source, string queryId)
    {
        var queryOption = new SegmentOption
        {
            Tags = option.Tags,
            Entry = option.Entry,
            Encoding = option.Encoding,
            Text = option.Text,
            Phones = option.Phones,
            MatchResult = option.MatchResult,
            WithinLexiconRank = option.WithinLexiconRank,
        };
        if (option.MatchResult == null || option.Entry == null || option.WithinLexiconRank == null)
        {
            return queryOption;
        }

        var lexiconId = option.Entry.Lexicon?.Id ?? string.Empty;
        var recordId = string.Format(
            CultureInfo.InvariantCulture,
            "{0}:{1}:rank-{2:D2}:{3}",
            queryId,
            lexiconId,
            option.WithinLexiconRank.Value,
            option.Entry.EntryId);
        queryOption.ScoredRecordId = recordId;
        scoredRecords.Add(new ScoredMatchRecord(
            ScoredMatchExporter.SchemaVersion,
            recordId,
            GetConfigurationId(),
            queryId,
            source.Text ?? string.Empty,
            source.Phones.Count,
            lexiconId,
            option.Entry.SourceId,
            option.Entry.Language,
            option.Entry.Family,
            option.Entry.EntryId,
            option.Entry.Lemma,
            string.IsNullOrEmpty(option.Entry.Form) ? null : option.Entry.Form,
            option.Entry.Phones.Count,
            option.MatchResult.Cost,
            option.MatchResult.PathLength,
            option.MatchResult.MeanPathCost,
            option.MatchResult.MeanInputLengthCost,
            option.WithinLexiconRank.Value));
        if (!string.IsNullOrWhiteSpace(option.Entry.Definition))
        {
            definitions.Add(new ScoredMatchDefinition(option.Entry.EntryId, option.Entry.Definition));
        }

        return queryOption;
    }

    private string GetConfigurationId()
    {
        IConfigurable configurable = this;
        while (configurable.Parent != null)
        {
            configurable = configurable.Parent;
        }

        return configurable.Id ?? Id ?? "configuration";
    }

    private void WriteScoredExports()
    {
        if (ScoredExport == null)
        {
            return;
        }

        ScoredMatchExporter.Write(
            ScoredExport,
            scoredRecords,
            definitions,
            GetConfigurationId(),
            AbsoluteFilePath,
            typeof(DTWMatcher).Assembly.Location);
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

            foreach (var lexicon in Lexicons)
            {
                double leastBestDistance = double.MaxValue;
                var bestEntries = new List<(Math.DynamicTimeWarpResult Result, LexiconEntry Entry)>();
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

                for (var rank = 0; rank < bestEntries.Count; rank++)
                {
                    var (result, entry) = bestEntries[rank];
                    yield return new SegmentOption
                    {
                        Text = entry.Lemma,
                        Entry = entry,
                        Phones = entry.Phones,
                        MatchResult = result,
                        WithinLexiconRank = rank + 1,
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
            var result = x.Result.Cost.CompareTo(y.Result.Cost);
            if (result != 0)
            {
                return result;
            }

            result = string.Compare(x.Entry.EntryId, y.Entry.EntryId, StringComparison.Ordinal);
            return result != 0
                ? result
                : string.Compare(x.Entry.SourceRecordId, y.Entry.SourceRecordId, StringComparison.Ordinal);
        }
    }
}
