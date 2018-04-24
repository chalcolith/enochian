using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GenVoynichRunner
{
    class Program
    {
        static readonly Regex TextLineRegex = new Regex(@"^(<([^\.]+)\.[^>]+H>)\s+(\S+)", RegexOptions.Compiled);

        class PageRec
        {
            public string Page;
            public IList<string> Locuses;
            public string Url;
            public string PrevUrl;
            public string NextUrl;
        }

        static void Main(string[] args)
        {
            try
            {
                var pageRecs = new List<PageRec>();
                PageRec curPageRec = null;

                foreach (var fname in args)
                {
                    using (var sr = new StreamReader(fname))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            var match = TextLineRegex.Match(line);
                            if (match.Success)
                            {
                                var locus = match.Groups[1].Value;
                                var page = match.Groups[2].Value;

                                if (curPageRec == null || page != curPageRec.Page)
                                {
                                    var newPageRec = new PageRec
                                    {
                                        Page = page,
                                        Locuses = new List<string>(),
                                        Url = "voynich_" + page + ".html",
                                        PrevUrl = curPageRec?.Url,
                                    };
                                    pageRecs.Add(newPageRec);

                                    curPageRec = newPageRec;
                                }
                                curPageRec.Locuses.Add(locus);
                            }
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

        static void PrintNewCommand(string url, IList<string> locuses, string prevUrl, string nextUrl)
        {
            string previousPage = prevUrl != null
                ? string.Format(@"|steps/Match Report/previousPage={0}", prevUrl)
                : "";

            string nextPage = nextUrl != null
                ? string.Format(@"|steps/Match Report/nextPage={0}", nextUrl)
                : "";

            Console.Out.WriteLine(@"dotnet ..\source\Enochian.Console\bin\Debug\netcoreapp2.0\Enochian.Console.dll voynich.json --overrides ""steps/Voynich Interlinear/locuses={0}|steps/Match Report/output=../reports/{1}{2}{3}""",
                string.Join(",", locuses), url, previousPage, nextPage);
        }
    }
}
