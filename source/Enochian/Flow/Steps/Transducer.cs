using System;
using System.Collections.Generic;
using System.Linq;
using Enochian.Text;
using Verophyle.Regexp;
using Verophyle.Regexp.InputSet;

namespace Enochian.Flow.Steps
{
    public class Transducer : FlowStep<TextChunk, TextChunk>
    {
        Encoder encoder;
        Encoder Encoder => encoder ?? (encoder = new Encoder(Features, OutputEncoding));

        public Transducer(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }

        public FeatureSet Features { get; private set; }

        public Encoding InputEncoding { get; private set; }
        public Encoding OutputEncoding { get; private set; }

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            if (Resources != null)
            {
                var features = config.Get<string>("features", this);
                Features = Resources.FeatureSets.FirstOrDefault(fs => fs.Id == features);
                if (Features == null)
                    AddError("invalid features name '{0}'", features);

                var inputEncoding = config.Get<string>("inputEncoding", this);
                InputEncoding = Resources.Encodings.FirstOrDefault(enc => enc.Id == inputEncoding);
                if (InputEncoding == null)
                    AddError("invalid inputEncoding name '{0}'", inputEncoding);

                var outputEncoding = config.Get<string>("outputEncoding", this);
                OutputEncoding = Resources.Encodings.FirstOrDefault(enc => enc.Id == outputEncoding);
                if (OutputEncoding == null)
                    AddError("invalid outputEncoding name '{0}'", outputEncoding);
            }
            else
            {
                AddError("no resources specified for Transducer");
            }

            return this;
        }

        protected override TextChunk ProcessTyped(TextChunk input)
        {
            var inputLines = input.Lines.Where(line => line.Encoding == InputEncoding);
            var outputLines = inputLines.Select(line => new Interline
            {
                Encoding = OutputEncoding,
                Segments = line.Segments.Select(seg => Encoder.ProcessSegment(seg)).ToList(),
            });
            var output = new TextChunk
            {
                Lines = input.Lines.Concat(outputLines).ToList(),
            };
            return output;
        }
    }
}
