using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Enochian.Text;

namespace Enochian.Flow.Steps
{
    public class MatchReport : FlowStep<TextChunk, string>
    {
        public MatchReport(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }

        public string OutputPath { get; protected set; }
        public IList<TextChunk> Results { get; protected set; }

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            var output = config.Get<string>("output", this);
            if (!string.IsNullOrWhiteSpace(output))
                OutputPath = GetChildPath(AbsoluteFilePath, output);
            else
                AddError("no 'output' path specified");

            return this;
        }

        public override IEnumerable<string> GetOutputs()
        {
            if (Previous != null)
                Results = Previous.GetOutputs().OfType<TextChunk>().ToList();

            var outputPath = Path.GetFullPath(OutputPath);
            try
            {
                using (var sr = new StreamWriter(outputPath))
                {
                    sr.WriteLine("output!");
                }
            }
            catch (Exception e)
            {
                AddError("erorr writing {0}: {1}", outputPath, e.Message);
            }

            yield return OutputPath;
        }
    }
}
