using System.Globalization;

namespace Enochian.Benchmark;

public static class StatisticalAnalyzer
{
    public static StatisticalAnalysisResult Analyze(StatisticsProtocol protocol, IEnumerable<NearestDistanceRecord> source)
    {
        var rows = source.ToArray();
        var diagnostics = new List<StatisticalDiagnosticRow>();
        var calibrated = Calibrate(protocol, rows, diagnostics);
        var estimates = new List<StatisticalEstimateRow>();
        var intervals = new List<StatisticalIntervalRow>();
        var tests = new List<StatisticalTestRow>();
        foreach (var group in calibrated.Where(row => row.StandardizedScore.HasValue)
            .GroupBy(row => (row.AnalysisId, row.AnalysisMode, row.RequestedSize))
            .OrderBy(group => group.Key.AnalysisId, StringComparer.Ordinal)
            .ThenBy(group => group.Key.AnalysisMode, StringComparer.Ordinal)
            .ThenBy(group => group.Key.RequestedSize))
        {
            CalibratedScoreRow[] groupRows = [.. group];
            foreach (var contrast in protocol.Contrasts.OrderBy(contrast => contrast.ContrastId, StringComparer.Ordinal))
            {
                AnalyzeContrast(protocol, groupRows, contrast, estimates, intervals, tests, diagnostics);
            }

            AddWinnerEstimates(protocol, groupRows, estimates);
            AddLanguageEstimates(protocol, groupRows, estimates);
        }

        var adjusted = tests.GroupBy(test => (test.AnalysisId, test.AnalysisMode, test.RequestedSize))
            .SelectMany(group => StatisticalMethods.HolmAdjust(
                group.Select(test => (test.ContrastId, test.PValue)),
                protocol.Contrasts.Count)
                .Select(value => new AdjustedPValueRow(
                    "1.0.0",
                    protocol.StatisticsId,
                    group.Key.AnalysisId,
                    group.Key.AnalysisMode,
                    group.Key.RequestedSize,
                    value.ContrastId,
                    value.PValue,
                    value.AdjustedPValueValue,
                    "holm",
                    value.FamilySize)))
            .OrderBy(row => row.AnalysisId, StringComparer.Ordinal)
            .ThenBy(row => row.AnalysisMode, StringComparer.Ordinal)
            .ThenBy(row => row.RequestedSize)
            .ThenBy(row => row.ContrastId, StringComparer.Ordinal)
            .ToArray();
        return new(
            calibrated,
            OrderEstimates(estimates),
            [.. intervals.OrderBy(row => row.AnalysisId, StringComparer.Ordinal).ThenBy(row => row.AnalysisMode, StringComparer.Ordinal).ThenBy(row => row.RequestedSize).ThenBy(row => row.ContrastId, StringComparer.Ordinal)],
            [.. tests.OrderBy(row => row.AnalysisId, StringComparer.Ordinal).ThenBy(row => row.AnalysisMode, StringComparer.Ordinal).ThenBy(row => row.RequestedSize).ThenBy(row => row.ContrastId, StringComparer.Ordinal)],
            adjusted,
            [.. diagnostics.OrderBy(row => row.Code, StringComparer.Ordinal).ThenBy(row => row.AnalysisId, StringComparer.Ordinal).ThenBy(row => row.SampleId, StringComparer.Ordinal).ThenBy(row => row.QueryId, StringComparer.Ordinal).ThenBy(row => row.Language, StringComparer.Ordinal)]);
    }

    private static IReadOnlyList<CalibratedScoreRow> Calibrate(
        StatisticsProtocol protocol,
        NearestDistanceRecord[] rows,
        List<StatisticalDiagnosticRow> diagnostics)
    {
        var nulls = rows.Where(row => row.IsNull && row.NullKind == protocol.CalibrationNullKind)
            .GroupBy(row => (row.AnalysisId, row.AnalysisMode, row.SampleId, row.QueryId, row.Language))
            .ToDictionary(group => group.Key, group => group.Select(row => row.Distance).ToArray());
        var calibrated = new List<CalibratedScoreRow>();
        foreach (var row in rows.Where(row => !row.IsNull))
        {
            _ = nulls.TryGetValue((row.AnalysisId, row.AnalysisMode, row.SampleId, row.QueryId, row.Language), out var distribution);
            var result = StatisticalMethods.Calibrate(row.Distance, distribution ?? []);
            if (result.Diagnostic != null)
            {
                diagnostics.Add(Diagnostic(protocol, "warning", result.Diagnostic,
                    $"Calibration failed for query '{row.QueryId}' and language '{row.Language}'.", row, null));
            }

            calibrated.Add(new(
                "1.0.0",
                protocol.StatisticsId,
                row.AnalysisId,
                row.AnalysisMode,
                row.SampleId,
                row.RequestedSize,
                row.Repetition,
                row.QueryId,
                row.QueryLength,
                row.Section,
                row.FrequencyBand,
                row.Weight,
                row.Language,
                row.Family,
                protocol.CalibrationNullKind,
                row.Distance,
                result.EmpiricalPercentile,
                result.StandardizedScore,
                result.NullCount,
                result.NullMean,
                result.NullStandardDeviation));
        }

        return [.. calibrated.OrderBy(row => row.AnalysisId, StringComparer.Ordinal).ThenBy(row => row.AnalysisMode, StringComparer.Ordinal).ThenBy(row => row.RequestedSize).ThenBy(row => row.SampleId, StringComparer.Ordinal).ThenBy(row => row.QueryId, StringComparer.Ordinal).ThenBy(row => row.Language, StringComparer.Ordinal)];
    }

    private static void AnalyzeContrast(
        StatisticsProtocol protocol,
        CalibratedScoreRow[] rows,
        StatisticalContrast contrast,
        List<StatisticalEstimateRow> estimates,
        List<StatisticalIntervalRow> intervals,
        List<StatisticalTestRow> tests,
        List<StatisticalDiagnosticRow> diagnostics)
    {
        var samplePairs = BuildSamplePairs(protocol, rows, contrast, diagnostics);
        var pairs = CollapseSamples(samplePairs);
        AddEstimate(protocol, rows[0], contrast, "overall", "all", pairs, samplePairs.Count, estimates);
        foreach (var stratum in Strata(rows))
        {
            var stratumSamples = BuildSamplePairs(protocol, stratum.Rows, contrast, diagnostics);
            var stratumPairs = CollapseSamples(stratumSamples);
            if (stratumPairs.Count != 0)
            {
                AddEstimate(protocol, rows[0], contrast, stratum.Scope, stratum.Id, stratumPairs, stratumSamples.Count, estimates);
            }
        }

        if (pairs.Count == 0)
        {
            diagnostics.Add(Diagnostic(protocol, "error", "no-complete-pairs",
                $"Contrast '{contrast.ContrastId}' has no complete query pairs.", rows[0], contrast.ContrastId));
            return;
        }

        var permutation = StatisticalMethods.PairedPermutation(pairs, contrast.Alternative, protocol.PermutationCount, protocol.Seed);
        tests.Add(new(
            "1.0.0",
            protocol.StatisticsId,
            rows[0].AnalysisId,
            rows[0].AnalysisMode,
            rows[0].RequestedSize,
            contrast.ContrastId,
            contrast.Primary,
            "paired-median-difference",
            permutation.Estimate,
            permutation.PValue,
            permutation.Alternative,
            protocol.PermutationCount,
            permutation.RandomizationCount,
            permutation.Exact,
            "holm",
            pairs.Count));
        try
        {
            var interval = StatisticalMethods.HierarchicalBootstrapMedianDifference(samplePairs, protocol.ConfidenceLevel, protocol.BootstrapCount, protocol.Seed);
            intervals.Add(new(
                "1.0.0",
                protocol.StatisticsId,
                rows[0].AnalysisId,
                rows[0].AnalysisMode,
                rows[0].RequestedSize,
                contrast.ContrastId,
                "paired-median-difference",
                interval.Lower,
                interval.Upper,
                interval.ConfidenceLevel,
                interval.BootstrapCount,
                "lexicon-samples-and-query-types"));
        }
        catch (ArgumentException exception)
        {
            diagnostics.Add(Diagnostic(protocol, "warning", "insufficient-bootstrap-samples", exception.Message, rows[0], contrast.ContrastId));
        }
    }

    private static Dictionary<string, IReadOnlyList<PairedValue>> BuildSamplePairs(
        StatisticsProtocol protocol,
        CalibratedScoreRow[] rows,
        StatisticalContrast contrast,
        List<StatisticalDiagnosticRow> diagnostics)
    {
        var result = new Dictionary<string, IReadOnlyList<PairedValue>>(StringComparer.Ordinal);
        foreach (var sample in rows.GroupBy(row => row.SampleId).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var pairs = new List<PairedValue>();
            foreach (var query in sample.GroupBy(row => row.QueryId).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var targets = query.Where(row => contrast.TargetFamilies.Contains(row.Family, StringComparer.Ordinal)).ToArray();
                var controls = query.Where(row => contrast.ControlFamilies.Contains(row.Family, StringComparer.Ordinal)).ToArray();
                if (targets.Length == 0 || controls.Length == 0)
                {
                    diagnostics.Add(Diagnostic(protocol, "warning", "missing-language-query-pair",
                        $"Query '{query.Key}' is missing a target or control value for contrast '{contrast.ContrastId}'.",
                        query.First(), contrast.ContrastId));
                    continue;
                }

                var weights = query.Select(row => row.Weight).Distinct().ToArray();
                if (weights.Length != 1)
                {
                    diagnostics.Add(Diagnostic(protocol, "warning", "inconsistent-query-weight",
                        $"Query '{query.Key}' has inconsistent weights.", query.First(), contrast.ContrastId));
                    continue;
                }

                pairs.Add(new(
                    query.Key,
                    StatisticalMethods.Median(targets.Select(Score)),
                    StatisticalMethods.Median(controls.Select(Score)),
                    weights[0]));
            }

            result[sample.Key] = pairs;
        }

        return result;
    }

    private static IReadOnlyList<PairedValue> CollapseSamples(IReadOnlyDictionary<string, IReadOnlyList<PairedValue>> samples) =>
        [
            .. samples.SelectMany(sample => sample.Value)
                .GroupBy(pair => pair.QueryId)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new PairedValue(
                    group.Key,
                    StatisticalMethods.Median(group.Select(pair => pair.Target)),
                    StatisticalMethods.Median(group.Select(pair => pair.Control)),
                    group.First().Weight)),
        ];

    private static void AddEstimate(
        StatisticsProtocol protocol,
        CalibratedScoreRow row,
        StatisticalContrast contrast,
        string scope,
        string stratum,
        IReadOnlyList<PairedValue> pairs,
        int sampleCount,
        List<StatisticalEstimateRow> estimates)
    {
        estimates.Add(new(
            "1.0.0",
            protocol.StatisticsId,
            row.AnalysisId,
            row.AnalysisMode,
            row.RequestedSize,
            scope,
            stratum,
            "paired-median-difference",
            contrast.ContrastId,
            null,
            StatisticalMethods.WeightedMedian(pairs.Select(pair => (pair.Difference, pair.Weight))),
            pairs.Count,
            sampleCount));
        estimates.Add(new(
            "1.0.0",
            protocol.StatisticsId,
            row.AnalysisId,
            row.AnalysisMode,
            row.RequestedSize,
            scope,
            stratum,
            "rank-biserial-effect",
            contrast.ContrastId,
            null,
            StatisticalMethods.RankBiserialEffect(pairs),
            pairs.Count,
            sampleCount));
    }

    private static void AddWinnerEstimates(
        StatisticsProtocol protocol,
        CalibratedScoreRow[] rows,
        List<StatisticalEstimateRow> estimates)
    {
        foreach (var stratum in new[] { new Stratum("overall", "all", rows) }.Concat(Strata(rows)))
        {
            var contests = stratum.Rows.GroupBy(row => (row.SampleId, row.QueryId)).ToArray();
            foreach (var language in stratum.Rows.Select(row => row.Language).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                var wins = contests.Sum(contest =>
                {
                    var best = contest.Max(Score);
                    var winners = contest.Count(row => Score(row) == best);
                    return contest.Any(row => row.Language == language && Score(row) == best) ? 1.0 / winners : 0;
                });
                estimates.Add(new(
                    "1.0.0",
                    protocol.StatisticsId,
                    rows[0].AnalysisId,
                    rows[0].AnalysisMode,
                    rows[0].RequestedSize,
                    stratum.Scope,
                    stratum.Id,
                    "winner-proportion",
                    null,
                    language,
                    wins / contests.Length,
                    contests.Select(contest => contest.Key.QueryId).Distinct(StringComparer.Ordinal).Count(),
                    contests.Select(contest => contest.Key.SampleId).Distinct(StringComparer.Ordinal).Count()));
            }
        }
    }

    private static void AddLanguageEstimates(
        StatisticsProtocol protocol,
        CalibratedScoreRow[] rows,
        List<StatisticalEstimateRow> estimates)
    {
        foreach (var stratum in new[] { new Stratum("overall", "all", rows) }.Concat(Strata(rows)))
        {
            foreach (var language in stratum.Rows.GroupBy(row => row.Language).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                AddLanguageMetric("median-raw-distance", language.Select(row => (row.RawDistance, row.Weight)));
                AddLanguageMetric("median-empirical-percentile", language.Select(row => (row.EmpiricalPercentile!.Value, row.Weight)));
                AddLanguageMetric("median-standardized-score", language.Select(row => (row.StandardizedScore!.Value, row.Weight)));

                void AddLanguageMetric(string metric, IEnumerable<(double Value, int Weight)> values) => estimates.Add(new(
                    "1.0.0",
                    protocol.StatisticsId,
                    rows[0].AnalysisId,
                    rows[0].AnalysisMode,
                    rows[0].RequestedSize,
                    stratum.Scope,
                    stratum.Id,
                    metric,
                    null,
                    language.Key,
                    StatisticalMethods.WeightedMedian(values),
                    language.Select(row => row.QueryId).Distinct(StringComparer.Ordinal).Count(),
                    language.Select(row => row.SampleId).Distinct(StringComparer.Ordinal).Count()));
            }
        }
    }

    private static IEnumerable<Stratum> Strata(CalibratedScoreRow[] rows)
    {
        foreach (var group in rows.GroupBy(row => row.QueryLength).OrderBy(group => group.Key))
        {
            yield return new("length", group.Key.ToString(CultureInfo.InvariantCulture), [.. group]);
        }

        foreach (var group in rows.GroupBy(row => row.Section).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            yield return new("section", group.Key, [.. group]);
        }

        foreach (var group in rows.GroupBy(row => row.FrequencyBand).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            yield return new("frequency-band", group.Key, [.. group]);
        }
    }

    private static double Score(CalibratedScoreRow row) => row.StandardizedScore!.Value;

    private static IReadOnlyList<StatisticalEstimateRow> OrderEstimates(IEnumerable<StatisticalEstimateRow> rows) =>
        [.. rows.OrderBy(row => row.AnalysisId, StringComparer.Ordinal).ThenBy(row => row.AnalysisMode, StringComparer.Ordinal).ThenBy(row => row.RequestedSize).ThenBy(row => row.Scope, StringComparer.Ordinal).ThenBy(row => row.Stratum, StringComparer.Ordinal).ThenBy(row => row.Metric, StringComparer.Ordinal).ThenBy(row => row.ContrastId, StringComparer.Ordinal).ThenBy(row => row.GroupId, StringComparer.Ordinal)];

    private static StatisticalDiagnosticRow Diagnostic(
        StatisticsProtocol protocol,
        string severity,
        string code,
        string message,
        CalibratedScoreRow row,
        string? contrastId) =>
        new("1.0.0", protocol.StatisticsId, severity, code, message, row.AnalysisId, row.AnalysisMode,
            row.RequestedSize, row.SampleId, row.QueryId, row.Language, contrastId);

    private static StatisticalDiagnosticRow Diagnostic(
        StatisticsProtocol protocol,
        string severity,
        string code,
        string message,
        NearestDistanceRecord row,
        string? contrastId) =>
        new("1.0.0", protocol.StatisticsId, severity, code, message, row.AnalysisId, row.AnalysisMode,
            row.RequestedSize, row.SampleId, row.QueryId, row.Language, contrastId);

    private sealed record Stratum(string Scope, string Id, CalibratedScoreRow[] Rows);
}
