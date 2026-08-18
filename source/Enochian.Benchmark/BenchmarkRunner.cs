using Enochian.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using IpaEncoder = Enochian.Text.Encoder;

namespace Enochian.Benchmark;

public sealed class BenchmarkRunner(string repositoryRoot, FeatureSet features, Text.Encoding encoding)
{
    private readonly string repositoryRoot = Path.GetFullPath(repositoryRoot);
    private readonly IpaEncoder encoder = new(features, encoding);

    public int Run(string protocolPath)
    {
        var resolvedProtocol = Resolve(protocolPath, repositoryRoot);
        var protocol = BenchmarkProtocol.Load(resolvedProtocol);
        var protocolDirectory = Path.GetDirectoryName(resolvedProtocol)!;
        var profiles = protocol.DegradationProfiles
            .Select(path => LoadProfile(Resolve(path, protocolDirectory)))
            .OrderBy(profile => profile.Id, StringComparer.Ordinal)
            .ToArray();
        var blockers = new List<string>();
        var scores = new List<BenchmarkScoreRow>();
        var reviews = new SortedDictionary<string, ReviewSummary>(StringComparer.Ordinal);
        var sourceSamples = new List<(BenchmarkSource Source, IReadOnlyList<BenchmarkEntry> Entries)>();
        foreach (var source in protocol.Sources.OrderBy(source => source.SourceId, StringComparer.Ordinal))
        {
            var lexiconPath = Resolve(source.LexiconPath, protocolDirectory);
            var reviewPath = Resolve(source.ReviewPath, protocolDirectory);
            if (!File.Exists(lexiconPath))
            {
                if (source.Required)
                {
                    blockers.Add($"{source.SourceId}:missing_lexicon");
                }

                continue;
            }

            if (!File.Exists(reviewPath))
            {
                if (source.Required)
                {
                    blockers.Add($"{source.SourceId}:missing_review");
                }

                continue;
            }

            var entries = BenchmarkEntryLoader.Load(
                lexiconPath,
                source.Language,
                encoder,
                protocol.MinimumPhonemes,
                protocol.MaximumPhonemes);
            var sampled = BenchmarkSampling.Sample(entries, protocol.SamplesPerStratum, protocol.SamplingSeed);
            var review = ReviewEvaluator.Evaluate(reviewPath, protocol.Thresholds);
            reviews[source.SourceId] = review;
            if (source.Required && source.Confirmatory && !review.Passed)
            {
                blockers.AddRange(review.Blockers.Select(blocker => $"{source.SourceId}:{blocker}"));
            }

            if (source.Required && entries.Count == 0)
            {
                blockers.Add($"{source.SourceId}:no_eligible_entries");
            }

            foreach (var requiredBand in protocol.RequiredLengthBands)
            {
                if (source.Required && source.Confirmatory && !sampled.Any(entry => entry.LengthBand == requiredBand))
                {
                    blockers.Add($"{source.SourceId}:missing_length_band:{requiredBand}");
                }
            }

            sourceSamples.Add((source, sampled));
        }

        var candidates = sourceSamples
            .SelectMany(sample => sample.Entries)
            .OrderBy(entry => entry.EntryId, StringComparer.Ordinal)
            .ToArray();
        foreach (var (source, sampled) in sourceSamples)
        {
            foreach (var profile in profiles)
            {
                foreach (var entry in sampled)
                {
                    var query = CreateQuery(entry, candidates, profile);
                    scores.Add(CreateScore(protocol, source, profile, query, candidates, false));
                    scores.Add(CreateScore(protocol, source, profile, query, candidates, true));
                }
            }
        }

        var summaries = CreateSummaries(protocol, scores).ToArray();
        var confirmatoryLanguages = protocol.Sources
            .Where(source => source.Required && source.Confirmatory)
            .Select(source => source.Language)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var summary in summaries.Where(summary =>
            !summary.Passed &&
            summary.Scope == "language_length_band" &&
            summary.CandidateMode == "source-included" &&
            summary.Language != null &&
            confirmatoryLanguages.Contains(summary.Language)))
        {
            blockers.AddRange(summary.Blockers.Select(blocker =>
                $"{summary.Language ?? "aggregate"}:{summary.LengthBand ?? "all"}:{summary.ProfileId}:{summary.CandidateMode}:{blocker}"));
        }

        WriteJsonLines(Resolve(protocol.Outputs.Scores, protocolDirectory), scores);
        WriteJsonLines(Resolve(protocol.Outputs.Summaries, protocolDirectory), summaries);
        var distinctBlockers = blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        WriteJson(Resolve(protocol.Outputs.QualityReport, protocolDirectory), new BenchmarkQualityReport
        {
            BenchmarkId = protocol.BenchmarkId,
            Passed = distinctBlockers.Length == 0,
            Blockers = distinctBlockers,
            Reviews = reviews,
            ScoreRows = scores.Count,
            SummaryRows = summaries.Length,
        });
        return distinctBlockers.Length == 0 ? 0 : 1;
    }

    private BenchmarkQuery CreateQuery(
        BenchmarkEntry entry,
        IReadOnlyList<BenchmarkEntry> entries,
        DegradationProfile profile)
    {
        var relevant = entries
            .Where(candidate => candidate.Source == entry.Source && candidate.SourceRecordId == entry.SourceRecordId)
            .Select(candidate => candidate.EntryId)
            .ToHashSet(StringComparer.Ordinal);
        return new(
            entry.EntryId,
            entry.Source,
            entry.SourceRecordId,
            entry.Language,
            BenchmarkDegrader.Apply(entry.Phones, profile, features),
            relevant,
            entry.LengthBand,
            entry.UnusualCategory);
    }

    private static BenchmarkScoreRow CreateScore(
        BenchmarkProtocol protocol,
        BenchmarkSource source,
        DegradationProfile profile,
        BenchmarkQuery query,
        IReadOnlyList<BenchmarkEntry> entries,
        bool excludeSourceRecord)
    {
        var metrics = RetrievalEvaluator.Evaluate(RetrievalEvaluator.Rank(query, entries, excludeSourceRecord));
        return new(
            protocol.BenchmarkId,
            profile.Id,
            profile.Version,
            excludeSourceRecord ? "source-excluded" : "source-included",
            query.QueryId,
            source.SourceId,
            source.Language,
            source.Confirmatory,
            query.LengthBand,
            query.UnusualCategory,
            query.Phones.Count,
            metrics.RecallAt1,
            metrics.RecallAt5,
            metrics.RecallAt20,
            metrics.ReciprocalRank,
            metrics.RelevantRank,
            metrics.RelevantNormalizedDistance,
            metrics.NearestNormalizedDistance,
            metrics.CandidateCount);
    }

    private static IEnumerable<BenchmarkSummaryRow> CreateSummaries(
        BenchmarkProtocol protocol,
        IReadOnlyList<BenchmarkScoreRow> scores)
    {
        foreach (var profileGroup in scores.GroupBy(row => (row.ProfileId, row.ProfileVersion, row.CandidateMode)))
        {
            yield return Summarize(protocol, "aggregate", null, null, profileGroup);
            foreach (var languageGroup in profileGroup.GroupBy(row => row.Language).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                yield return Summarize(protocol, "language", languageGroup.Key, null, languageGroup);
            }

            foreach (var bandGroup in profileGroup.GroupBy(row => row.LengthBand).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                yield return Summarize(protocol, "length_band", null, bandGroup.Key, bandGroup);
            }

            foreach (var group in profileGroup.GroupBy(row => (row.Language, row.LengthBand))
                .OrderBy(group => group.Key.Language, StringComparer.Ordinal)
                .ThenBy(group => group.Key.LengthBand, StringComparer.Ordinal))
            {
                yield return Summarize(protocol, "language_length_band", group.Key.Language, group.Key.LengthBand, group);
            }
        }
    }

    private static BenchmarkSummaryRow Summarize(
        BenchmarkProtocol protocol,
        string scope,
        string? language,
        string? lengthBand,
        IEnumerable<BenchmarkScoreRow> source)
    {
        var rows = source.ToArray();
        var metrics = rows.Select(row => new RetrievalMetrics(
            row.RecallAt1,
            row.RecallAt5,
            row.RecallAt20,
            row.ReciprocalRank,
            row.RelevantRank,
            row.RelevantNormalizedDistance,
            row.NearestNormalizedDistance,
            row.CandidateCount));
        var summary = ThresholdEvaluator.Summarize(metrics);
        var decision = ThresholdEvaluator.Evaluate(summary, protocol.Thresholds);
        return new(
            protocol.BenchmarkId,
            scope,
            language,
            lengthBand,
            rows[0].ProfileId,
            rows[0].ProfileVersion,
            rows[0].CandidateMode,
            summary.QueryCount,
            summary.RecallAt1,
            summary.RecallAt5,
            summary.RecallAt20,
            summary.MeanReciprocalRank,
            summary.MeanNormalizedDistance,
            decision.Passed,
            decision.Blockers);
    }

    private static DegradationProfile LoadProfile(string path) =>
        LoadValidatedProfile(path);

    private static DegradationProfile LoadValidatedProfile(string path)
    {
        var json = File.ReadAllText(path);
        BenchmarkProtocol.Validate(json, path);
        return JsonSerializer.Deserialize<DegradationProfile>(json, BenchmarkProtocol.SerializerOptions)
            ?? throw new InvalidDataException($"Unable to deserialize degradation profile '{path}'.");
    }

    private static string Resolve(string path, string root) =>
        Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));

    private static void WriteJsonLines<T>(string path, IEnumerable<T> rows) =>
        WriteAtomically(path, temporary =>
        {
            using var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)) { NewLine = "\n" };
            foreach (var row in rows)
            {
                writer.WriteLine(JsonSerializer.Serialize(row, BenchmarkProtocol.LineSerializerOptions));
            }
        });

    private static void WriteJson<T>(string path, T value) =>
        WriteAtomically(path, temporary => File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(value, BenchmarkProtocol.SerializerOptions).ReplaceLineEndings("\n") + "\n",
            new UTF8Encoding(false)));

    private static void WriteAtomically(string path, Action<string> write)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            write(temporary);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
