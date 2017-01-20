using System;
using System.Collections.Generic;

namespace Enochian.Text
{
    public class Encoding : Configurable, ILoadedFromFile
    {
        public static Encoding Default { get; } = new Encoding();

        public string Name { get; internal set; }
        public string Path { get; internal set; }

        public FeatureSet Features { get; internal set; }

        public IList<EncodingPattern> Patterns { get; } = new List<EncodingPattern>();

        public override IConfigurable Configure(dynamic config)
        {
            base.Configure((object)config);

            if (config.Patterns != null)
            {
                try
                {
                    foreach (dynamic pat in config.Patterns)
                    {
                        Patterns.Add(new EncodingPattern(Features, pat));
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
        public EncodingPattern(FeatureSet features, object config)
        {
            Features = features;
            Configure(config);
        }

        public FeatureSet Features { get; }

        public string Text { get; private set; }
        public string Spec { get; private set; }

        // TODO: regular expression
        public double[] Vector { get; private set; }

        public override IConfigurable Configure(dynamic config)
        {
            base.Configure((object)config);

            Text = config.Text;
            if (string.IsNullOrWhiteSpace(Text))
            {
                AddError("empty text template");
            }
            
            var feats = config.Features as IList<string>;
            if (feats != null)
            {
                Spec = string.Join(", ", feats);

                if (Features != null)
                {
                    var errors = new List<string>();
                    Vector = Features.GetFeatureVector(feats, errors);
                    foreach (var error in errors)
                        AddError("error in feature spec for '{0}': {1}", Text, error);
                }
                else
                {
                    AddError("null feature set for '{0}'", Text);
                }
            }
            else
            {
                AddError("invalid feature spec (needs to be a list of strings)");
            }

            return this;
        }
    }
}
