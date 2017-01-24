using System;
using System.Collections.Generic;
using System.Linq;
using Enochian.Text;

namespace Enochian.Flow.Steps
{
    public class Transducer : FlowStep<TextChunk, TextChunk>
    {
        public Transducer(IFlowResources resources)
            : base(resources)
        {
        }

        public FeatureSet Features { get; private set; }

        public Encoding Input { get; private set; }
        public Encoding Output { get; private set; }

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            if (Resources != null)
            {
                var features = config.Get<string>("Features", this);
                Features = Resources.FeatureSets.FirstOrDefault(fs => fs.Name == features);
                if (Features == null)
                    AddError("invalid features name '{0}'", features);

                var inputEncoding = config.Get<string>("InputEncoding", this);
                Input = Resources.Encodings.FirstOrDefault(enc => enc.Name == inputEncoding);
                if (Input == null)
                    AddError("invalid inputEncoding name '{0}'", inputEncoding);

                var outputEncoding = config.Get<string>("OutputEncoding", this);
                Output = Resources.Encodings.FirstOrDefault(enc => enc.Name == outputEncoding);
                if (Output == null)
                    AddError("invalid outputEncoding name '{0}'", outputEncoding);
            }
            else
            {
                AddError("no resources specified for Transducer");
            }

            return this;
        }
    }
}
