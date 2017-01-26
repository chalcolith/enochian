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

        public override IEnumerable<IConfigurable> Children => 
            FeatureSets.Concat<IConfigurable>(Encodings).Concat(Steps != null
                ? new IConfigurable[] { Steps } : Enumerable.Empty<IConfigurable>());

        public IList<FeatureSet> FeatureSets { get; } = new List<FeatureSet>();

        public IList<Encoding> Encodings { get; } = new List<Encoding>();

        public FlowContainer Steps { get; private set; }        

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            var features = config.GetChildren("features", this);
            if (features != null)
            {
                foreach (var fset in features)
                {
                    var featureSet = new FeatureSet
                    {
                        Name = fset.Get<string>("name", this),
                        RelativePath = fset.Get<string>("path", this),
                    };

                    FeatureSets.Add(Load(this, featureSet, featureSet.RelativePath));
                }
            }

            var encodings = config.GetChildren("encodings", this);
            if (encodings != null)
            {
                foreach (var enc in encodings)
                {
                    var featuresName = enc.Get<string>("features", this);
                    var encoding = new Encoding
                    {
                        Name = enc.Get<string>("name", this),
                        RelativePath = enc.Get<string>("path", this),
                        Features = FeatureSets.FirstOrDefault(fs => fs.Name == featuresName),
                    };

                    if (encoding.Features == null)
                        AddError("unknown feature set '{0}' for encoding '{1}'", featuresName, encoding.Name);

                    Encodings.Add(Load(this, encoding, encoding.RelativePath));
                }
            }

            if (!Encodings.Any(e => e.Name == Encoding.Default.Name))
                Encodings.Add(Encoding.Default);

            Steps = new FlowContainer(this, config);
            return this;
        }

        public IEnumerable<object> GetOutputs()
        {
            if (Steps == null)
                yield break;

            foreach (var output in Steps.GetOutputs())
            {
                if (output != null)
                    yield return output;
            }
        }
    }
}
