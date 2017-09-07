using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        public bool DebugFirstOnly { get; protected set; }
        public IList<TextChunk> Results { get; protected set; }

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            var output = config.Get<string>("output", this);
            if (!string.IsNullOrWhiteSpace(output))
                OutputPath = GetChildPath(AbsoluteFilePath, output);
            else
                AddError("no 'output' path specified");

            DebugFirstOnly = config.Get<bool>("debugFirstOnly", this);

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
            int index = 0;
            foreach (var chunk in chunks)
            {
                results.Add(chunk);
                bodyNode.AppendChild(GenerateReportChunk(chunk, index++));

                if (DebugFirstOnly)
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

            IConfigurable parent = this;
            while (parent != null && !(parent is Flow))
                parent = parent.Parent;
            if (parent != null)
            {
                headerNode.AppendChild(HtmlNode.CreateNode(string.Format("<div class=\"flow-desc\">{0}: {1}</div>", parent.Id, parent.Description)));
            }

            if (this.Container != null)
            {
                foreach (var step in this.Container.Children.OfType<FlowStep>())
                {
                    var report = step.GenerateReport(ReportType.Html);
                    if (string.IsNullOrWhiteSpace(report))
                        continue;

                    var reportNode = HtmlNode.CreateNode(string.Format("<div class=\"step-report\"><div class=\"step-report-title\">{0}: {1}</div><div class=\"step-report-content\">{2}</div></div>", 
                        step.Id, step.Description, report));
                    headerNode.AppendChild(reportNode);
                }
            }

            return headerNode;
        }

        HtmlNode GenerateReportChunk(TextChunk chunk, int index)
        {
            var sectionNode = HtmlNode.CreateNode("<section class=\"text-chunk\"></section>");
            sectionNode.AppendChild(HtmlNode.CreateNode(string.Format("<div class=\"text-chunk-intro\">{0}</div>", HtmlEntity.Entitize(chunk.Description ?? index.ToString(), true, true))));
            var interNode = HtmlNode.CreateNode("<div class=\"text-chunk-lines\"></div>");

            var linesNode = interNode;
            if (chunk.Lines != null && chunk.Lines.Any())
            {
                var firstStep = GetFirstStep();
                var firstLine = chunk.Lines.FirstOrDefault(line => object.ReferenceEquals(line.SourceStep, firstStep));
                if (firstLine == null) firstLine = chunk.Lines.FirstOrDefault();

                string encoding = (firstLine.Segments?.FirstOrDefault()?.Options?.FirstOrDefault()?.Encoding?.Id?.ToLowerInvariant() ?? "default").ToLowerInvariant();

                var textLine = HtmlNode.CreateNode(string.Format("<div class=\"text-line-first\"><span class=\"text-line-label\">Original text:</span> <span class=\"encoding-{1}\">{0}<span></div>", firstLine.Text, encoding));
                linesNode.AppendChild(textLine);

                foreach (var line in chunk.Lines)
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
            lineNode.AppendChild(HtmlNode.CreateNode(string.Format("<div class=\"text-line-intro\">{0}: {1}</div>", line.SourceStep?.Id, line.SourceStep?.Description)));

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
                        if (!string.IsNullOrWhiteSpace(segment.Text) && segment.Text != segment.Options.FirstOrDefault()?.Text)
                        {
                            var textNode = HtmlNode.CreateNode(string.Format("<div class=\"segment-option-first\">{0}</div>", segment.Text));
                            optionsNode.AppendChild(textNode);
                        }

                        foreach (var option in segment.Options)
                        {
                            string encoding = (option.Encoding?.Id ?? "default").ToLowerInvariant();

                            string optionTitle = "";
                            if (option.Phones != null && option.Phones.Any() && option.Encoding?.Features != null)
                            {
                                var sb = new StringBuilder();
                                foreach (var phone in option.Phones)
                                {
                                    sb.AppendFormat("[ {0} ]\n", string.Join(",", option.Encoding.Features.GetFeatureSpec(phone)));
                                }
                                optionTitle = sb.ToString();
                            }
                            if (!string.IsNullOrWhiteSpace(option.Lexicon?.Id))
                            {
                                optionTitle = string.Format("{0}: {1} {2}\n\n", option.Lexicon?.Id, option.Entry?.Lemma, option.Entry?.Encoded)
                                    + optionTitle;
                            }

                            var optionNode = HtmlNode.CreateNode(string.Format("<div class=\"segment-option\" title=\"{1}\"><div class=\"option-text encoding-{2}\">{0}</div><div class=\"option-definition\">{3}</div></div>", 
                                option.Text, optionTitle, encoding, option.Entry?.Definition));
                            optionsNode.AppendChild(optionNode);
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
