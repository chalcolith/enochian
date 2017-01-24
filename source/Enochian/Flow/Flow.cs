using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using Enochian.Text;

namespace Enochian.Flow
{
    public interface IFlowResources
    {
        IList<FeatureSet> FeatureSets { get; }
        IList<Encoding> Encodings { get; }
    }

    public class Flow : Configurable, IFlowResources
    {
        public Flow(string fname)
        {
            Load(fname, this);
        }

        public IList<FeatureSet> FeatureSets { get; } = new List<FeatureSet>();

        public IList<Encoding> Encodings { get; } = new List<Encoding>();

        public FlowContainer Steps { get; private set; }

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            var features = config.GetChildren("Features", this);
            if (features != null)
            {
                foreach (var fset in features)
                {
                    var featureSet = new FeatureSet
                    {
                        Name = fset.Get<string>("Name", this),
                        Path = fset.Get<string>("Path", this),
                    };

                    FeatureSets.Add(Load(this, featureSet, featureSet.Path));
                }
            }

            var encodings = config.GetChildren("Encodings", this);
            if (encodings != null)
            {
                foreach (var enc in encodings)
                {
                    var encoding = new Encoding
                    {
                        Name = enc.Get<string>("Name", this),
                        Path = enc.Get<string>("Path", this),
                        Features = FeatureSets.FirstOrDefault(fs => fs.Name == enc.Get<string>("Features", this)),
                    };

                    if (encoding.Features == null)
                        AddError("unknown feature set '{0}' for encoding '{1}'", enc.Get<string>("Features", this), encoding.Name);

                    Encodings.Add(Load(this, encoding, encoding.Path));
                }
            }

            Steps = new FlowContainer(this, config);
            return this;
        }

        public IEnumerable<object> GetOutputs()
        {
            if (Steps == null)
                yield break;

            foreach (var output in Steps.GetOutputs())
                yield return output;
        }
    }
}
