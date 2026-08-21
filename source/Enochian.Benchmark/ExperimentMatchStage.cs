using Enochian.Math;
using Enochian.Text;
using System.Text;
using System.Text.Json;
using IpaEncoder = Enochian.Text.Encoder;

namespace Enochian.Benchmark;

public sealed class ExperimentMatchStage(FeatureSet features, Text.Encoding encoding)
{
    private readonly IpaEncoder encoder = new(features, encoding);

    public ExperimentMatchResult Run(
        string samplingProtocolPath,
        IReadOnlyDictionary<string, string> families,
        double tolerance = 0)
    {
        var protocol = SamplingProtocol.Load(samplingProtocolPath);
        var root = Path.GetDirectoryName(samplingProtocolPath)!;
        var queries = SamplingInputLoader.LoadQueries(Resolve(protocol.QueriesPath, root));
        var scores = new List<ExperimentMatchRecord>();
        foreach (var analysis in protocol.Analyses.OrderBy(value => value.AnalysisId, StringComparer.Ordinal))
        {
            var memberships = LoadLines<SamplingMembership>(Resolve(analysis.Outputs.Memberships, root));
            var nulls = LoadLines<SequenceNullRecord>(Resolve(analysis.Outputs.Nulls, root));
            var samples = memberships.GroupBy(value => (value.SampleId, value.Language))
                .ToDictionary(group => group.Key, group => group.OrderBy(value => value.CandidateId, StringComparer.Ordinal).ToArray());
            foreach (var sample in samples.OrderBy(value => value.Key.SampleId, StringComparer.Ordinal)
                .ThenBy(value => value.Key.Language, StringComparer.Ordinal))
            {
                if (!families.TryGetValue(sample.Key.Language, out var family))
                {
                    throw new InvalidDataException($"No family is configured for language '{sample.Key.Language}'.");
                }

                var candidates = sample.Value.Select(value => (value.CandidateId, Phones: Encode(value.Phonology))).ToArray();
                foreach (var query in queries)
                {
                    double[][] observed = [.. query.Symbols.Select(symbol => protocol.Mapping.TryGetValue(symbol, out var phone)
                        ? phone
                        : throw new InvalidDataException($"Query '{query.QueryId}' has unmapped symbol '{symbol}'."))];
                    AddScores(scores, analysis.AnalysisId, "type-primary", sample.Value[0], query, sample.Key.Language,
                        family, false, null, null, observed, 1, candidates, tolerance);
                    AddScores(scores, analysis.AnalysisId, "token-weighted", sample.Value[0], query, sample.Key.Language,
                        family, false, null, null, observed, query.TokenFrequency, candidates, tolerance);
                }

                foreach (var value in nulls.Where(value => value.SampleId == sample.Key.SampleId &&
                    (value.Language is "all" || value.Language == sample.Key.Language)))
                {
                    var query = queries.Single(query => query.QueryId == value.QueryId);
                    AddScores(scores, analysis.AnalysisId, value.AnalysisMode, sample.Value[0], query,
                        sample.Key.Language, family, true, value.NullId, value.NullKind, value.Phones,
                        value.Weight, candidates, tolerance);
                }
            }
        }

        var ordered = scores.OrderBy(value => value.AnalysisId, StringComparer.Ordinal)
            .ThenBy(value => value.AnalysisMode, StringComparer.Ordinal)
            .ThenBy(value => value.RequestedSize)
            .ThenBy(value => value.SampleId, StringComparer.Ordinal)
            .ThenBy(value => value.QueryId, StringComparer.Ordinal)
            .ThenBy(value => value.Language, StringComparer.Ordinal)
            .ThenBy(value => value.IsNull)
            .ThenBy(value => value.NullId, StringComparer.Ordinal)
            .ThenBy(value => value.WithinSampleRank)
            .ThenBy(value => value.CandidateId, StringComparer.Ordinal)
            .ToArray();
        var nearest = ordered.Where(value => value.WithinSampleRank == 1)
            .Select(value => new NearestDistanceRecord(
                "1.0.0", value.AnalysisId, value.AnalysisMode, value.SampleId, value.RequestedSize,
                value.Repetition, value.QueryId, value.QueryLength, value.Section, value.FrequencyBand,
                value.Weight, value.Language, value.Family, value.IsNull, value.NullKind, value.MeanPathCost))
            .ToArray();
        return new(ordered, nearest);
    }

    private static void AddScores(
        List<ExperimentMatchRecord> scores,
        string analysisId,
        string analysisMode,
        SamplingMembership membership,
        SamplingQuery query,
        string language,
        string family,
        bool isNull,
        string? nullId,
        string? nullKind,
        IReadOnlyList<double[]> queryPhones,
        int weight,
        IReadOnlyList<(string CandidateId, IReadOnlyList<double[]> Phones)> candidates,
        double tolerance)
    {
        var ranked = candidates.Select(candidate =>
            {
                var result = DynamicTimeWarp.GetSequenceResult(
                    queryPhones, candidate.Phones, DynamicTimeWarp.EuclideanDistance, tolerance);
                return (candidate.CandidateId, Result: result);
            })
            .OrderBy(value => value.Result.MeanPathCost)
            .ThenBy(value => value.CandidateId, StringComparer.Ordinal)
            .ToArray();
        scores.AddRange(ranked.Select((value, index) => new ExperimentMatchRecord(
            "1.0.0", analysisId, analysisMode, membership.SampleId, membership.RequestedSize,
            membership.Repetition, query.QueryId, queryPhones.Count, query.Section, query.FrequencyBand,
            weight, language, family, isNull, nullId, nullKind, value.CandidateId, value.Result.Cost,
            value.Result.PathLength, value.Result.MeanPathCost, value.Result.MeanInputLengthCost, index + 1)));
    }

    private IReadOnlyList<double[]> Encode(string ipa)
    {
        var (_, _, phones) = encoder.GetTextAndPhones(ipa, out var unknown);
        if (unknown.Count != 0 || phones.Count == 0)
        {
            throw new InvalidDataException($"Candidate phonology '{ipa}' contains unknown or empty IPA.");
        }

        return [.. phones];
    }

    private static IReadOnlyList<T> LoadLines<T>(string path) =>
        [.. File.ReadLines(path, new UTF8Encoding(false, true))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => JsonSerializer.Deserialize<T>(line, BenchmarkProtocol.LineSerializerOptions)
                ?? throw new InvalidDataException($"Unable to deserialize a row from '{path}'."))];

    private static string Resolve(string path, string root) =>
        Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
}
