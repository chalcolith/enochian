using System;
using System.Collections.Generic;
using System.Text;
using Enochian.Math;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Enochian.UnitTests.Math
{
    [TestClass]
    public class DtwTests
    {
        [TestMethod]
        public void TestDtwSimple()
        {
            var seq1 = new double[][]
            {
                new double[] { 0.0 },
                new double[] { 0.5 },
                new double[] { 1.0 },
                new double[] { 0.5 },
                new double[] { 0.0 },
            };

            var seq2 = new double[][]
            {
                new double[] { 0.0 },
                new double[] { 0.5 },
                new double[] { 0.75 },
                new double[] { 0.5 },
                new double[] { 0.0 },
            };

            Func<double[], double[], double> dist = (a, b) => System.Math.Abs(a[0] - b[0]);

            var dist1 = DynamicTimeWarp.GetSequenceDistance(seq1, seq2, dist, 0.0);            

            var seq3 = new double[][]
            {
                new double[] { 0.0 },
                new double[] { 0.25 },
                new double[] { 0.5 },
                new double[] { 0.75 },
                new double[] { 1.0 },
                new double[] { 0.75 },
                new double[] { 0.5 },
                new double[] { 0.25 },
                new double[] { 0.0 },
            };

            var dist2 = DynamicTimeWarp.GetSequenceDistance(seq1, seq3, dist, 0.0);

            Assert.IsTrue(dist1 < dist2);
            Assert.IsTrue(dist2 < 1.5);

            var seq4 = new double[][]
            {
                new double[] { -0.5 },
                new double[] { -1.0 },
                new double[] { -1.5 },
                new double[] { -1.0 },
                new double[] { -0.5 },
            };

            var dist3 = DynamicTimeWarp.GetSequenceDistance(seq1, seq4, dist, 0.0);
            Assert.IsTrue(dist3 > dist2);
            Assert.IsTrue(dist3 > 5.0);
        }
    }
}
