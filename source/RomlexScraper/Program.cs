using Enochian.Lexicons;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace RomlexScraper;

internal sealed class Program
{
    private static readonly IList<RomlexLanguage> RomaniLanguages =
    [
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
    ];
    private const string EnglishAlphabet = "abcdefghijklmnopqrstuvwxyz";

    private static readonly Random Rng = new();
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static async Task<int> Main()
    {
        try
        {
            Dictionary<string, IList<RomlexEntry>> entriesByLemma = await LoadLexicon();

            Console.WriteLine("read {0} lemmas, {1} entries", entriesByLemma.Count, entriesByLemma.Sum(kv => kv.Value.Count));

            var deduped = new List<RomlexEntry>();
            foreach (var entry in entriesByLemma.Values.SelectMany(es => es))
            {
                if (!deduped.Any(d => d.Equals(entry)))
                {
                    deduped.Add(entry);
                }
            }
            var ordered = deduped.OrderBy(e => e.Lemma).ThenBy(e => e.SrcLangCode).ToList();

            var lexicon = new RomlexLexicon
            {
                Created = DateTime.UtcNow.ToString("u", CultureInfo.InvariantCulture),
                Languages = [new RomlexLanguage { Code = "en", Name = "English" }, .. RomaniLanguages],
                Entries = ordered,
            };

            using var stream = File.Create("romlex.json");
            JsonSerializer.Serialize(stream, lexicon, SerializerOptions);

            return 0;
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            return 1;
        }
    }

    private static async Task<Dictionary<string, IList<RomlexEntry>>> LoadLexicon()
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

                        var url = FormattableString.Invariant(
                            $@"http://romani.uni-graz.at/romlex/lex.cgi?st={letter}&rev=y&cl1={language.Code}&cl2=en&fi=&pm=in&ic=y&im=y&wc=");
                        Console.WriteLine("  {0}", url);

                        await Task.Delay(Rng.Next(100));
                        var response = await client.GetAsync(url);
                        _ = response.EnsureSuccessStatusCode();

                        var xdoc = XDocument.Load(await response.Content.ReadAsStreamAsync(), LoadOptions.None);
                        var res = xdoc.Descendants("res").FirstOrDefault()
                            ?? throw new InvalidDataException("response has no 'res' node");
                        if (res.Attribute("type")?.Value != "success")
                        {
                            throw new InvalidDataException(res.Value);
                        }

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
                                if (!entriesByLemma.TryGetValue(lemma, out var entries))
                                {
                                    entriesByLemma[lemma] = entries = [];
                                }

                                if (!entries.Any(entry => entry.Lemma == lemma && entry.Entry == str && entry.PartOfSpeech == pos && entry.Definition == def))
                                {
                                    entries.Add(new RomlexEntry
                                    {
                                        SrcLangCode = language.Code,
                                        DefLangCode = "en",

                                        Lemma = lemma,
                                        Entry = str,
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
