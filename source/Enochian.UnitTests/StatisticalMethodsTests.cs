using Enochian.Benchmark;

namespace Enochian.UnitTests;

[TestClass]
public sealed class StatisticalMethodsTests
{
    [TestMethod]
    public void CalibrateUsesMidrankTiesAndReportsFailedAssumptions()
    {
        var result = StatisticalMethods.Calibrate(2, [1, 2, 2, 4]);

        Assert.AreEqual(0.5, result.EmpiricalPercentile!.Value, 1e-12);
        Assert.AreEqual(2.25, result.NullMean!.Value, 1e-12);
        Assert.AreEqual(0.19867985355975654, result.StandardizedScore!.Value, 1e-12);
        Assert.IsNull(result.Diagnostic);
        Assert.AreEqual("zero-null-variance", StatisticalMethods.Calibrate(2, [2, 2]).Diagnostic);
        Assert.AreEqual("insufficient-null-samples", StatisticalMethods.Calibrate(2, [2]).Diagnostic);
        Assert.AreEqual("missing-null-distribution", StatisticalMethods.Calibrate(2, []).Diagnostic);
        Assert.IsNull(StatisticalMethods.Calibrate(2, []).EmpiricalPercentile);
    }

    [TestMethod]
    public void PairedStatisticsMatchHandCalculatedFixtures()
    {
        PairedValue[] pairs =
        [
            new("q1", 4, 1),
            new("q2", 3, 1),
            new("q3", 2, 1),
        ];

        var greater = StatisticalMethods.PairedPermutation(pairs, "greater", 8, 17);
        var less = StatisticalMethods.PairedPermutation(pairs, "less", 8, 17);
        var twoSided = StatisticalMethods.PairedPermutation(pairs, "two-sided", 8, 17);

        Assert.AreEqual(2, greater.Estimate, 1e-12);
        Assert.AreEqual(0.25, greater.PValue, 1e-12);
        Assert.AreEqual(1, less.PValue, 1e-12);
        Assert.AreEqual(0.5, twoSided.PValue, 1e-12);
        Assert.IsTrue(greater.Exact);
        Assert.AreEqual(1, StatisticalMethods.RankBiserialEffect(pairs), 1e-12);
        Assert.AreEqual(0, StatisticalMethods.RankBiserialEffect([new("q", 1, 1)]), 1e-12);
        Assert.AreEqual(3, StatisticalMethods.WeightedMedian([(1.0, 1), (3.0, 3), (5.0, 1)]), 1e-12);
        Assert.AreEqual(2, StatisticalMethods.WeightedMedian([(1.0, 1), (3.0, 1)]), 1e-12);
    }

    [TestMethod]
    public void BootstrapAndHolmAreDeterministicAndMonotonic()
    {
        PairedValue[] pairs =
        [
            new("q1", 4, 1),
            new("q2", 3, 1),
            new("q3", 2, 1),
        ];

        var first = StatisticalMethods.BootstrapMedianDifference(pairs, 0.8, 100, 19);
        var repeated = StatisticalMethods.BootstrapMedianDifference(pairs.Reverse(), 0.8, 100, 19);
        Assert.AreEqual(first, repeated);
        Assert.IsTrue(first.Lower <= 2 && first.Upper >= 2);

        var adjusted = StatisticalMethods.HolmAdjust([("c", 0.04), ("a", 0.01), ("b", 0.03)]);
        Assert.AreEqual(3, adjusted.Count);
        Assert.IsTrue(adjusted.All(value => value.FamilySize == 3));
        Assert.AreEqual(0.03, adjusted.Single(value => value.ContrastId == "a").AdjustedPValueValue, 1e-12);
        Assert.AreEqual(0.06, adjusted.Single(value => value.ContrastId == "b").AdjustedPValueValue, 1e-12);
        Assert.AreEqual(0.06, adjusted.Single(value => value.ContrastId == "c").AdjustedPValueValue, 1e-12);
        var partial = StatisticalMethods.HolmAdjust([("a", 0.01), ("b", 0.03)], 3);
        Assert.IsTrue(partial.All(value => value.FamilySize == 3));
        Assert.AreEqual(0.06, partial.Single(value => value.ContrastId == "b").AdjustedPValueValue, 1e-12);
    }

    [TestMethod]
    public void HierarchicalBootstrapResamplesSamplesAndQueryTypesDeterministically()
    {
        var samples = new Dictionary<string, IReadOnlyList<PairedValue>>(StringComparer.Ordinal)
        {
            ["sample-1"] = [new("q1", 4, 1), new("q2", 3, 1)],
            ["sample-2"] = [new("q1", 5, 1), new("q2", 2, 1)],
        };

        var first = StatisticalMethods.HierarchicalBootstrapMedianDifference(samples, 0.8, 100, 23);
        var repeated = StatisticalMethods.HierarchicalBootstrapMedianDifference(
            samples.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            0.8,
            100,
            23);

        Assert.AreEqual(first, repeated);
        Assert.AreEqual(100, first.BootstrapCount);
        Assert.IsTrue(first.Lower <= first.Upper);

        var weighted = new Dictionary<string, IReadOnlyList<PairedValue>>(StringComparer.Ordinal)
        {
            ["sample-1"] = [new("q1", 0, 0, 1), new("q2", 10, 0, 100)],
            ["sample-2"] = [new("q1", 0, 0, 1), new("q2", 10, 0, 100)],
        };
        var weightedInterval = StatisticalMethods.HierarchicalBootstrapMedianDifference(weighted, 0.8, 100, 23);
        Assert.AreEqual(10, weightedInterval.Upper, 1e-12);
    }

    [TestMethod]
    public void PlantedEffectIsDetectedAndExchangeableFixturesAreNot()
    {
        var planted = Enumerable.Range(0, 10)
            .Select(index => new PairedValue($"q{index:D2}", System.Math.Pow(2, index), 0))
            .ToArray();
        var plantedResult = StatisticalMethods.PairedPermutation(planted, "greater", 1024, 41);
        Assert.IsLessThan(0.05, plantedResult.PValue);

        var exchangeable = Enumerable.Range(0, 40)
            .Select(index => new PairedValue($"q{index:D2}", index, index))
            .ToArray();
        foreach (var seed in new[] { 43, 47, 53, 59 })
        {
            Assert.AreEqual(1, StatisticalMethods.PairedPermutation(exchangeable, "two-sided", 1000, seed).PValue, 1e-12);
        }
    }
}
