using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Enochian.Encoding
{
    public class Features : Configurable
    {
        IList<string> featureList;
        IDictionary<string, int> featureIndices;

        public double PlusValue { get; private set; } = 1.0;
        public double UnsetValue { get; private set; } = 0.0;
        public double MinusValue { get; private set; } = -1.0;

        public IList<string> FeatureList
        {
            get { return featureList ?? (featureList = new List<string>()); }
        }

        public int NumDimensions
        {
            get { return featureList?.Count ?? 0; }
        }

        static readonly Regex FeatureSpec = new Regex(@"^([+-])(\w+)$", RegexOptions.Compiled);

        public double[] GetFeatureVector(IEnumerable<string> featureSpecs, IList<string> errors)
        {
            var vector = Enumerable.Range(0, NumDimensions)
                .Select(i => UnsetValue).ToArray();

            if (featureIndices != null)
            {
                foreach (var fspec in featureSpecs)
                {
                    var m = FeatureSpec.Match(fspec);
                    if (m.Success)
                    {
                        var fname = m.Groups[2].Value.Trim();
                        if (featureIndices.TryGetValue(fname.ToUpperInvariant(), out int idx))
                            vector[idx] = m.Groups[1].Value == "+" ? PlusValue : MinusValue;
                        else
                            errors.Add(string.Format("unknown feature name '{0}'", fname));
                    }
                    else
                    {
                        errors.Add(string.Format("invalid feature specification '{0}'", fspec));
                    }
                }
            }
            else
            {
                errors.Add("no features are defined");
            }

            return vector;
        }

        public override void Configure(dynamic config)
        {
            base.Configure((object)config);

            if (config.PlusValue != null)
                PlusValue = (double)config.PlusValue;
            if (config.MinusValue != null)
                MinusValue = (double)config.MinusValue;

            UnsetValue = (MinusValue + PlusValue) / 2.0;

            if (config.Features != null)
            {
                featureList = config.Features as IList<string>;
                if (featureList == null)
                {
                    AddError("features are not defined");
                    return;
                }

                featureList = featureList.OrderBy(f => f).ToList();

                featureIndices = featureList.SelectMany(
                    (fnames, i) => fnames.Split(',')
                        .Where(n => !string.IsNullOrWhiteSpace(n))
                        .Select(n => (n, i)))
                    .ToDictionary(ni => ni.Item1.Trim().ToUpperInvariant(), ni => ni.Item2);
            }
        }
    }
}
