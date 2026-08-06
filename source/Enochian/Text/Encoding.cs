using Enochian.Flow;
using Verophyle.Regexp;
using Verophyle.Regexp.InputSet;
using Verophyle.Regexp.Node;

namespace Enochian.Text;

public class Encoding(IConfigurable? parent) : RelativeConfigurable(parent)
{
    private static readonly ILogger Logger = Logging.CreateLogger<Encoding>();

    public static Encoding Default { get; } = new Encoding(null) { Id = "Default" };

    public override ILogger Log => Logger;

    public FeatureSet? Features { get; internal set; }

    public IList<EncodingPattern> Patterns { get; } = [];

    public override IEnumerable<IConfigurable> Children => Patterns;

    public override IConfigurable Configure(JsonObject config)
    {
        _ = base.Configure(config);

        var patterns = config.GetChildren("patterns", this);
        if (patterns != null)
        {
            try
            {
                foreach (var pattern in patterns)
                {
                    Patterns.Add(new EncodingPattern(this, Features, pattern));
                }
            }
            catch (Exception e)
            {
                _ = AddError("patterns needs to be a list of pattern configs: {0}", e.Message);
            }
        }
        return this;
    }

    public override void PostConfigure()
    {
        base.PostConfigure();

        if (Patterns.Any(p => !string.IsNullOrWhiteSpace(p.Ipa)))
        {
            // find IPA encoding
            IFlowResources? flowResources = null;
            IConfigurable? cur = this;
            do
            {
                cur = cur?.Parent;
                flowResources = cur as IFlowResources;
            }
            while (flowResources == null && cur != null);

            if (flowResources == null)
            {
                _ = AddError("Unable to find flow resources.");
                return;
            }

            var ipaEncoding = flowResources.Encodings.FirstOrDefault(enc => string.Equals(enc.Id, "ipa", StringComparison.OrdinalIgnoreCase));
            if (ipaEncoding?.Features == null)
            {
                _ = AddError("Unable to find an IPA encoding with a feature set.");
                return;
            }

            var encoder = new Encoder(ipaEncoding.Features, ipaEncoding);
            foreach (var pattern in Patterns.Where(p => !string.IsNullOrWhiteSpace(p.Ipa)))
            {
                var (input, repr, phones) = encoder.GetTextAndPhones(pattern.Ipa ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(repr))
                {
                    pattern.Repr = repr;
                }

                pattern.Phones = phones;
                pattern.FeatureSpecs = [.. phones.Select(p => string.Format(CultureInfo.InvariantCulture, "[{0}]", string.Join(", ", ipaEncoding.Features.GetFeatureSpec(p))))];
            }
        }
    }
}

public class EncodingPattern : Configurable
{
    private static readonly ILogger Logger = Logging.CreateLogger<EncodingPattern>();

    public EncodingPattern(IConfigurable parent, FeatureSet? features, JsonObject config)
        : base(parent)
    {
        Features = features;
        _ = Configure(config);
    }

    public override ILogger Log => Logger;

    public FeatureSet? Features { get; }

    public string Input { get; internal set; } = string.Empty;
    public string? Output { get; internal set; }
    public string? Repr { get; internal set; }
    internal string? Ipa { get; private set; }

    public IList<string> FeatureSpecs { get; internal set; } = [];
    public IList<double[]> Phones { get; internal set; } = [];

    public bool IsReplacement => !Input.Contains('_');

    public override IConfigurable Configure(JsonObject config)
    {
        _ = base.Configure(config);

        Input = config.Get<string>("input", this) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(Input))
        {
            _ = AddError("empty text template");
        }

        Output = config.Get<string>("output", this);
        Repr = config.Get<string>("repr", this);

        var ipa = config.Get<string>("ipa", this);
        if (config["features"] is JsonArray { Count: > 0 } features)
        {
            ConfigureFeatures(features);
        }
        else if (!string.IsNullOrWhiteSpace(ipa))
        {
            Ipa = ipa;
        }
        else
        {
            _ = AddError("invalid or missing ipa or feature spec (needs to be a list of strings or a list of lists of strings)");
        }

        return this;
    }

    private void ConfigureFeatures(JsonArray features)
    {
        if (Features == null)
        {
            _ = AddError("no feature set configured for '{0}'", Input);
            return;
        }

        var specs = new List<string>();
        var vectors = new List<double[]>();
        var errors = new List<string>();

        var fstrings = features
            .OfType<JsonValue>()
            .Select(feature => feature.GetValue<string>())
            .ToList();
        if (fstrings.Count != 0)
        {
            specs.Add(string.Join(", ", fstrings));
            vectors.Add(Features.GetFeatureVector(fstrings, errors));
        }
        else
        {
            var flists = features.OfType<JsonArray>();
            foreach (var flist in flists)
            {
                fstrings = [.. flist.OfType<JsonValue>().Select(feature => feature.GetValue<string>())];
                if (fstrings.Count != 0)
                {
                    specs.Add(string.Join(", ", fstrings));
                    vectors.Add(Features.GetFeatureVector(fstrings, errors));
                }
                else
                {
                    _ = AddError("empty feature set for '{0}'", Input);
                }
            }
        }

        foreach (var error in errors)
        {
            _ = AddError("error in feature spec for '{0}': {1}", Input, error);
        }

        FeatureSpecs = [.. specs];
        Phones = [.. vectors];
    }

    public DeterministicAutomaton<char, UnicodeCategoryMatcher> GetRegexp()
    {
        if (string.IsNullOrEmpty(Input))
        {
            return new DeterministicAutomaton<char, UnicodeCategoryMatcher>();
        }

        int pos = 0;
        Node<char>? seq = null;
        foreach (var ch in Input)
        {
            Node<char> leaf = ch == '_'
                ? new Dot<char>(new DotSet<char>(), ref pos)
                : new Leaf<char>(new CharSet(ch), ref pos);
            seq = seq != null ? new Seq<char>(seq, leaf) : leaf;
        }
        Node<char> end = new End<char>(ref pos);
        seq = seq != null ? new Seq<char>(seq, end) : end;

        return new DeterministicAutomaton<char, UnicodeCategoryMatcher>(seq);
    }
}
