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

        public override IConfigurable Configure(dynamic config)
        {
            base.Configure((object)config);

            if (Resources != null)
            {
                Features = Resources.FeatureSets.FirstOrDefault(fs => fs.Name == config.Features);
                if (Features == null)
                    AddError("invalid features name '{0}'", config.Features);

                Input = Resources.Encodings.FirstOrDefault(enc => enc.Name == config.InputEncoding);
                if (Input == null)
                    AddError("invalid inputEncoding name '{0}'", config.Input);

                Output = Resources.Encodings.FirstOrDefault(enc => enc.Name == config.OutputEncoding);
                if (Output == null)
                    AddError("invalid outputEncoding name '{0}'", config.Output);
            }
            else
            {
                AddError("no resources specified for Transducer");
            }

            return this;
        }
    }
}
