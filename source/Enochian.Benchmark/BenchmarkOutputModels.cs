using System.Text.Json.Serialization;

namespace Enochian.Benchmark;

public sealed record BenchmarkScoreRow(
    string BenchmarkId,
    string ProfileId,
    string ProfileVersion,
    string CandidateMode,
    string QueryId,
    string SourceId,
    string Language,
    bool Confirmatory,
    string LengthBand,
    string UnusualCategory,
    int QueryPhonemeLength,
    [property: JsonPropertyName("recall_at_1")]
    bool RecallAt1,
    [property: JsonPropertyName("recall_at_5")]
    bool RecallAt5,
    [property: JsonPropertyName("recall_at_20")]
    bool RecallAt20,
    double ReciprocalRank,
    int? RelevantRank,
    double? RelevantNormalizedDistance,
    double? NearestNormalizedDistance,
    int CandidateCount);

public sealed record BenchmarkSummaryRow(
    string BenchmarkId,
    string Scope,
    string? Language,
    string? LengthBand,
    string ProfileId,
    string ProfileVersion,
    string CandidateMode,
    int QueryCount,
    [property: JsonPropertyName("recall_at_1")]
    double RecallAt1,
    [property: JsonPropertyName("recall_at_5")]
    double RecallAt5,
    [property: JsonPropertyName("recall_at_20")]
    double RecallAt20,
    double MeanReciprocalRank,
    double? MeanNormalizedDistance,
    bool Passed,
    IReadOnlyList<string> Blockers);

public sealed class BenchmarkQualityReport
{
    public string SchemaVersion { get; init; } = "1.0.0";

    public string BenchmarkId { get; init; } = string.Empty;

    public bool Passed { get; init; }

    public IReadOnlyList<string> Blockers { get; init; } = [];

    public IReadOnlyDictionary<string, ReviewSummary> Reviews { get; init; } =
        new SortedDictionary<string, ReviewSummary>(StringComparer.Ordinal);

    public int ScoreRows { get; init; }

    public int SummaryRows { get; init; }
}
