using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Enochian.Config
{
    public class PhonFeaturesConfig : ConfigEntity
    {
        double plusValue = 1.0;
        double minusValue = -1.0;
        double? unknownValue;

        IList<string> features;
        IDictionary<string, int> indexMap;
        string debug;

        public double PlusValue
        {
            get { return plusValue; }
            set { plusValue = value; debug = null; }
        }

        public double MinusValue
        {
            get { return minusValue; }
            set { minusValue = value; debug = null; }
        }

        public double UnknownValue
        {
            get { return unknownValue ?? (PlusValue - MinusValue) / 2.0; }
            set { unknownValue = value; debug = null; }
        }

        public IList<string> Features
        {
            get { return features ?? (features = new List<string>()); }
            set { features = value.OrderBy(f => f).ToList(); BuildIndexMap(); debug = null; }
        }

        static readonly Regex WordRegex = new Regex(@"^\w+$", RegexOptions.Compiled);
        static readonly Regex FeatureRegex = new Regex(@"^([+-])(\w+)$", RegexOptions.Compiled);

        public double[] GetFeatureVector(IList<string> givens, IList<string> errors)
        {
            if (features == null || features.Count == 0 || indexMap == null)
            {
                errors.Add("no features configured");
                return new double[0];
            }

            var vec = Enumerable.Range(0, features.Count).Select(i => UnknownValue).ToArray();
            foreach (var given in givens)
            {
                var m = FeatureRegex.Match(given);
                if (m.Success)
                {
                    bool plus = m.Groups[1].Value == "+";
                    var key = m.Groups[2].Value.Trim().ToUpperInvariant();
                    if (indexMap.TryGetValue(key, out int index))
                        vec[index] = plus ? PlusValue : MinusValue;
                    else
                        errors.Add(string.Format("unknown feature '{0}'", given.Substring(1)));
                }
                else
                {
                    errors.Add(string.Format("invalid feature specification '{1}'; must be [+-]Foo", given));
                }
            }
            return vec;
        }

        void BuildIndexMap()
        {
            indexMap = new Dictionary<string, int>();
            if (features == null || features.Count == 0)
                return;

            for (int i = 0; i < features.Count; i++)
            {
                var names = features[i].Split(',').Select(s => s.Trim());
                foreach (var name in names)
                {
                    var key = name.ToUpperInvariant();
                    if (WordRegex.IsMatch(key))
                    {
                        if (!indexMap.ContainsKey(key))
                            indexMap.Add(key, i);
                        else
                            Errors.Add(string.Format("{0}: duplicate feature name '{1}'", AbsolutePath, name));
                    }
                    else
                    {
                        Errors.Add(string.Format("{0}: invalid feature name '{1}'; must be only alphabetic characters", AbsolutePath, name));
                    }
                }
            }
        }

        public override string ToString()
        {
            if (debug == null)
            {
                debug = string.Format("{{ [{0},{1},{2} {3} }}",
                    features == null ? "" : string.Join(", ", features.OrderBy(f => f)));
            }
            return debug;
        }
    }
}
