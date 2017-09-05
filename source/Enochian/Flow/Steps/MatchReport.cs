using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Enochian.Text;

namespace Enochian.Flow.Steps
{
    public class MatchReport : FlowStep<TextChunk, string>
    {
        static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public MatchReport(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }

        public override NLog.Logger Log => logger;

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
                Log.Info("writing report to " + outputPath);

                var outputDir = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                using (var sr = new StreamWriter(outputPath))
                {
                    sr.WriteLine("output!");
                }
            }
            catch (Exception e)
            {
                AddError("error writing {0}: {1}", outputPath, e.Message);
            }

            yield return OutputPath;
        }
    }
}
