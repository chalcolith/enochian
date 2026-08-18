using System.Text.Json.Serialization;

namespace Enochian.Benchmark;

public sealed record BenchmarkThresholds(
    int MinimumReviewRecords,
    double MinimumReviewAccuracy,
    [property: JsonPropertyName("minimum_recall_at_1")]
    double MinimumRecallAt1,
    [property: JsonPropertyName("minimum_recall_at_5")]
    double MinimumRecallAt5,
    [property: JsonPropertyName("minimum_recall_at_20")]
    double MinimumRecallAt20,
    double MinimumMeanReciprocalRank,
    double MaximumMeanNormalizedDistance);

public sealed record MetricSummary(
    int QueryCount,
    double RecallAt1,
    double RecallAt5,
    double RecallAt20,
    double MeanReciprocalRank,
    double? MeanNormalizedDistance);

public sealed record ThresholdDecision(bool Passed, IReadOnlyList<string> Blockers);

public static class ThresholdEvaluator
{
    public static MetricSummary Summarize(IEnumerable<RetrievalMetrics> metrics)
    {
        var rows = metrics.ToArray();
        return new(
            rows.Length,
            Mean(rows, row => row.RecallAt1),
            Mean(rows, row => row.RecallAt5),
            Mean(rows, row => row.RecallAt20),
            rows.Length == 0 ? 0 : rows.Average(row => row.ReciprocalRank),
            rows.Where(row => row.RelevantNormalizedDistance != null)
                .Select(row => row.RelevantNormalizedDistance!.Value)
                .DefaultIfEmpty(double.NaN)
                .Average() is var mean && !double.IsNaN(mean) ? mean : null);
    }

    public static ThresholdDecision Evaluate(MetricSummary summary, BenchmarkThresholds thresholds)
    {
        var blockers = new List<string>();
        AddBelow(blockers, "recall_at_1", summary.RecallAt1, thresholds.MinimumRecallAt1);
        AddBelow(blockers, "recall_at_5", summary.RecallAt5, thresholds.MinimumRecallAt5);
        AddBelow(blockers, "recall_at_20", summary.RecallAt20, thresholds.MinimumRecallAt20);
        AddBelow(blockers, "mean_reciprocal_rank", summary.MeanReciprocalRank, thresholds.MinimumMeanReciprocalRank);
        if (summary.MeanNormalizedDistance == null ||
            summary.MeanNormalizedDistance > thresholds.MaximumMeanNormalizedDistance)
        {
            blockers.Add("mean_normalized_distance");
        }

        return new(blockers.Count == 0, blockers);
    }

    private static double Mean(RetrievalMetrics[] rows, Func<RetrievalMetrics, bool> select) =>
        rows.Length == 0 ? 0 : rows.Count(select) / (double)rows.Length;

    private static void AddBelow(List<string> blockers, string name, double actual, double threshold)
    {
        if (actual < threshold)
        {
            blockers.Add(name);
        }
    }
}
