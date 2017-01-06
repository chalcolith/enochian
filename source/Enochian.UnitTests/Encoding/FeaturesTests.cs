using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Text;
using Enochian.Encoding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Enochian.UnitTests.Encoding
{
    [TestClass]
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
            dynamic config = new ExpandoObject().Init(new
            {
                Id = "Features Tests",
                Description = "",
                Changes = "",
                PlusValue = plusValue,
                MinusValue = minusValue,
                Features = featureNames
            });
            featuresUnderTest = new Features();
            featuresUnderTest.ConfigWith((object)config);
        }

        #region Tests

        static readonly Tuple<string[], double[]>[] featureVectorData_Valid =
        {
            Tuple.Create(new [] { "+Alpha", "-Charlie" }, new [] { plusValue, unsetValue, minusValue }),
            Tuple.Create(new string[] { }, new[] { unsetValue, unsetValue, unsetValue }),
        };

        static readonly Tuple<string[], double[]>[] featureVectorData_Invalid =
        {
            Tuple.Create(new[] { "Foobar" }, new[] { unsetValue, unsetValue, unsetValue }),
        };

        [TestMethod]
        public void GetFeatureVector()
        {
            string expectedError = null;
            Action<string[], double[]> test = (featureSpec, expected) =>
            {
                double[] actual = null;
                AssertUtils.WithErrors(
                    errors => actual = featuresUnderTest.GetFeatureVector(featureSpec, errors),
                    () =>
                    {
                        Assert.IsNotNull(actual);
                        AssertUtils.SequenceEquals(expected, actual);
                    },
                    expectedError);
            };

            foreach (var fv in featureVectorData_Valid)
                test(fv.Item1, fv.Item2);

            expectedError = "invalid feature specification";
            foreach (var fv in featureVectorData_Invalid)
                test(fv.Item1, fv.Item2);
        }

        #endregion
    }
}
