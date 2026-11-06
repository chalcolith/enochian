using HtmlAgilityPack;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Enochian.Provenance;

public sealed record RecoveryPin(string Path, string Sha256);

public sealed record RecoveryPlan(
    string SchemaVersion, RecoveryPin ParentProtocol, string PageCutoff,
    int UnitsPerBand, string[] Languages, string OutputDirectory,
    string ReportPath, string AmendmentPath);

public sealed record RecoveryPage(
    string Title, long Revision, string? Timestamp, RecoveryPin Api, RecoveryPin? Html);

public sealed record RecoveryPackage(
    string SchemaVersion, RecoveryPin Plan, string License, RecoveryPin RightsInfo,
    RecoveryPage[] Pages, RecoveryPage[] GeneratorSources);

public sealed record PronunciationOriginProof(
    string SchemaVersion, string EntryId, string ParentRecordSha256,
    string Language, string LexicalIdentity, string Convention, string OriginalIpa,
    long PageRevision, string? HtmlSha256, string Status, string Origin,
    string? Template, string? TransclusionId);

public static class PronunciationOriginProofBuilder
{
    private static readonly Dictionary<string, string> LanguageNames =
        new(StringComparer.Ordinal)
        {
            ["hin"] = "Hindi",
            ["san"] = "Sanskrit",
            ["tur"] = "Turkish",
            ["hun"] = "Hungarian",
        };

    private static readonly Dictionary<string, string> DictionaryCodes =
        new(StringComparer.Ordinal)
        {
            ["hin"] = "hi",
            ["san"] = "sa",
            ["tur"] = "tr",
            ["hun"] = "hu",
        };

    private static readonly Dictionary<string, string> Generators =
        new(StringComparer.Ordinal)
        {
            ["hin"] = "hi-IPA",
            ["san"] = "sa-IPA",
            ["tur"] = "tr-IPA",
            ["hun"] = "hu-IPA",
        };

    private static readonly Dictionary<string, string> PartsOfSpeech =
        new(StringComparer.Ordinal)
        {
            ["Noun"] = "noun",
            ["Verb"] = "verb",
            ["Adjective"] = "adj",
            ["Adverb"] = "adv",
            ["Pronoun"] = "pron",
            ["Determiner"] = "det",
            ["Numeral"] = "num",
            ["Preposition"] = "prep",
            ["Postposition"] = "postp",
            ["Conjunction"] = "conj",
            ["Particle"] = "particle",
            ["Interjection"] = "intj",
        };

    public static PronunciationOriginProof Bind(PronunciationEvidence row, RecoveryPage page, string? html)
    {
        if (page.Title != row.Lemma ||
            !Uri.TryCreate(row.SourceEntry, UriKind.Absolute, out var entryUri) ||
            entryUri.Host != "en.wiktionary.org" ||
            Uri.UnescapeDataString(entryUri.AbsolutePath) != "/wiki/" + row.Lemma)
        {
            throw new InvalidDataException("Proof page does not match the exact source entry.");
        }

        var proof = new PronunciationOriginProof("1.0.0", row.EntryId,
            PronunciationPolicyEvaluator.StableId(JsonSerializer.Serialize(row, PronunciationAudit.JsonOptions)),
            row.Language, row.LexicalIdentity, row.Convention, row.OriginalIpa,
            page.Revision, page.Html?.Sha256, "no_matching_scoped_reading", "unknown", null, null);
        if (page.Revision == 0)
        {
            return proof with { Status = "page_missing_at_cutoff" };
        }

        if (html == null || page.Html == null || page.Title != row.Lemma ||
            !LanguageNames.TryGetValue(row.Language, out var languageName))
        {
            throw new InvalidDataException("Missing HTML, wrong page title, or unsupported proof language.");
        }

        var document = new HtmlDocument();
        document.LoadHtml(html);
        var root = document.DocumentNode.SelectSingleNode("//html");
        var title = document.DocumentNode.SelectSingleNode("//head/title");
        var modified = document.DocumentNode.SelectSingleNode("//head/meta[@property='dc:modified']");
        if (root?.GetAttributeValue("about", "") !=
                "//en.wiktionary.org/wiki/Special:Redirect/revision/" + page.Revision.ToString(CultureInfo.InvariantCulture) ||
            Text(title) != page.Title || modified?.GetAttributeValue("content", "") != page.Timestamp)
        {
            throw new InvalidDataException("Revision HTML title, timestamp, or revision mismatch.");
        }

        var identity = row.LexicalIdentity.Split('\u001f');
        if (identity.Length != 3 || identity[0] != row.Lemma || identity[1] != row.PartOfSpeech ||
            !int.TryParse(identity[2], NumberStyles.None, CultureInfo.InvariantCulture, out var targetEtymology))
        {
            throw new InvalidDataException("Invalid source lexical identity.");
        }

        var templates = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var node in document.DocumentNode.Descendants().Where(node => node.Attributes["data-mw"] != null))
        {
            var about = node.GetAttributeValue("about", "");
            if (about.Length == 0)
            {
                continue;
            }

            using var data = JsonDocument.Parse(HtmlEntity.DeEntitize(node.GetAttributeValue("data-mw", "")));
            if (!data.RootElement.TryGetProperty("parts", out var parts) || parts.GetArrayLength() != 1 ||
                !parts[0].TryGetProperty("template", out var template))
            {
                continue;
            }

            if (!templates.TryAdd(about, template.Clone()))
            {
                throw new InvalidDataException("Duplicate transclusion metadata.");
            }
        }

        var language = "";
        var etymology = 0;
        string? pos = null;
        var pronunciationLevel = 0;
        var positions = new HashSet<(int Etymology, string Pos)>();
        var matches = new List<(int Etymology, string? Pos, string About, JsonElement Template)>();
        foreach (var node in document.DocumentNode.Descendants())
        {
            if (node.Name.Length == 2 && node.Name[0] == 'h' && node.Name[1] is >= '2' and <= '6')
            {
                var level = node.Name[1] - '0';
                var heading = Text(node);
                if (level == 2)
                {
                    language = heading;
                    etymology = 0;
                    pos = null;
                    pronunciationLevel = 0;
                }
                else if (language == languageName)
                {
                    if (pronunciationLevel != 0 && level <= pronunciationLevel)
                    {
                        pronunciationLevel = 0;
                    }

                    if (level == 3)
                    {
                        pos = null;
                        if (heading == "Etymology")
                        {
                            etymology = 0;
                        }
                        else if (heading.StartsWith("Etymology ", StringComparison.Ordinal))
                        {
                            etymology = int.Parse(heading.AsSpan(10), NumberStyles.None, CultureInfo.InvariantCulture);
                        }
                    }

                    if (PartsOfSpeech.TryGetValue(heading, out var part))
                    {
                        pos = part;
                        _ = positions.Add((etymology, part));
                    }

                    if (heading is "Pronunciation" or "Pronunciation 1" or "Pronunciation 2")
                    {
                        pronunciationLevel = level;
                    }
                }
            }

            if (language != languageName || pronunciationLevel == 0 ||
                !node.GetAttributeValue("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("IPA") ||
                Text(node).Normalize(NormalizationForm.FormC) != row.OriginalIpa.Normalize(NormalizationForm.FormC))
            {
                continue;
            }

            var aboutNode = node.AncestorsAndSelf().FirstOrDefault(ancestor => ancestor.Attributes["about"] != null);
            var aboutId = aboutNode?.GetAttributeValue("about", "");
            if (aboutId != null && templates.TryGetValue(aboutId, out var templateData))
            {
                matches.Add((etymology, pos, aboutId, templateData));
            }
        }

        var scoped = matches.Where(match => match.Etymology == targetEtymology &&
            (match.Pos == null || match.Pos == row.PartOfSpeech) &&
            positions.Contains((targetEtymology, row.PartOfSpeech)))
            .DistinctBy(match => match.About, StringComparer.Ordinal).ToArray();
        if (scoped.Length != 1)
        {
            return proof with { Status = scoped.Length > 1 ? "ambiguous_transclusion" : proof.Status };
        }

        var (_, _, selectedAbout, selectedTemplate) = scoped[0];
        var name = selectedTemplate.GetProperty("target").GetProperty("wt").GetString()!.Trim()
            .Replace('_', ' ');
        if (name.StartsWith("Template:", StringComparison.Ordinal))
        {
            name = name[9..];
        }

        var parameters = selectedTemplate.GetProperty("params");
        if (name == "IPA" && IsLiteral(parameters, DictionaryCodes[row.Language], row.OriginalIpa))
        {
            return proof with
            {
                Status = "literal_dictionary_ipa",
                Origin = "dictionary-supplied",
                Template = name,
                TransclusionId = selectedAbout,
            };
        }

        if (name == Generators[row.Language] && !HasLiteralArgument(parameters, row.OriginalIpa))
        {
            return proof with
            {
                Status = "generated_revision_and_dependencies_unresolved",
                Origin = "template-generated",
                Template = name,
                TransclusionId = selectedAbout,
            };
        }

        return proof with { Status = "template_semantics_unresolved", Template = name, TransclusionId = selectedAbout };
    }

    public static PronunciationEvidence Apply(PronunciationEvidence row, PronunciationOriginProof proof)
    {
        if (proof.ParentRecordSha256 !=
            PronunciationPolicyEvaluator.StableId(JsonSerializer.Serialize(row, PronunciationAudit.JsonOptions)) ||
            proof.EntryId != row.EntryId)
        {
            throw new InvalidDataException("Proof is not bound to its exact parent reading.");
        }

        return row with
        {
            Origin = proof.Origin,
            Generator = proof.Origin == "template-generated" ? "wiktionary:" + proof.Template : null,
            GeneratorRevision = null,
            Dependencies = proof.Origin == "template-generated"
                ? [.. row.Dependencies.Append("unknown-generator-dependencies").Distinct(StringComparer.Ordinal)] : row.Dependencies,
            EvidenceStatus = proof.Origin == "dictionary-supplied" ? "supported" : "exploratory",
        };
    }

    private static bool IsLiteral(JsonElement parameters, string code, string ipa)
    {
        var legacy = parameters.TryGetProperty("lang", out var language);
        var languageKey = legacy ? language : parameters.TryGetProperty("1", out var first) ? first : default;
        if (languageKey.ValueKind != JsonValueKind.Object || languageKey.GetProperty("wt").GetString()?.Trim() != code)
        {
            return false;
        }

        return parameters.EnumerateObject().Any(parameter =>
            int.TryParse(parameter.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var number) &&
            number >= (legacy ? 1 : 2) && LiteralValue(parameter.Value, ipa));
    }

    private static bool HasLiteralArgument(JsonElement parameters, string ipa) =>
        parameters.EnumerateObject().Any(parameter => LiteralValue(parameter.Value, ipa));

    private static bool LiteralValue(JsonElement parameter, string ipa)
    {
        var value = parameter.GetProperty("wt").GetString()!.Trim();
        return value.Length >= 2 && ((value[0] == '/' && value[^1] == '/') || (value[0] == '[' && value[^1] == ']')) &&
            !value.Contains("{{", StringComparison.Ordinal) && !value.Contains("[[", StringComparison.Ordinal) &&
            !value.Contains('<') && value.Normalize(NormalizationForm.FormC) == ipa.Normalize(NormalizationForm.FormC);
    }

    private static string Text(HtmlNode? node) => HtmlEntity.DeEntitize(node?.InnerText ?? "").Trim();
}
