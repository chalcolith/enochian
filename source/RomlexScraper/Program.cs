using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using Enochian.Lexicons;
using Newtonsoft.Json;

namespace RomlexScraper
{
    class Program
    {
        const string RomlexBaseUrl = @"http://romani.uni-graz.at/romlex/lex.cgi";

        static readonly IList<RomlexLanguage> RomaniLanguages = new List<RomlexLanguage>
        {
            new RomlexLanguage { Code = "rmyb", Name = "Banatiski Gurbet Romani" },
            new RomlexLanguage { Code = "rmnb", Name = "Bugurdži Romani" },
            new RomlexLanguage { Code = "rmcb", Name = "Burgenland Romani" },
            new RomlexLanguage { Code = "rmnc", Name = "Crimean Romani" },
            new RomlexLanguage { Code = "rmcd", Name = "Dolenjski Romani" },
            new RomlexLanguage { Code = "rmce", Name = "East Slovak Romani" },
            new RomlexLanguage { Code = "rmff", Name = "Finnish Romani" },
            new RomlexLanguage { Code = "rmyg", Name = "Gurbet Romani" },
            new RomlexLanguage { Code = "rmyh", Name = "Gurvari Romani" },
            new RomlexLanguage { Code = "rmcv", Name = "Hungarian Vend Romani" },
            new RomlexLanguage { Code = "rmyk", Name = "Kalderaš Romani" },
            new RomlexLanguage { Code = "rmnk", Name = "Kosovo Arli Romani" },
            new RomlexLanguage { Code = "roml", Name = "Latvian Romani" },
            new RomlexLanguage { Code = "romt", Name = "Lithuanian Romani" },
            new RomlexLanguage { Code = "rmyl", Name = "Lovara Romani" },
            new RomlexLanguage { Code = "rmna", Name = "Macedonian Arli Romani" },
            new RomlexLanguage { Code = "rmyd", Name = "Macedonian Džambazi Romani" },
            new RomlexLanguage { Code = "romr", Name = "North Russian Romani" },
            new RomlexLanguage { Code = "rmcp", Name = "Prekmurski Romani" },
            new RomlexLanguage { Code = "rmcr", Name = "Romungro Romani" },
            new RomlexLanguage { Code = "rmns", Name = "Sepečides Romani" },
            new RomlexLanguage { Code = "rmoo", Name = "Sinte Romani" },
            new RomlexLanguage { Code = "rmne", Name = "Sofia Erli Romani" },
            new RomlexLanguage { Code = "rmys", Name = "Sremski Gurbet Romani" },
            new RomlexLanguage { Code = "rmnu", Name = "Ursari Romani" },
            new RomlexLanguage { Code = "rmcs", Name = "Veršend Romani" },
            new RomlexLanguage { Code = "rmww", Name = "Welsh Romani" },
        };
        const string EnglishAlphabet = "abcdefghijklmnopqrstuvwxyz";

        static readonly Random Rng = new Random();

        public static int Main(string[] args)
        {
            try
            {
                Dictionary<string, IList<RomlexEntry>> entriesByLemma = LoadLexicon().Result;

                Console.WriteLine("read {0} lemmas, {1} entries", entriesByLemma.Count, entriesByLemma.Sum(kv => kv.Value.Count));

                var deduped = new List<RomlexEntry>();
                foreach (var entry in entriesByLemma.Values.SelectMany(es => es))
                {
                    if (!deduped.Any(d => d.Equals(entry)))
                        deduped.Add(entry);
                }
                var ordered = deduped.OrderBy(e => e.Lemma).ThenBy(e => e.SrcLangCode).ToList();

                var lexicon = new RomlexLexicon
                {
                    Created = DateTime.UtcNow.ToString("u"),
                    Languages = new[] { new RomlexLanguage { Code = "en", Name = "English" } }
                        .Concat(RomaniLanguages)
                        .ToList(),
                    Entries = ordered,
                };

                var js = new JsonSerializer()
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore,
                };

                using (var sw = new StreamWriter("romlex.json"))
                using (var jw = new JsonTextWriter(sw))
                {
                    js.Serialize(jw, lexicon);
                }

                return 0;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                return 1;
            }
        }

        static async Task<Dictionary<string, IList<RomlexEntry>>> LoadLexicon()
        {
            var entriesByLemma = new Dictionary<string, IList<RomlexEntry>>();

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/57.0.2987.133 Safari/537.36");

                foreach (var language in RomaniLanguages)
                {
                    foreach (var letter in EnglishAlphabet)
                    {
                        try
                        {
                            Console.WriteLine("loading {0} ({1}): {2}", language.Code, language.Name, letter);

                            var url = string.Format(@"http://romani.uni-graz.at/romlex/lex.cgi?st={0}&rev=y&cl1={1}&cl2={2}&fi=&pm=in&ic=y&im=y&wc=",
                                letter, language.Code, "en");
                            Console.WriteLine("  {0}", url);

                            await Task.Delay(Rng.Next(100));
                            var response = await client.GetAsync(url);
                            response.EnsureSuccessStatusCode();

                            var xdoc = XDocument.Load(await response.Content.ReadAsStreamAsync(), LoadOptions.None);
                            var res = xdoc.Descendants("res").FirstOrDefault();
                            if (res == null) throw new Exception("response has no 'res' node");
                            if (res.Attribute("type")?.Value != "success") throw new Exception(res.Value);

                            foreach (var node in xdoc.Descendants("entry"))
                            {
                                var str = node.Descendants("o").FirstOrDefault()?.Value;
                                var pos = node.Descendants("pos").FirstOrDefault()?.Value;
                                var def = string.Join("; ", 
                                    node.Descendants("g")
                                        .SelectMany(g => g.Descendants("s")
                                            .SelectMany(s => s.Descendants("t")
                                                .SelectMany(t => t.Descendants("e")
                                                    .Select(e => e.Value)))));

                                if (!string.IsNullOrWhiteSpace(str))
                                {
                                    var lemma = str.ToLowerInvariant();
                                    if (!entriesByLemma.TryGetValue(lemma, out IList<RomlexEntry> entries))
                                    {
                                        entriesByLemma[lemma] = (entries = new List<RomlexEntry>());
                                    }

                                    if (!entries.Any(entry => entry.Lemma == lemma && entry.Entry == str && entry.PartOfSpeech == pos && entry.Definition == def))
                                    {
                                        entries.Add(new RomlexEntry
                                        {
                                            SrcLangCode = language.Code,
                                            DefLangCode = "en",

                                            Lemma = lemma,
                                            Entry = str != lemma ? str : null,
                                            PartOfSpeech = pos,
                                            Definition = def,
                                        });
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Console.Error.WriteLine(e);
                        }
                    }
                }
            }

            return entriesByLemma;
        }
    }
}
