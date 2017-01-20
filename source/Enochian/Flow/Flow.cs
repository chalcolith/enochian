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

        public override IConfigurable Configure(dynamic config)
        {
            base.Configure((object)config);

            if (config.Features != null)
            {
                foreach (dynamic fset in config.Features)
                {
                    var featureSet = new FeatureSet
                    {
                        Name = fset.Name,
                        Path = fset.Path,
                    };

                    FeatureSets.Add(Load(this, featureSet, featureSet.Path));
                }
            }

            if (config.Encodings != null)
            {
                foreach (dynamic enc in config.Encodings)
                {
                    var encoding = new Encoding
                    {
                        Name = enc.Name,
                        Path = enc.Path,
                        Features = FeatureSets.FirstOrDefault(fs => fs.Name == enc.Features),
                    };

                    if (encoding.Features == null)
                        AddError("unknown feature set '{0}' for encoding '{1}'", enc.Features, enc.Name);

                    Encodings.Add(Load(this, encoding, encoding.Path));
                }
            }

            if (config.Steps != null)
            {
                Steps = new FlowContainer(this, config.Steps);
            }

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
