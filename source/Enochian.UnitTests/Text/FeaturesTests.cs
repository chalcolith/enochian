using Enochian.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Enochian.UnitTests.Text;

[TestClass]
public class FeaturesTests
{
    private const double plusValue = 10.0;
    private const double minusValue = 0.0;
    private const double unsetValue = (plusValue + minusValue) / 2.0;

    private static readonly string[] featureNames =
    [
        "Alpha", "Charlie", "Bravo",
    ];

    private readonly FeatureSet featuresUnderTest;

    public FeaturesTests()
    {
        var config = new JsonObject
        {
            ["id"] = "Features Tests",
            ["description"] = "",
            ["plusValue"] = plusValue,
            ["minusValue"] = minusValue,
            ["features"] = JsonSerializer.SerializeToNode(featureNames),
        };

        featuresUnderTest = new FeatureSet(null);
        _ = featuresUnderTest.Configure(config);
    }

    #region Tests

    private static readonly (string[], double[])[] featureVectorData_Valid =
    [
        (["+Alpha", "-Charlie"], [plusValue, unsetValue, minusValue]),
        ([], [unsetValue, unsetValue, unsetValue]),
    ];

    private static readonly (string[], double[])[] featureVectorData_Invalid =
    [
        (["Foobar"], [unsetValue, unsetValue, unsetValue]),
    ];

    [TestMethod]
    public void GetFeatureVector()
    {
        string? expectedError = null;
        void test(string[] featureSpec, double[] expected)
        {
            double[]? actual = null;
            AssertUtils.WithErrors(errors => actual = featuresUnderTest.GetFeatureVector(featureSpec, errors),
                () =>
                {
                    Assert.IsNotNull(actual);
                    AssertUtils.SequenceEquals(expected, actual);
                },
                expectedError);
        }

        foreach (var fv in featureVectorData_Valid)
        {
            test(fv.Item1, fv.Item2);
        }

        expectedError = "invalid feature specification";
        foreach (var fv in featureVectorData_Invalid)
        {
            test(fv.Item1, fv.Item2);
        }
    }

    #endregion
}
