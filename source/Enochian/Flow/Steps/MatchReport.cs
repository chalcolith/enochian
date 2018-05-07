using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
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

        public string Output { get; protected set; }
        public string PreviousPage { get; protected set; }
        public string NextPage { get; protected set; }

        public bool DebugFirstOnly { get; protected set; }
        public IList<TextChunk> Results { get; protected set; }

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            var output = config.Get<string>("output", this);
            if (!string.IsNullOrWhiteSpace(output))
                Output = GetChildPath(AbsoluteFilePath, output);
            else
                AddError("no 'output' path specified");

            var previousPage = config.Get<string>("previousPage", this);
            if (!string.IsNullOrWhiteSpace(previousPage))
                PreviousPage = GetChildPath(AbsoluteFilePath, previousPage);
            var nextPage = config.Get<string>("nextPage", this);
            if (!string.IsNullOrWhiteSpace(nextPage))
                NextPage = GetChildPath(AbsoluteFilePath, nextPage);

            DebugFirstOnly = config.Get<bool>("debugFirstOnly", this);

            return this;
        }

        public override IEnumerable<string> GetOutputs()
        {
            var outputPath = Path.GetFullPath(Output);
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
                AddError("error writing {0}: {1}\n{2}", outputPath, e.Message, e.StackTrace);
            }

            yield return Output;
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

            int entryId = 0;
            bodyNode.AppendChild(GenerateReportHeader(chunks, out DateTime timeGenerated, ref entryId));

            var contentsNode = HtmlNode.CreateNode("<div class=\"contents\"></div>");
            var chunksNode = HtmlNode.CreateNode("<div class=\"chunks\"></div>");
            var idCounts = new Dictionary<string, int>();
            int index = 0;
            foreach (var chunk in chunks)
            {
                GetChunkNameAndId(chunk, index, idCounts, out string name, out string id);
                contentsNode.AppendChild(HtmlNode.CreateNode(string.Format("<span><a href=\"#{0}\">{1}</a></span>&nbsp;&nbsp;", id, HtmlEntity.Entitize(name, true, true))));

                results.Add(chunk);
                chunksNode.AppendChild(GenerateReportChunk(chunk, index, name, id, ref entryId));

                if (DebugFirstOnly)
                    break;

                index++;
            }
            bodyNode.AppendChild(contentsNode);
            bodyNode.AppendChild(chunksNode);
            bodyNode.AppendChild(GenerateReportFooter(results, timeGenerated, ref entryId));

            return bodyNode;
        }

        HtmlNode GenerateReportHeader(IEnumerable<TextChunk> chunks, out DateTime timeGenerated, ref int entryId)
        {
            var headerNode = HtmlNode.CreateNode("<header></header>");

            var navNode = HtmlNode.CreateNode("<div class=\"nav\"></div>");
            if (!string.IsNullOrWhiteSpace(PreviousPage))
                navNode.AppendChild(HtmlNode.CreateNode(string.Format("<div class=\"prev\"><a href=\"{0}\">Previous</a></div>", PreviousPage)));
            if (!string.IsNullOrWhiteSpace(NextPage))
                navNode.AppendChild(HtmlNode.CreateNode(string.Format("<div class=\"prev\"><a href=\"{0}\">Next</a></div>", NextPage)));
            headerNode.AppendChild(navNode);

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

        static readonly Regex WordRegex = new Regex(@"[^\w\d]+");

        void GetChunkNameAndId(TextChunk chunk, int index, IDictionary<string, int> idCounts, out string name, out string id)
        {
            name = chunk.Description ?? index.ToString();
            id = WordRegex.Replace(name, "");

            if (idCounts != null)
            {
                if (idCounts.ContainsKey(id))
                {
                    idCounts[id] = idCounts[id] + 1;
                    id = id + idCounts[id].ToString();
                }
                else
                {
                    idCounts[id] = 1;
                }
            }
        }

        HtmlNode GenerateReportChunk(TextChunk chunk, int index, string name, string id, ref int entryId)
        {
            var sectionNode = HtmlNode.CreateNode(string.Format("<section id=\"{0}\" class=\"text-chunk\"></section>", id));
            sectionNode.AppendChild(HtmlNode.CreateNode(string.Format("<div class=\"text-chunk-intro\">{0}</div>", HtmlEntity.Entitize(name, true, true))));
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
                    linesNode.AppendChild(GenerateReportLine(line, ref entryId));
                }
            }
            sectionNode.AppendChild(interNode);

            return sectionNode;
        }

        HtmlNode GenerateReportLine(TextLine line, ref int entryId)
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

                        int numOptions = 0;
                        if (!string.IsNullOrWhiteSpace(segment.Text) && segment.Text != segment.Options.FirstOrDefault()?.Text)
                        {
                            numOptions++;
                            var encoding = (segment.SourceSegments?.FirstOrDefault()?.Options?.FirstOrDefault().Encoding?.Id ?? "default").ToLowerInvariant();
                            var textNode = HtmlNode.CreateNode(string.Format("<div class=\"option-first encoding-{1}\">{0}</div>", segment.Text, encoding));
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
                                optionTitle += sb.ToString();
                            }
                            if (!string.IsNullOrWhiteSpace(option.Entry?.Lemma))
                            {
                                optionTitle = string.Format("{0}: {1} {2}\n{3}\n\n", option.Entry?.Lexicon?.Id, option.Entry?.Lemma, option.Entry?.Encoded, option.Entry?.Definition)
                                    + optionTitle;
                            }

                            string classes = "segment-option";
                            if (numOptions++ == 0) classes += " option-first";
                            if ((option.Tags & TextTag.Hypo) != TextTag.None) classes += " option-hypo";
                            if ((option.Tags & TextTag.Repr) != TextTag.None) classes += " option-repr";

                            var optionNode = HtmlNode.CreateNode(string.Format("<div id=\"entry{4}\" class=\"{5}\" title=\"{1}\"><div class=\"option-text encoding-{2}\">{0}</div><div class=\"option-definition\">{3}</div></div>", 
                                option.Text, optionTitle, encoding, option.Entry?.Definition.Replace("\n", "<br/>"), entryId++, classes));
                            optionsNode.AppendChild(optionNode);
                        }
                        segmentNode.AppendChild(optionsNode);
                    }
                    lineDiv.AppendChild(segmentNode);
                }
            }

            return lineNode;
        }

        HtmlNode GenerateReportFooter(IEnumerable<TextChunk> chunks, DateTime timeGenerated, ref int entryId)
        {
            var footerNode = HtmlNode.CreateNode("<footer></footer>");
            var navNode = HtmlNode.CreateNode("<div class=\"nav\"></div>");
            if (!string.IsNullOrWhiteSpace(PreviousPage))
                navNode.AppendChild(HtmlNode.CreateNode(string.Format("<div class=\"prev\"><a href=\"{0}\">Previous</a></div>", PreviousPage)));
            if (!string.IsNullOrWhiteSpace(NextPage))
                navNode.AppendChild(HtmlNode.CreateNode(string.Format("<div class=\"prev\"><a href=\"{0}\">Next</a></div>", NextPage)));
            footerNode.AppendChild(navNode);
            return footerNode;
        }
    }
}
