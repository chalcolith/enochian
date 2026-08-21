using System.Globalization;

namespace Enochian.Benchmark;

public static class StatisticalMethods
{
    public static ScoreCalibration Calibrate(double observedDistance, IEnumerable<double> nullDistances)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(observedDistance);
        var values = nullDistances.Order().ToArray();
        if (values.Length == 0)
        {
            return new(null, null, 0, null, null, "missing-null-distribution");
        }

        if (values.Any(value => !double.IsFinite(value) || value < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(nullDistances), "Null distances must be finite and non-negative.");
        }

        var less = values.Count(value => value < observedDistance);
        var equal = values.Count(value => value == observedDistance);
        var percentile = (less + (0.5 * equal)) / values.Length;
        var mean = values.Average();
        if (values.Length < 2)
        {
            return new(percentile, null, values.Length, mean, null, "insufficient-null-samples");
        }

        var standardDeviation = System.Math.Sqrt(values.Sum(value => System.Math.Pow(value - mean, 2)) / (values.Length - 1));
        return standardDeviation == 0
            ? new(percentile, null, values.Length, mean, 0, "zero-null-variance")
            : new(percentile, (mean - observedDistance) / standardDeviation, values.Length, mean, standardDeviation, null);
    }

    public static double Median(IEnumerable<double> source)
    {
        var values = source.Order().ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(source));
        }

        var midpoint = values.Length / 2;
        return values.Length % 2 == 0
            ? (values[midpoint - 1] + values[midpoint]) / 2
            : values[midpoint];
    }

    public static double WeightedMedian(IEnumerable<(double Value, int Weight)> source)
    {
        var values = source.OrderBy(item => item.Value).ToArray();
        if (values.Length == 0)
        {
            throw new ArgumentException("At least one value is required.", nameof(source));
        }

        if (values.Any(item => item.Weight <= 0))
        {
            throw new ArgumentOutOfRangeException(nameof(source), "Weights must be positive.");
        }

        var totalWeight = values.Sum(item => (long)item.Weight);
        var lowerRank = (totalWeight + 1) / 2;
        var upperRank = (totalWeight / 2) + 1;
        long cumulative = 0;
        double? lowerValue = null;
        foreach (var (value, weight) in values)
        {
            cumulative += weight;
            if (!lowerValue.HasValue && cumulative >= lowerRank)
            {
                lowerValue = value;
            }

            if (cumulative >= upperRank)
            {
                return (lowerValue!.Value + value) / 2;
            }
        }

        throw new InvalidOperationException("Unable to locate the weighted median ranks.");
    }

    public static PermutationResult PairedPermutation(
        IEnumerable<PairedValue> pairs,
        string alternative,
        int randomizationCount,
        int seed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(randomizationCount);
        ArgumentOutOfRangeException.ThrowIfNegative(seed);
        ValidateAlternative(alternative);
        var ordered = pairs.OrderBy(pair => pair.QueryId, StringComparer.Ordinal).ToArray();
        if (ordered.Length == 0)
        {
            throw new ArgumentException("At least one pair is required.", nameof(pairs));
        }

        var differences = ordered.Select(pair => pair.Difference).ToArray();
        var observed = WeightedMedian(ordered.Select((pair, index) => (differences[index], pair.Weight)));
        var exactCount = ordered.Length < 31 ? 1 << ordered.Length : int.MaxValue;
        var exact = randomizationCount >= exactCount;
        var iterations = exact ? exactCount : randomizationCount;
        var extreme = 0;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var permuted = differences.Select((difference, index) =>
                (Sign(seed, iteration, ordered[index].QueryId, index, exact) * difference, ordered[index].Weight));
            var statistic = WeightedMedian(permuted);
            if (IsExtreme(statistic, observed, alternative))
            {
                extreme++;
            }
        }

        var pValue = exact ? (double)extreme / iterations : (double)(extreme + 1) / (iterations + 1);
        return new(observed, pValue, alternative, iterations, exact);
    }

    public static BootstrapInterval BootstrapMedianDifference(
        IEnumerable<PairedValue> pairs,
        double confidenceLevel,
        int bootstrapCount,
        int seed)
    {
        if (confidenceLevel is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidenceLevel));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(bootstrapCount, 2);
        var ordered = pairs.OrderBy(pair => pair.QueryId, StringComparer.Ordinal).ToArray();
        if (ordered.Length < 2)
        {
            throw new ArgumentException("At least two pairs are required for a bootstrap interval.", nameof(pairs));
        }

        var estimates = new double[bootstrapCount];
        for (var iteration = 0; iteration < bootstrapCount; iteration++)
        {
            estimates[iteration] = WeightedMedian(Enumerable.Range(0, ordered.Length).Select(index =>
            {
                var key = BalancedSampler.StableKey(
                    seed,
                    "bootstrap",
                    iteration.ToString(CultureInfo.InvariantCulture),
                    index.ToString(CultureInfo.InvariantCulture));
                var selected = Convert.ToUInt64(key[..16], 16) % (ulong)ordered.Length;
                var pair = ordered[(int)selected];
                return (pair.Difference, pair.Weight);
            }));
        }

        Array.Sort(estimates);
        var tail = (1 - confidenceLevel) / 2;
        return new(
            Quantile(estimates, tail),
            Quantile(estimates, 1 - tail),
            confidenceLevel,
            bootstrapCount);
    }

    public static BootstrapInterval HierarchicalBootstrapMedianDifference(
        IReadOnlyDictionary<string, IReadOnlyList<PairedValue>> samplePairs,
        double confidenceLevel,
        int bootstrapCount,
        int seed)
    {
        if (confidenceLevel is <= 0 or >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(confidenceLevel));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(bootstrapCount, 2);
        var samples = samplePairs.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray();
        if (samples.Length < 2)
        {
            throw new ArgumentException("At least two lexicon samples are required for a hierarchical bootstrap.", nameof(samplePairs));
        }

        var queryIds = samples.SelectMany(sample => sample.Value.Select(pair => pair.QueryId))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (queryIds.Length < 2)
        {
            throw new ArgumentException("At least two query types are required for a hierarchical bootstrap.", nameof(samplePairs));
        }

        var estimates = new double[bootstrapCount];
        for (var iteration = 0; iteration < bootstrapCount; iteration++)
        {
            var selectedSamples = Enumerable.Range(0, samples.Length)
                .Select(index => samples[StableIndex(seed, "bootstrap-sample", iteration, index, samples.Length)])
                .ToArray();
            var collapsed = queryIds.Select(queryId =>
            {
                var matches = selectedSamples.SelectMany(sample => sample.Value)
                    .Where(pair => pair.QueryId == queryId)
                    .ToArray();
                return matches.Length == 0
                    ? null
                    : new PairedValue(
                        queryId,
                        Median(matches.Select(pair => pair.Target)),
                        Median(matches.Select(pair => pair.Control)),
                        matches.First().Weight);
            }).OfType<PairedValue>().ToArray();
            estimates[iteration] = WeightedMedian(Enumerable.Range(0, collapsed.Length).Select(index =>
            {
                var pair = collapsed[StableIndex(seed, "bootstrap-query", iteration, index, collapsed.Length)];
                return (pair.Difference, pair.Weight);
            }));
        }

        Array.Sort(estimates);
        var tail = (1 - confidenceLevel) / 2;
        return new(Quantile(estimates, tail), Quantile(estimates, 1 - tail), confidenceLevel, bootstrapCount);
    }

    public static IReadOnlyList<AdjustedPValue> HolmAdjust(
        IEnumerable<(string ContrastId, double PValue)> tests,
        int? plannedFamilySize = null)
    {
        var ordered = tests.OrderBy(test => test.PValue).ThenBy(test => test.ContrastId, StringComparer.Ordinal).ToArray();
        var familySize = plannedFamilySize ?? ordered.Length;
        if (familySize < ordered.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(plannedFamilySize), "The planned family cannot be smaller than the tested family.");
        }

        if (ordered.Any(test => test.PValue is < 0 or > 1))
        {
            throw new ArgumentOutOfRangeException(nameof(tests), "P-values must be between zero and one.");
        }

        var adjusted = new List<AdjustedPValue>(ordered.Length);
        var previous = 0.0;
        for (var index = 0; index < ordered.Length; index++)
        {
            var value = System.Math.Min(1, ordered[index].PValue * (familySize - index));
            previous = System.Math.Max(previous, value);
            adjusted.Add(new(ordered[index].ContrastId, ordered[index].PValue, previous, familySize));
        }

        return [.. adjusted.OrderBy(value => value.ContrastId, StringComparer.Ordinal)];
    }

    public static double RankBiserialEffect(IEnumerable<PairedValue> pairs)
    {
        var differences = pairs.Select(pair => pair.Difference).Where(difference => difference != 0)
            .OrderBy(System.Math.Abs)
            .ToArray();
        if (differences.Length == 0)
        {
            return 0;
        }

        var signedRankSum = 0.0;
        var rank = 1;
        while (rank <= differences.Length)
        {
            var end = rank;
            while (end < differences.Length && System.Math.Abs(differences[end]) == System.Math.Abs(differences[rank - 1]))
            {
                end++;
            }

            var midrank = (rank + end) / 2.0;
            signedRankSum += differences[(rank - 1)..end].Sum(difference => System.Math.Sign(difference) * midrank);
            rank = end + 1;
        }

        return signedRankSum / (differences.Length * (differences.Length + 1) / 2.0);
    }

    private static double Quantile(double[] sorted, double probability)
    {
        var position = probability * (sorted.Length - 1);
        var lower = (int)System.Math.Floor(position);
        var upper = (int)System.Math.Ceiling(position);
        var fraction = position - lower;
        return sorted[lower] + ((sorted[upper] - sorted[lower]) * fraction);
    }

    private static int StableIndex(int seed, string kind, int iteration, int index, int count)
    {
        var key = BalancedSampler.StableKey(
            seed,
            kind,
            iteration.ToString(CultureInfo.InvariantCulture),
            index.ToString(CultureInfo.InvariantCulture));
        return (int)(Convert.ToUInt64(key[..16], 16) % (ulong)count);
    }

    private static int Sign(int seed, int iteration, string queryId, int index, bool exact)
    {
        if (exact)
        {
            return (iteration & (1 << index)) == 0 ? -1 : 1;
        }

        var key = BalancedSampler.StableKey(
            seed,
            "permutation",
            iteration.ToString(CultureInfo.InvariantCulture),
            queryId);
        return Convert.ToUInt64(key[..16], 16) % 2 == 0 ? -1 : 1;
    }

    private static bool IsExtreme(double statistic, double observed, string alternative) => alternative switch
    {
        "greater" => statistic >= observed,
        "less" => statistic <= observed,
        _ => System.Math.Abs(statistic) >= System.Math.Abs(observed),
    };

    private static void ValidateAlternative(string alternative)
    {
        if (alternative is not ("greater" or "less" or "two-sided"))
        {
            throw new ArgumentException("Alternative must be greater, less, or two-sided.", nameof(alternative));
        }
    }
}
