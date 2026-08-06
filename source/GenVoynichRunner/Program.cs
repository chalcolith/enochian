using System.Text.RegularExpressions;

namespace GenVoynichRunner;

internal sealed class Program
{
    private static readonly Regex TextLineRegex = new(@"^(<([^\.]+)\.[^>]+H>)\s+(\S+)", RegexOptions.Compiled);

    private sealed class PageRec
    {
        public required string Page { get; init; }
        public List<string> Locuses { get; } = [];
        public required string Url { get; init; }
        public string? PrevUrl { get; init; }
        public string? NextUrl { get; set; }
    }

    private static void Main(string[] args)
    {
        try
        {
            var pageRecs = new List<PageRec>();
            PageRec? curPageRec = null;

            foreach (var fname in args)
            {
                using var sr = new StreamReader(fname);
                string? line;
                while ((line = sr.ReadLine()) != null)
                {
                    var match = TextLineRegex.Match(line);
                    if (match.Success)
                    {
                        var locus = match.Groups[1].Value;
                        var page = match.Groups[2].Value;

                        if (curPageRec == null || page != curPageRec.Page)
                        {
                            curPageRec = new PageRec
                            {
                                Page = page,
                                Url = "voynich_" + page + ".html",
                                PrevUrl = curPageRec?.Url,
                            };
                            pageRecs.Add(curPageRec);
                        }
                        curPageRec.Locuses.Add(locus);
                    }
                }
            }

            PageRec lastPage = pageRecs.First();
            foreach (var pageRec in pageRecs.Skip(1))
            {
                lastPage.NextUrl = pageRec.Url;
                lastPage = pageRec;
            }

            foreach (var pageRec in pageRecs)
            {
                PrintNewCommand(pageRec.Url, pageRec.Locuses, pageRec.PrevUrl, pageRec.NextUrl);
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
        }
    }

    private static void PrintNewCommand(string url, IEnumerable<string> locuses, string? prevUrl, string? nextUrl)
    {
        string previousPage = prevUrl != null
            ? $@"|steps/Match Report/previousPage={prevUrl}"
            : "";

        string nextPage = nextUrl != null
            ? $@"|steps/Match Report/nextPage={nextUrl}"
            : "";

        Console.Out.WriteLine(FormattableString.Invariant(
            $@"dotnet ..\source\Enochian.Console\bin\Debug\net10.0\Enochian.Console.dll voynich.json --overrides ""steps/Voynich Interlinear/locuses={string.Join(",", locuses)}|steps/Match Report/output=../reports/{url}{previousPage}{nextPage}"""));
    }
}
