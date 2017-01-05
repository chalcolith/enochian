using Enochian.Encoding;
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace Enochian.UnitTests.Encoding
{
    public class FeaturesTests
    {
        const double plusValue = 10.0;
        const double minusValue = 0.0;
        const double unsetValue = (plusValue + minusValue) / 2.0;

        static readonly string[] featureNames = new[]
        {
            "Alpha", "Charlie", "Bravo",
        };

        Features featuresUnderTest;

        public FeaturesTests()
        {
            dynamic config = new 
            {
                Id = "Features Tests",
                Description = "",
                Changes = "",
                PlusValue = plusValue,
                MinusValue = minusValue,
                Features = featureNames,
            };
            featuresUnderTest = new Features(config);
        }

        public static TheoryData<IList<string>, double[]> GetFeatureVectorData()
        {
            return new TheoryData<IList<string>, double[]>
            {
                { new [] { "+Alpha", "-Charlie" }, new [] { plusValue, unsetValue, minusValue } },
                { new string[] { }, new[] { unsetValue, unsetValue, unsetValue } },
            };
        }

        [Theory]
        [MemberData(nameof(GetFeatureVectorData))]
        public void Test_GetFeatureVector(IList<string> featureSpec, double[] expected)
        {
            var errors = new List<string>();
            var actual = featuresUnderTest.GetFeatureVector(featureSpec, errors);
            Assert.NotNull(actual);
            Assert.Equal(featureNames.Length, actual.Length);
            Assert.Equal(expected, actual);
        }
    }
}
