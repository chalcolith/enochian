using System;
using System.Collections.Generic;

namespace Enochian.Text
{
    public class Encoding : Configurable, IFileReference
    {
        public static Encoding Default { get; } = new Encoding();

        public string Name { get; internal set; }
        public string RelativePath { get; internal set; }

        public FeatureSet Features { get; internal set; }

        public IList<EncodingPattern> Patterns { get; } = new List<EncodingPattern>();

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
                        Patterns.Add(new EncodingPattern(Features, pattern));
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
        public EncodingPattern(FeatureSet features, IDictionary<string, object> config)
        {
            Features = features;
            Configure(config);
        }

        public FeatureSet Features { get; }

        public string Text { get; private set; }
        public string Spec { get; private set; }

        // TODO: regular expression
        public double[] Vector { get; private set; }

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            Text = config.Get<string>("text", this);
            if (string.IsNullOrWhiteSpace(Text))
            {
                AddError("empty text template");
            }

            var features = config.GetList<string>("features", this);
            if (features != null)
            {
                Spec = string.Join(", ", features);

                if (Features != null)
                {
                    var errors = new List<string>();
                    Vector = Features.GetFeatureVector(features, errors);
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
