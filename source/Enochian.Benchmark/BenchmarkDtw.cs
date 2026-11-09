using DynamicTimeWarp = Enochian.Math.DynamicTimeWarp;
using DynamicTimeWarpResult = Enochian.Math.DynamicTimeWarpResult;

namespace Enochian.Benchmark;

public static class BenchmarkDtw
{
    public static DynamicTimeWarpResult Measure(
        IReadOnlyList<double[]> source,
        IReadOnlyList<double[]> target,
        IReadOnlySet<int>? ignoredQueryFeatureIndices = null)
    {
        var ignored = ignoredQueryFeatureIndices ?? new HashSet<int>();
        return DynamicTimeWarp.GetSequenceResult(
            source,
            target,
            (left, right) => EuclideanDistance(left, right, ignored),
            0);
    }

    public static double EuclideanDistance(
        double[] query,
        double[] candidate,
        IReadOnlySet<int> ignoredQueryFeatureIndices)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(ignoredQueryFeatureIndices);
        if (query.Length != candidate.Length)
        {
            throw new ArgumentException("Feature vectors must have the same dimensions.");
        }

        if (ignoredQueryFeatureIndices.Count >= query.Length ||
            ignoredQueryFeatureIndices.Any(index => index < 0 || index >= query.Length))
        {
            throw new ArgumentOutOfRangeException(
                nameof(ignoredQueryFeatureIndices), "Ignored feature indices must leave at least one measured dimension.");
        }

        double sum = 0;
        for (var index = 0; index < query.Length; index++)
        {
            if (!double.IsFinite(query[index]) || !double.IsFinite(candidate[index]))
            {
                throw new ArgumentException("Feature vectors must contain only finite values.");
            }

            if (ignoredQueryFeatureIndices.Contains(index))
            {
                continue;
            }

            var difference = query[index] - candidate[index];
            sum += difference * difference;
            if (!double.IsFinite(sum))
            {
                throw new OverflowException("The Euclidean distance exceeded the finite double range.");
            }
        }

        return System.Math.Sqrt(sum);
    }
}
