namespace Enochian.Benchmark;

public static class RetrievalEvaluator
{
    public static IReadOnlyList<RankedCandidate> Rank(
        BenchmarkQuery query,
        IEnumerable<BenchmarkEntry> candidates,
        bool excludeSourceRecord)
    {
        return
        [
            .. candidates
                .Where(candidate => !excludeSourceRecord ||
                    candidate.Source != query.Source ||
                    candidate.SourceRecordId != query.SourceRecordId)
                .Select(candidate =>
                {
                    var measurement = BenchmarkDtw.Measure(query.Phones, candidate.Phones);
                    return new RankedCandidate(
                        candidate.EntryId,
                        measurement.Cost,
                        measurement.PathLength,
                        measurement.MeanPathCost,
                        query.RelevantEntryIds.Contains(candidate.EntryId));
                })
                .OrderBy(candidate => candidate.NormalizedDistance)
                .ThenBy(candidate => candidate.EntryId, StringComparer.Ordinal),
        ];
    }

    public static RetrievalMetrics Evaluate(IReadOnlyList<RankedCandidate> ranking)
    {
        var relevantIndex = -1;
        for (var index = 0; index < ranking.Count; index++)
        {
            if (ranking[index].Relevant)
            {
                relevantIndex = index;
                break;
            }
        }

        int? rank = relevantIndex < 0 ? null : relevantIndex + 1;
        return new(
            rank <= 1,
            rank <= 5,
            rank <= 20,
            rank == null ? 0 : 1.0 / rank.Value,
            rank,
            relevantIndex < 0 ? null : ranking[relevantIndex].NormalizedDistance,
            ranking.Count == 0 ? null : ranking[0].NormalizedDistance,
            ranking.Count);
    }
}
