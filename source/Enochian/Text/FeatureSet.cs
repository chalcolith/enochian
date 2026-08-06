using System.Text.RegularExpressions;

namespace Enochian.Text;

public class FeatureSet(IConfigurable? parent) : RelativeConfigurable(parent)
{
    private static readonly ILogger Logger = Logging.CreateLogger<FeatureSet>();

    private IList<string>? featureList;
    private Dictionary<string, int>? featureIndices;

    public override ILogger Log => Logger;

    public double PlusValue { get; private set; } = 1.0;
    public double UnsetValue { get; private set; }
    public double MinusValue { get; private set; } = -1.0;

    public IList<string> FeatureList
    {
        get { return featureList ??= []; }
    }

    public int NumDimensions
    {
        get { return featureList?.Count ?? 0; }
    }

    private static readonly Regex FeatureSpec = new(@"^([+-])(\w+)$", RegexOptions.Compiled);

    public override IConfigurable Configure(JsonObject config)
    {
        _ = base.Configure(config);

        var plusValue = config.Get<double?>("plusValue", this);
        if (plusValue != null)
        {
            PlusValue = plusValue.Value;
        }

        var minusValue = config.Get<double?>("minusValue", this);
        if (minusValue != null)
        {
            MinusValue = minusValue.Value;
        }

        UnsetValue = (MinusValue + PlusValue) / 2.0;

        var features = config.GetList<string>("features", this);
        if (features != null)
        {
            featureList = [.. features.OrderBy(f => f)];

            featureIndices = featureList.SelectMany(
                (fnames, i) => fnames.Split(',')
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => (n, i)))
                .ToDictionary(ni => ni.n.Trim().ToUpperInvariant(), ni => ni.i);
        }
        else
        {
            _ = AddError("features are not defined");
        }

        return this;
    }

    public double[] GetFeatureVector(IEnumerable<string> featureSpecs, IList<string> errors)
    {
        var vector = GetUnsetVector();

        if (featureIndices != null)
        {
            foreach (var fspec in featureSpecs)
            {
                if (string.IsNullOrWhiteSpace(fspec))
                {
                    continue;
                }

                var m = FeatureSpec.Match(fspec.Trim());
                if (m.Success)
                {
                    var fname = m.Groups[2].Value.Trim();
                    if (featureIndices.TryGetValue(fname.ToUpperInvariant(), out int idx))
                    {
                        vector[idx] = m.Groups[1].Value == "+" ? PlusValue : MinusValue;
                    }
                    else
                    {
                        errors.Add(string.Format(CultureInfo.InvariantCulture, "unknown feature name '{0}'", fname));
                    }
                }
                else
                {
                    errors.Add(string.Format(CultureInfo.InvariantCulture, "invalid feature specification '{0}'", fspec));
                }
            }
        }
        else
        {
            errors.Add("no features are defined");
        }

        return vector;
    }

    public IEnumerable<string> GetFeatureSpec(double[] vector)
    {
        int n = System.Math.Min(vector.Length, NumDimensions);
        for (int i = 0; i < n; i++)
        {
            if (vector[i] == PlusValue)
            {
                yield return "+" + FeatureList[i].Split(',').Last();
            }
            else if (vector[i] == MinusValue)
            {
                yield return "-" + FeatureList[i].Split(',').Last();
            }
        }
    }

    public double[] GetUnsetVector()
    {
        return [.. Enumerable.Range(0, NumDimensions).Select(i => UnsetValue)];
    }

    public double[] Override(double[] orig, double[] ovr)
    {
        double[] result = new double[orig.Length];
        int n = System.Math.Min(orig.Length, ovr.Length);
        for (int i = 0; i < n; i++)
        {
            result[i] = ovr[i] != UnsetValue
                ? ovr[i]
                : orig[i];
        }
        return result;
    }
}
