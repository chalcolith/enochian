using DynamicTimeWarp = Enochian.Math.DynamicTimeWarp;

namespace Enochian.Benchmark;

public static class BenchmarkDtw
{
    public static DtwMeasurement Measure(IReadOnlyList<double[]> source, IReadOnlyList<double[]> target)
    {
        if (source.Count == 0 || target.Count == 0)
        {
            return source.Count == 0 && target.Count == 0
                ? new(0, 0)
                : new(double.PositiveInfinity, 0);
        }

        var costs = new double[source.Count + 1, target.Count + 1];
        var lengths = new int[source.Count + 1, target.Count + 1];
        for (var sourceIndex = 0; sourceIndex <= source.Count; sourceIndex++)
        {
            for (var targetIndex = 0; targetIndex <= target.Count; targetIndex++)
            {
                costs[sourceIndex, targetIndex] = double.PositiveInfinity;
            }
        }

        costs[0, 0] = 0;
        for (var sourceIndex = 1; sourceIndex <= source.Count; sourceIndex++)
        {
            for (var targetIndex = 1; targetIndex <= target.Count; targetIndex++)
            {
                var (cost, length) = SelectPredecessor(costs, lengths, sourceIndex, targetIndex);
                costs[sourceIndex, targetIndex] = cost +
                    DynamicTimeWarp.EuclideanDistance(
                        source[sourceIndex - 1],
                        target[targetIndex - 1]);
                lengths[sourceIndex, targetIndex] = length + 1;
            }
        }

        return new(costs[source.Count, target.Count], lengths[source.Count, target.Count]);
    }

    private static (double Cost, int Length) SelectPredecessor(
        double[,] costs,
        int[,] lengths,
        int sourceIndex,
        int targetIndex)
    {
        var candidates = new[]
        {
            (Cost: costs[sourceIndex - 1, targetIndex - 1], Length: lengths[sourceIndex - 1, targetIndex - 1], Order: 0),
            (Cost: costs[sourceIndex - 1, targetIndex], Length: lengths[sourceIndex - 1, targetIndex], Order: 1),
            (Cost: costs[sourceIndex, targetIndex - 1], Length: lengths[sourceIndex, targetIndex - 1], Order: 2),
        };
        var (cost, length, _) = candidates
            .OrderBy(candidate => candidate.Cost)
            .ThenBy(candidate => candidate.Length)
            .ThenBy(candidate => candidate.Order)
            .First();
        return (cost, length);
    }
}
