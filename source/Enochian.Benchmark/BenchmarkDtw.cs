using DynamicTimeWarp = Enochian.Math.DynamicTimeWarp;
using DynamicTimeWarpResult = Enochian.Math.DynamicTimeWarpResult;

namespace Enochian.Benchmark;

public static class BenchmarkDtw
{
    public static DynamicTimeWarpResult Measure(IReadOnlyList<double[]> source, IReadOnlyList<double[]> target)
    {
        return DynamicTimeWarp.GetSequenceResult(
            source,
            target,
            DynamicTimeWarp.EuclideanDistance,
            0);
    }
}
