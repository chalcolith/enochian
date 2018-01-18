using System;
using System.Collections.Generic;
using System.Linq;
using Enochian.Flow;
using Verophyle.Regexp;
using Verophyle.Regexp.InputSet;
using Verophyle.Regexp.Node;

namespace Enochian.Text
{
    public class Encoding : RelativeConfigurable
    {
        static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public static Encoding Default { get; } = new Encoding(null) { Id = "Default" };

        public Encoding(IConfigurable parent)
            : base(parent)
        {
        }

        public override NLog.Logger Log => logger;

        public FeatureSet Features { get; internal set; }

        public IList<EncodingPattern> Patterns { get; } = new List<EncodingPattern>();

        public override IEnumerable<IConfigurable> Children => Patterns;

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

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
                    AddError("patterns needs to be a list of pattern configs: {0}", e.Message);
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
                IFlowResources flowResources = null;
                IConfigurable cur = this;
                do
                {
                    cur = cur.Parent;
                    flowResources = cur as IFlowResources;
                }
                while (flowResources == null && cur != null);

                if (flowResources == null)
                {
                    AddError("Unable to find flow resources.");
                    return;
                }

                var ipaEncoding = flowResources.Encodings.FirstOrDefault(enc => enc.Id.Equals("ipa", StringComparison.InvariantCultureIgnoreCase));
                if (ipaEncoding == null)
                {
                    AddError("Unable to find IPA encodingl.");
                    return;
                }

                var encoder = new Encoder(ipaEncoding.Features, ipaEncoding);
                foreach (var pattern in Patterns.Where(p => !string.IsNullOrWhiteSpace(p.Ipa)))
                {
                    var (input, repr, phones) = encoder.GetTextAndPhones(pattern.Ipa);
                    pattern.Repr = repr;
                    pattern.Phones = phones;
                    pattern.FeatureSpecs = phones.Select(p => string.Format("[{0}]", string.Join(", ", ipaEncoding.Features.GetFeatureSpec(p)))).ToList();
                }
            }
        }
    }

    public class EncodingPattern : Configurable
    {
        static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public EncodingPattern(IConfigurable parent, FeatureSet features, IDictionary<string, object> config)
            : base(parent)
        {
            Features = features;
            Configure(config);
        }

        public override NLog.Logger Log => logger;

        public FeatureSet Features { get; }

        public string Input { get; internal set; }
        public string Output { get; internal set; }
        public string Repr { get; internal set; }
        internal string Ipa { get; private set; }

        public IList<string> FeatureSpecs { get; internal set; }
        public IList<double[]> Phones { get; internal set; }

        public bool IsReplacement => Input != null && !Input.Contains("_");

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            Input = config.Get<string>("input", this);
            if (string.IsNullOrWhiteSpace(Input))
            {
                AddError("empty text template");
            }

            Output = config.Get<string>("output", this);
            Repr = config.Get<string>("repr", this);

            var features = config.GetList<object>("features", this);
            var ipa = config.Get<string>("ipa", this);
            if (features != null)
            {
                ConfigureFeatures(features);
            }
            else if (!string.IsNullOrWhiteSpace(ipa))
            {
                Ipa = ipa;
            }
            else
            {
                AddError("invalid or missing ipa or feature spec (needs to be a list of strings or a list of lists of strings)");
            }

            return this;
        }

        void ConfigureFeatures(IEnumerable<object> features)
        {
            var specs = new List<string>();
            var vectors = new List<double[]>();
            var errors = new List<string>();

            var fstrings = features.OfType<string>().ToList();
            if (fstrings.Any())
            {
                specs.Add(string.Join(", ", fstrings));
                vectors.Add(Features.GetFeatureVector(fstrings, errors));
            }
            else
            {
                var flists = features.OfType<IEnumerable<object>>().ToList();
                foreach (var flist in flists)
                {
                    fstrings = flist.OfType<string>().ToList();
                    if (fstrings.Any())
                    {
                        specs.Add(string.Join(", ", fstrings));
                        vectors.Add(Features.GetFeatureVector(fstrings, errors));
                    }
                    else
                    {
                        AddError("empty feature set for '{0}'", Input);
                    }
                }
            }

            foreach (var error in errors)
                AddError("error in feature spec for '{0}': {1}", Input, error);

            FeatureSpecs = specs.ToArray();
            Phones = vectors.ToArray();
        }

        public DeterministicAutomaton<char, UnicodeCategoryMatcher> GetRegexp()
        {
            if (string.IsNullOrEmpty(Input))
                return new DeterministicAutomaton<char, UnicodeCategoryMatcher>();

            int pos = 0;
            Node<char> seq = null;
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
}
