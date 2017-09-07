using System;
using System.Collections.Generic;
using System.Linq;
using Verophyle.Regexp;
using Verophyle.Regexp.InputSet;
using Verophyle.Regexp.Node;

namespace Enochian.Text
{
    public class Encoding : Configurable, IFileReference
    {
        static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public static Encoding Default { get; } = new Encoding(null) { Id = "Default" };

        public Encoding(IConfigurable parent)
            : base(parent)
        {
        }

        public override NLog.Logger Log => logger;

        public string RelativePath { get; internal set; }

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

        public string Input { get; private set; }
        public string Output { get; private set; }
        public string Repr { get; private set; }

        public IList<string> FeatureSpecs { get; private set; }

        public bool IsReplacement => Input != null && !Input.Contains("_");

        public IList<double[]> Phones { get; private set; }

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
            if (features != null)
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
            else
            {
                AddError("invalid feature spec (needs to be a list of strings or a list of lists of strings)");
            }

            return this;
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
