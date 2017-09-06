using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Enochian.Text;
using HtmlAgilityPack;

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
            var outputPath = Path.GetFullPath(OutputPath);
            try
            {
                Log.Info("generating HTML report...");

                var results = Previous != null ? Previous.GetOutputs() : Enumerable.Empty<TextChunk>();
                HtmlDocument document = GenerateReportDocument(results, Results = new List<TextChunk>());

                Log.Info("writing report to " + outputPath);

                var outputDir = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                using (var sw = new StreamWriter(outputPath))
                {
                    document.Save(sw);
                }
            }
            catch (Exception e)
            {
                AddError("error writing {0}: {1}", outputPath, e.Message);
            }

            yield return OutputPath;
        }

        HtmlDocument GenerateReportDocument(IEnumerable<TextChunk> chunks, IList<TextChunk> results)
        {
            var document = new HtmlDocument();
            var htmlNode = HtmlNode.CreateNode("<html></html>");

            htmlNode.AppendChild(GenerateReportHead());
            htmlNode.AppendChild(GenerateReportBody(chunks, results));

            document.DocumentNode.AppendChild(htmlNode);
            return document;
        }

        HtmlNode GenerateReportHead()
        {
            var headNode = HtmlNode.CreateNode("<head><link href=\"enochian.css\" rel=\"stylesheet\" type=\"text/css\"></head>");
            return headNode;
        }

        HtmlNode GenerateReportBody(IEnumerable<TextChunk> chunks, IList<TextChunk> results)
        {
            var bodyNode = HtmlNode.CreateNode("<body></body>");

            bodyNode.AppendChild(GenerateReportHeader(chunks, out DateTime timeGenerated));
            foreach (var chunk in chunks)
            {
                results.Add(chunk);
                bodyNode.AppendChild(GenerateReportChunk(chunk));
                break;
            }
            bodyNode.AppendChild(GenerateReportFooter(results, timeGenerated));

            return bodyNode;
        }

        HtmlNode GenerateReportHeader(IEnumerable<TextChunk> chunks, out DateTime timeGenerated)
        {
            var headerNode = HtmlNode.CreateNode("<header></header>");

            headerNode.AppendChild(HtmlNode.CreateNode(string.Format("<div class=\"title\">{0}</div>", "Phonological Match Report")));
            timeGenerated = DateTime.Now;
            headerNode.AppendChild(HtmlNode.CreateNode(string.Format("<div class=\"subtitle\">Generated {0} ({1})</div>", 
                timeGenerated.ToString("s"), timeGenerated.ToUniversalTime().ToString("u"))));

            return headerNode;
        }

        HtmlNode GenerateReportChunk(TextChunk chunk)
        {
            var sectionNode = HtmlNode.CreateNode("<section class=\"text-chunk\"></section>");
            sectionNode.AppendChild(HtmlNode.CreateNode("<div class=\"text-chunk-intro\"></div>"));
            var interNode = HtmlNode.CreateNode("<div class=\"text-chunk-lines\"></div>");

            var linesNode = interNode;
            if (chunk.Lines != null && chunk.Lines.Any())
            {
                var firstStep = GetFirstStep();
                var firstLine = chunk.Lines.FirstOrDefault(line => object.ReferenceEquals(line.SourceStep, firstStep));
                if (firstLine == null) firstLine = chunk.Lines.FirstOrDefault();

                var textLine = HtmlNode.CreateNode(string.Format("<div class=\"text-line-first\">{0}</div>", firstLine.Text));
                linesNode.AppendChild(textLine);

                foreach (var line in chunk.Lines.Where(l => !object.ReferenceEquals(l, firstLine)))
                {
                    linesNode.AppendChild(GenerateReportLine(line));
                }
            }
            sectionNode.AppendChild(interNode);

            return sectionNode;
        }

        HtmlNode GenerateReportLine(TextLine line)
        {
            var lineNode = HtmlNode.CreateNode("<div class=\"text-line\"><div>");
            lineNode.AppendChild(HtmlNode.CreateNode("<div class=\"text-line-intro\"></div>"));

            var lineDiv = HtmlNode.CreateNode("<div class=\"text-line-content\"></div>");
            lineNode.AppendChild(lineDiv);

            if (line.Segments != null && line.Segments.Any())
            {
                foreach (var segment in line.Segments)
                {
                    var segmentNode = HtmlNode.CreateNode("<div class=\"line-segment\"></div>");
                    if (segment.Options != null && segment.Options.Any())
                    {
                        var optionsNode = HtmlNode.CreateNode("<div class=\"segment-options\"></div>");
                        foreach (var option in segment.Options)
                        {
                            var textNode = HtmlNode.CreateNode(string.Format("<div class=\"segment-option\">{0}</div>", option.Text));                            
                            optionsNode.AppendChild(textNode);

                            if (option.Phones != null && option.Encoding.Features != null && option.Phones.Any())
                            {
                                var phonesNode = HtmlNode.CreateNode(string.Format("<div class\"options-phones\"></div>"));

                                foreach (var phone in option.Phones)
                                {
                                    var phoneNode = HtmlNode.CreateNode(string.Format("<div class=\"option-phone\">[{0}]</div>",
                                        string.Join(",", option.Encoding.Features.GetFeatureSpec(phone))));
                                    phonesNode.AppendChild(phonesNode);
                                }

                                optionsNode.AppendChild(phonesNode);
                            }
                        }
                        segmentNode.AppendChild(optionsNode);
                    }
                    lineDiv.AppendChild(segmentNode);
                }
            }

            return lineNode;
        }

        HtmlNode GenerateReportFooter(IEnumerable<TextChunk> chunks, DateTime timeGenerated)
        {
            var footerNode = HtmlNode.CreateNode("<footer></footer>");
            return footerNode;
        }
    }
}
