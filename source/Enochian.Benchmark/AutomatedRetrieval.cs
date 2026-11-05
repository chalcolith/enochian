using Enochian.Text;

namespace Enochian.Benchmark;

public static class AutomatedRetrieval
{
    public static AutomatedRetrievalCell[] Evaluate(
        EvidenceUnit[] units,
        AutomatedProtocol protocol,
        BenchmarkProtocol retrieval,
        DegradationProfile[] degradations,
        FeatureSet features)
    {
        var cells = new List<AutomatedRetrievalCell>();
        foreach (var degradation in degradations)
        {
            foreach (var band in retrieval.RequiredLengthBands)
            {
                var sample = units.Where(unit => unit.LengthBand == band)
                    .OrderBy(unit => BalancedSampler.StableKey(protocol.SamplingSeed, "retrieval", unit.Id), StringComparer.Ordinal)
                    .ThenBy(unit => unit.Id, StringComparer.Ordinal).Take(retrieval.SamplesPerStratum).ToArray();
                foreach (var excluded in new[] { false, true })
                {
                    var values = sample.SelectMany(unit => unit.Readings.Select(reading =>
                    {
                        var query = BenchmarkDegrader.Apply(reading.Phones, degradation, features);
                        var ranking = units.Where(candidate => !excluded || candidate.Id != unit.Id)
                            .Select(candidate => new RankedCandidate(candidate.Id, 0, 0,
                                Distance(query, candidate), candidate.Id == unit.Id))
                            .OrderBy(candidate => candidate.NormalizedDistance)
                            .ThenBy(candidate => candidate.EntryId, StringComparer.Ordinal).ToArray();
                        return (reading.Weight, Metrics: RetrievalEvaluator.Evaluate(ranking));
                    })).ToArray();
                    var total = values.Sum(value => value.Weight);
                    var summary = new MetricSummary(sample.Length,
                        total == 0 ? 0 : values.Sum(value => value.Weight * (value.Metrics.RecallAt1 ? 1 : 0)) / total,
                        total == 0 ? 0 : values.Sum(value => value.Weight * (value.Metrics.RecallAt5 ? 1 : 0)) / total,
                        total == 0 ? 0 : values.Sum(value => value.Weight * (value.Metrics.RecallAt20 ? 1 : 0)) / total,
                        total == 0 ? 0 : values.Sum(value => value.Weight * value.Metrics.ReciprocalRank) / total,
                        total == 0 || values.Any(value => value.Metrics.RelevantNormalizedDistance == null) ? null
                            : values.Sum(value => value.Weight * value.Metrics.RelevantNormalizedDistance!.Value) / total);
                    var decision = ThresholdEvaluator.Evaluate(summary, retrieval.Thresholds);
                    var blockers = decision.Blockers.ToList();
                    if (sample.Length < retrieval.SamplesPerStratum)
                    {
                        blockers.Add("insufficient_lexical_queries");
                    }

                    cells.Add(new(degradation.Id, band, excluded ? "source-excluded" : "source-included",
                        sample.Length, summary, blockers.Count == 0, [.. blockers.Order(StringComparer.Ordinal)]));
                }
            }
        }

        return [.. cells];
    }

    public static double Distance(IReadOnlyList<double[]> query, EvidenceUnit candidate) =>
        candidate.Readings.Sum(reading => reading.Weight * BenchmarkDtw.Measure(query, reading.Phones).MeanPathCost);
}
