namespace Enochian.Math;

public static class DynamicTimeWarp
{
    public static double GetSequenceDistance(IReadOnlyList<double[]> s, IReadOnlyList<double[]> t, Func<double[], double[], double> elemDistance, double tolerance)
    {
        return GetSequenceResult(s, t, elemDistance, tolerance).Cost;
    }

    public static DynamicTimeWarpResult GetSequenceResult(
        IReadOnlyList<double[]> s,
        IReadOnlyList<double[]> t,
        Func<double[], double[], double> elemDistance,
        double tolerance,
        bool includePath = false)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(t);
        ArgumentNullException.ThrowIfNull(elemDistance);
        if (!double.IsFinite(tolerance) || tolerance < 0.0 || tolerance > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "Tolerance must be finite and between zero and one.");
        }

        int n = s.Count;
        int m = t.Count;
        if (n == 0 || m == 0)
        {
            return n == 0 && m == 0
                ? new DynamicTimeWarpResult(0, 0, 0, 0, includePath ? [] : null)
                : new DynamicTimeWarpResult(double.PositiveInfinity, 0, n, m, includePath ? [] : null);
        }

        var dtw = new double[n + 1, m + 1];
        var pathLengths = new int[n + 1, m + 1];
        var backpointers = includePath ? new DynamicTimeWarpStep?[n + 1, m + 1] : null;

        // phi
        int tn = (int)System.Math.Ceiling(tolerance * n);
        int tm = (int)System.Math.Ceiling(tolerance * m);
        for (int i = 0; i <= n; i++)
        {
            for (int j = 0; j <= m; j++)
            {
                dtw[i, j] = double.MaxValue;
            }
        }

        dtw[0, 0] = 0;
        for (int i = 1; i <= tn; i++)
        {
            dtw[i, 0] = 0;
        }

        for (int j = 1; j <= tm; j++)
        {
            dtw[0, j] = 0;
        }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                var cost = elemDistance(s[i - 1], t[j - 1]);
                if (!double.IsFinite(cost) || cost < 0.0)
                {
                    throw new InvalidOperationException("The element distance must return a finite, non-negative value.");
                }

                var (predecessorCost, predecessorPathLength, step) = SelectPredecessor(dtw, pathLengths, i, j);
                var accumulatedCost = predecessorCost + cost;
                dtw[i, j] = double.IsPositiveInfinity(accumulatedCost)
                    ? double.PositiveInfinity
                    : accumulatedCost;
                pathLengths[i, j] = predecessorPathLength + 1;
                if (includePath)
                {
                    backpointers![i, j] = step;
                }
            }
        }

        var path = backpointers == null ? null : BuildPath(backpointers, n, m);
        return new DynamicTimeWarpResult(dtw[n, m], pathLengths[n, m], n, m, path);
    }

    private static (double Cost, int PathLength, DynamicTimeWarpStep Step) SelectPredecessor(
        double[,] costs,
        int[,] pathLengths,
        int sourceIndex,
        int targetIndex)
    {
        var candidates = new[]
        {
            (Cost: costs[sourceIndex - 1, targetIndex - 1], PathLength: pathLengths[sourceIndex - 1, targetIndex - 1], Step: DynamicTimeWarpStep.Match),
            (Cost: costs[sourceIndex - 1, targetIndex], PathLength: pathLengths[sourceIndex - 1, targetIndex], Step: DynamicTimeWarpStep.Insertion),
            (Cost: costs[sourceIndex, targetIndex - 1], PathLength: pathLengths[sourceIndex, targetIndex - 1], Step: DynamicTimeWarpStep.Deletion),
        };
        return candidates
            .OrderBy(candidate => candidate.Cost)
            .ThenBy(candidate => candidate.PathLength)
            .ThenBy(candidate => candidate.Step)
            .First();
    }

    private static List<DynamicTimeWarpPathPoint> BuildPath(
        DynamicTimeWarpStep?[,] backpointers,
        int sourceIndex,
        int targetIndex)
    {
        var path = new List<DynamicTimeWarpPathPoint>();
        while (sourceIndex > 0 && targetIndex > 0 && backpointers[sourceIndex, targetIndex] is { } step)
        {
            path.Add(new DynamicTimeWarpPathPoint(sourceIndex - 1, targetIndex - 1, step));
            switch (step)
            {
                case DynamicTimeWarpStep.Match:
                    sourceIndex--;
                    targetIndex--;
                    break;
                case DynamicTimeWarpStep.Insertion:
                    sourceIndex--;
                    break;
                case DynamicTimeWarpStep.Deletion:
                    targetIndex--;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown dynamic time warping step '{step}'.");
            }
        }

        path.Reverse();
        return path;
    }

    public static double EuclideanDistance(double[] a, double[] b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Length != b.Length)
        {
            throw new ArgumentException("Feature vectors must have the same dimensions.");
        }

        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            if (!double.IsFinite(a[i]) || !double.IsFinite(b[i]))
            {
                throw new ArgumentException("Feature vectors must contain only finite values.");
            }

            double d = a[i] - b[i];
            sum += d * d;
            if (!double.IsFinite(sum))
            {
                throw new OverflowException("The Euclidean distance exceeded the finite double range.");
            }
        }
        return System.Math.Sqrt(sum);
    }
}
