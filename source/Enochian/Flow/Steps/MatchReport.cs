using Enochian.Text;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Enochian.Flow.Steps;

public class MatchReport(IConfigurable parent, IFlowResources resources) : FlowStep<TextChunk, string>(parent, resources)
{
    private static readonly ILogger Logger = Logging.CreateLogger<MatchReport>();

    public override ILogger Log => Logger;

    public string? Output { get; protected set; }
    public string? PreviousPage { get; protected set; }
    public string? NextPage { get; protected set; }

    public bool DebugFirstOnly { get; protected set; }
    public IList<TextChunk>? Results { get; protected set; }

    public override IConfigurable Configure(JsonObject config)
    {
        _ = base.Configure(config);

        var output = config.Get<string>("output", this);
        if (!string.IsNullOrWhiteSpace(output))
        {
            Output = GetChildPath(AbsoluteFilePath, output);
        }
        else
        {
            _ = AddError("no 'output' path specified");
        }

        var previousPage = config.Get<string>("previousPage", this);
        if (!string.IsNullOrWhiteSpace(previousPage))
        {
            PreviousPage = GetChildPath(AbsoluteFilePath, previousPage);
        }

        var nextPage = config.Get<string>("nextPage", this);
        if (!string.IsNullOrWhiteSpace(nextPage))
        {
            NextPage = GetChildPath(AbsoluteFilePath, nextPage);
        }

        DebugFirstOnly = config.Get<bool>("debugFirstOnly", this);

        return this;
    }

    public override IEnumerable<string> GetOutputs()
    {
        if (string.IsNullOrWhiteSpace(Output))
        {
            yield break;
        }

        var outputPath = Path.GetFullPath(Output);
        try
        {
            Log.LogInformation("generating HTML report...");

            var results = Previous != null ? Previous.GetOutputs() : [];
            XDocument document = GenerateReportDocument(results, Results = []);

            Log.LogInformation("writing report to {OutputPath}", outputPath);

            var outputDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(outputDir) && !Directory.Exists(outputDir))
            {
                _ = Directory.CreateDirectory(outputDir);
            }

            using var sw = new StreamWriter(outputPath);
            document.Save(sw);
        }
        catch (Exception e)
        {
            _ = AddError("error writing {0}: {1}\n{2}", outputPath, e.Message, e.StackTrace);
        }

        yield return Output;
    }

    private XDocument GenerateReportDocument(IEnumerable<TextChunk> chunks, IList<TextChunk> results)
    {
        var document = new XDocument();
        var htmlNode = ParseElement("<html></html>");

        htmlNode.Add(GenerateReportHead());
        htmlNode.Add(GenerateReportBody(chunks, results));

        document.Add(htmlNode);
        return document;
    }

    private static XElement GenerateReportHead()
    {
        var headNode = ParseElement("<head><link href=\"enochian.css\" rel=\"stylesheet\" type=\"text/css\" /></head>");
        return headNode;
    }

    private XElement GenerateReportBody(IEnumerable<TextChunk> chunks, IList<TextChunk> results)
    {
        var bodyNode = ParseElement("<body></body>");

        int entryId = 0;
        bodyNode.Add(GenerateReportHeader());

        var contentsNode = ParseElement("<div class=\"contents\"></div>");
        var chunksNode = ParseElement("<div class=\"chunks\"></div>");
        var idCounts = new Dictionary<string, int>();
        int index = 0;
        foreach (var chunk in chunks)
        {
            GetChunkNameAndId(chunk, index, idCounts, out string name, out string id);
            contentsNode.Add(ParseElement(string.Format(CultureInfo.InvariantCulture, "<span><a href=\"#{0}\">{1}</a></span>", Encode(id), Encode(name))));
            contentsNode.Add(new XText("  "));

            results.Add(chunk);
            chunksNode.Add(GenerateReportChunk(chunk, name, id, ref entryId));

            if (DebugFirstOnly)
            {
                break;
            }

            index++;
        }
        bodyNode.Add(contentsNode);
        bodyNode.Add(chunksNode);
        bodyNode.Add(GenerateReportFooter());

        return bodyNode;
    }

    private XElement GenerateReportHeader()
    {
        var headerNode = ParseElement("<header></header>");

        var navNode = ParseElement("<div class=\"nav\"></div>");
        if (!string.IsNullOrWhiteSpace(PreviousPage))
        {
            navNode.Add(ParseElement(string.Format(CultureInfo.InvariantCulture, "<div class=\"prev\"><a href=\"{0}\">Previous</a></div>", Encode(PreviousPage))));
        }

        if (!string.IsNullOrWhiteSpace(NextPage))
        {
            navNode.Add(ParseElement(string.Format(CultureInfo.InvariantCulture, "<div class=\"prev\"><a href=\"{0}\">Next</a></div>", Encode(NextPage))));
        }

        headerNode.Add(navNode);

        headerNode.Add(ParseElement("<div class=\"title\">Phonological Match Report</div>"));
        var timeGenerated = DateTime.Now;
        headerNode.Add(ParseElement(string.Format(CultureInfo.InvariantCulture, "<div class=\"subtitle\">Generated {0} ({1})</div>",
            Encode(timeGenerated.ToString("s")), Encode(timeGenerated.ToUniversalTime().ToString("u")))));

        IConfigurable? parent = this;
        while (parent is not null and not Flow)
        {
            parent = parent.Parent;
        }

        if (parent != null)
        {
            headerNode.Add(ParseElement(string.Format(CultureInfo.InvariantCulture, "<div class=\"flow-desc\">{0}: {1}</div>", Encode(parent.Id), Encode(parent.Description))));
        }

        if (Container != null)
        {
            foreach (var step in Container.Children.OfType<FlowStep>())
            {
                var report = step.GenerateReport(ReportType.Html);
                if (string.IsNullOrWhiteSpace(report))
                {
                    continue;
                }

                var reportNode = ParseElement(string.Format(CultureInfo.InvariantCulture, "<div class=\"step-report\"><div class=\"step-report-title\">{0}: {1}</div><div class=\"step-report-content\">{2}</div></div>",
                    Encode(step.Id), Encode(step.Description), report));
                headerNode.Add(reportNode);
            }
        }

        return headerNode;
    }

    private static readonly Regex WordRegex = new(@"[^\w\d]+");

    private static void GetChunkNameAndId(TextChunk chunk, int index, Dictionary<string, int> idCounts, out string name, out string id)
    {
        name = chunk.Description ?? index.ToString(CultureInfo.InvariantCulture);
        id = WordRegex.Replace(name, "");

        if (idCounts != null)
        {
            if (idCounts.TryGetValue(id, out int value))
            {
                idCounts[id] = value + 1;
                id += idCounts[id].ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                idCounts[id] = 1;
            }
        }
    }

    private XElement GenerateReportChunk(TextChunk chunk, string name, string id, ref int entryId)
    {
        var sectionNode = ParseElement(string.Format(CultureInfo.InvariantCulture, "<section id=\"{0}\" class=\"text-chunk\"></section>", Encode(id)));
        sectionNode.Add(ParseElement(string.Format(CultureInfo.InvariantCulture, "<div class=\"text-chunk-intro\">{0}</div>", Encode(name))));
        var interNode = ParseElement("<div class=\"text-chunk-lines\"></div>");

        var linesNode = interNode;
        if (chunk.Lines != null && chunk.Lines.Any())
        {
            var firstStep = GetFirstStep();
            var firstLine = chunk.Lines.FirstOrDefault(line => ReferenceEquals(line.SourceStep, firstStep));
            firstLine ??= chunk.Lines.FirstOrDefault();
            if (firstLine == null)
            {
                return sectionNode;
            }

            string encoding = (firstLine.Segments?.FirstOrDefault()?.Options?.FirstOrDefault()?.Encoding?.Id?.ToLowerInvariant() ?? "default").ToLowerInvariant();

            var textLine = ParseElement(string.Format(CultureInfo.InvariantCulture, "<div class=\"text-line-first\"><span class=\"text-line-label\">Original text:</span> <span class=\"encoding-{1}\">{0}</span></div>", Encode(firstLine.Text), Encode(encoding)));
            linesNode.Add(textLine);

            foreach (var line in chunk.Lines)
            {
                linesNode.Add(GenerateReportLine(line, ref entryId));
            }
        }
        sectionNode.Add(interNode);

        return sectionNode;
    }

    private static XElement GenerateReportLine(TextLine line, ref int entryId)
    {
        var lineNode = ParseElement("<div class=\"text-line\"></div>");
        lineNode.Add(ParseElement(string.Format(CultureInfo.InvariantCulture, "<div class=\"text-line-intro\">{0}: {1}</div>", Encode(line.SourceStep?.Id), Encode(line.SourceStep?.Description))));

        var lineDiv = ParseElement("<div class=\"text-line-content\"></div>");
        lineNode.Add(lineDiv);

        if (line.Segments != null && line.Segments.Any())
        {
            foreach (var segment in line.Segments)
            {
                var segmentNode = ParseElement("<div class=\"line-segment\"></div>");
                if (segment.Options != null && segment.Options.Any())
                {
                    var optionsNode = ParseElement("<div class=\"segment-options\"></div>");

                    int numOptions = 0;
                    if (!string.IsNullOrWhiteSpace(segment.Text) && segment.Text != segment.Options.FirstOrDefault()?.Text)
                    {
                        numOptions++;
                        var encoding = (segment.SourceSegments?.FirstOrDefault()?.Options?.FirstOrDefault()?.Encoding?.Id ?? "default").ToLowerInvariant();
                        var textNode = ParseElement(string.Format(CultureInfo.InvariantCulture, "<div class=\"option-first encoding-{1}\">{0}</div>", Encode(segment.Text), Encode(encoding)));
                        optionsNode.Add(textNode);
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
                                _ = sb.AppendFormat(CultureInfo.InvariantCulture, "[ {0} ]\n", string.Join(",", option.Encoding.Features.GetFeatureSpec(phone)));
                            }
                            optionTitle += sb.ToString();
                        }
                        if (!string.IsNullOrWhiteSpace(option.Entry?.Lemma))
                        {
                            optionTitle = string.Format(CultureInfo.InvariantCulture, "{0}: {1} {2}\n{3}\n\n", option.Entry?.Lexicon?.Id, option.Entry?.Lemma, option.Entry?.Encoded, option.Entry?.Definition)
                                + optionTitle;
                        }

                        string classes = "segment-option";
                        if (numOptions++ == 0)
                        {
                            classes += " option-first";
                        }

                        if ((option.Tags & TextTag.Hypo) != TextTag.None)
                        {
                            classes += " option-hypo";
                        }

                        if ((option.Tags & TextTag.Repr) != TextTag.None)
                        {
                            classes += " option-repr";
                        }

                        var optionNode = ParseElement(string.Format(CultureInfo.InvariantCulture, "<div id=\"entry{4}\" class=\"{5}\" title=\"{1}\"><div class=\"option-text encoding-{2}\">{0}</div><div class=\"option-definition\">{3}</div></div>",
                            Encode(option.Text), Encode(optionTitle), Encode(encoding), Encode(option.Entry?.Definition), entryId++, Encode(classes)));
                        optionsNode.Add(optionNode);
                    }
                    segmentNode.Add(optionsNode);
                }
                lineDiv.Add(segmentNode);
            }
        }

        return lineNode;
    }

    private XElement GenerateReportFooter()
    {
        var footerNode = ParseElement("<footer></footer>");
        var navNode = ParseElement("<div class=\"nav\"></div>");
        if (!string.IsNullOrWhiteSpace(PreviousPage))
        {
            navNode.Add(ParseElement(string.Format(CultureInfo.InvariantCulture, "<div class=\"prev\"><a href=\"{0}\">Previous</a></div>", Encode(PreviousPage))));
        }

        if (!string.IsNullOrWhiteSpace(NextPage))
        {
            navNode.Add(ParseElement(string.Format(CultureInfo.InvariantCulture, "<div class=\"prev\"><a href=\"{0}\">Next</a></div>", Encode(NextPage))));
        }

        footerNode.Add(navNode);
        return footerNode;
    }

    private static XElement ParseElement(string markup)
    {
        return XElement.Parse(markup.Replace("&nbsp;", "&#160;", StringComparison.Ordinal), LoadOptions.PreserveWhitespace);
    }

    private static string Encode(string? value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }
}
