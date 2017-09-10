using System;
using System.Collections.Generic;
using System.Text;

namespace Enochian.Math
{
    public static class DynamicTimeWarp
    {
        public static double GetSequenceDistance(IList<double[]> s, IList<double[]> t, Func<double[], double[], double> elemDistance, double tolerance)
        {
            int n = s.Count;
            int tn = (int)System.Math.Ceiling(tolerance * (double)n);            

            int m = t.Count;
            int tm = (int)System.Math.Ceiling(tolerance * (double)m);

            var dtw = new double[n + 1, m + 1];
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= m; j++)
                    dtw[i, j] = double.MaxValue;

            for (int i = 0; i < tn + 1; i++) dtw[i, 1] = 0;
            for (int j = 0; j < tn + 1; j++) dtw[1, j] = 0;

            //for (int i = 1; i <= n; i++) dtw[i, 0] = double.MaxValue;
            //for (int j = 1; j <= m; j++) dtw[0, j] = double.MaxValue;
            //dtw[0, 0] = 0;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    var cost = elemDistance(s[i - 1], t[j - 1]);

                    var ins = dtw[i - 1, j];
                    var del = dtw[i, j - 1];
                    var mat = dtw[i - 1, j - 1];

                    dtw[i, j] = cost + System.Math.Min(ins, System.Math.Min(del, mat));
                }
            }

            return dtw[n, m];
        }

        public static double EuclideanDistance(double[] a, double[] b)
        {
            if (a == null || b == null)
                return double.MaxValue;

            int n = System.Math.Max(a.Length, b.Length);
            double sum = 0;
            for (int i = 0; i < n; i++)
            {
                double x = i < a.Length ? a[i] : double.MinValue;
                double y = i < b.Length ? b[i] : double.MaxValue;
                double d = x - y;
                sum += d * d;
            }
            return System.Math.Sqrt(sum);
        }
    }
}
