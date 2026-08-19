using Enochian.Math;

namespace Enochian.UnitTests.Math;

[TestClass]
public class DtwTests
{
    private static readonly Func<double[], double[], double> AbsoluteDistance =
        (left, right) => System.Math.Abs(left[0] - right[0]);

    [TestMethod]
    public void TestDtwSimple()
    {
        var seq1 = new double[][]
        {
            [0.0],
            [0.5],
            [1.0],
            [0.5],
            [0.0],
        };

        var seq2 = new double[][]
        {
            [0.0],
            [0.5],
            [0.75],
            [0.5],
            [0.0],
        };

        static double dist(double[] a, double[] b) => System.Math.Abs(a[0] - b[0]);

        var dist1 = DynamicTimeWarp.GetSequenceDistance(seq1, seq2, dist, 0.0);
        Assert.AreEqual(0.25, dist1);

        var seq3 = new double[][]
        {
            [0.0],
            [0.25],
            [0.5],
            [0.75],
            [1.0],
            [0.75],
            [0.5],
            [0.25],
            [0.0],
        };

        var dist2 = DynamicTimeWarp.GetSequenceDistance(seq1, seq3, dist, 0.0);

        Assert.IsTrue(dist1 < dist2);
        Assert.IsTrue(dist2 < 1.5);

        var seq4 = new double[][]
        {
            [-0.5],
            [-1.0],
            [-1.5],
            [-1.0],
            [-0.5],
        };

        var dist3 = DynamicTimeWarp.GetSequenceDistance(seq1, seq4, dist, 0.0);
        Assert.IsTrue(dist3 > dist2);
        Assert.IsTrue(dist3 > 5.0);
    }

    [TestMethod]
    public void ReturnsCostPathLengthAndNormalizations()
    {
        double[][] source = [[0], [1]];
        double[][] target = [[0], [0], [1]];

        var result = DynamicTimeWarp.GetSequenceResult(source, target, AbsoluteDistance, 0, includePath: true);

        Assert.AreEqual(0, result.Cost);
        Assert.AreEqual(3, result.PathLength);
        Assert.AreEqual(2, result.SourceLength);
        Assert.AreEqual(3, result.TargetLength);
        Assert.AreEqual(0, result.MeanPathCost);
        Assert.AreEqual(0, result.MeanInputLengthCost);
        Assert.HasCount(3, result.Path ?? throw new AssertFailedException("Expected a diagnostic path."));

        var reverse = DynamicTimeWarp.GetSequenceResult(target, source, AbsoluteDistance, 0);
        Assert.AreEqual(result.Cost, reverse.Cost);
        Assert.AreEqual(result.PathLength, reverse.PathLength);
    }

    [TestMethod]
    public void ChoosesShortestPathThenMatchForTies()
    {
        double[][] values = [[0], [0], [0]];

        var result = DynamicTimeWarp.GetSequenceResult(values, values, AbsoluteDistance, 0, includePath: true);

        Assert.AreEqual(3, result.PathLength);
        Assert.IsTrue(result.Path?.All(point => point.Step == DynamicTimeWarpStep.Match));
    }

    [TestMethod]
    public void DefinesEmptyAndInvalidInputBehavior()
    {
        var empty = DynamicTimeWarp.GetSequenceResult([], [], AbsoluteDistance, 0);
        var oneEmpty = DynamicTimeWarp.GetSequenceResult([[0]], [], AbsoluteDistance, 0);

        Assert.AreEqual(0, empty.Cost);
        Assert.AreEqual(0, empty.PathLength);
        Assert.IsTrue(double.IsPositiveInfinity(oneEmpty.Cost));
        Assert.AreEqual(0, DynamicTimeWarp.GetSequenceResult([[0]], [[0]], AbsoluteDistance, 1).Cost);
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            DynamicTimeWarp.GetSequenceResult([[0]], [[0]], AbsoluteDistance, -0.01));
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            DynamicTimeWarp.GetSequenceResult([[0]], [[0]], AbsoluteDistance, 1.01));
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            DynamicTimeWarp.GetSequenceResult([[0]], [[0]], AbsoluteDistance, double.NaN));
        _ = Assert.ThrowsExactly<ArgumentException>(() =>
            DynamicTimeWarp.EuclideanDistance([0], [0, 1]));
        _ = Assert.ThrowsExactly<OverflowException>(() =>
            DynamicTimeWarp.EuclideanDistance([double.MaxValue], [-double.MaxValue]));
        _ = Assert.ThrowsExactly<InvalidOperationException>(() =>
            DynamicTimeWarp.GetSequenceResult([[0]], [[0]], (_, _) => double.NaN, 0));
    }
}
