using Enochian.Provenance;
using Json.Schema;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Enochian.UnitTests;

[TestClass]
public sealed class PronunciationRecoveryTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    private static readonly string[] FixtureLanguages = ["hin", "san", "tur", "hun"];
    private static readonly string[] FixtureHeadings = ["Hindi", "Sanskrit", "Turkish", "Hungarian"];
    private static readonly string[] FixtureCodes = ["hi", "sa", "tr", "hu"];
    private static readonly string[] FixtureGeneratorTitles =
        ["Template:hi-IPA", "Module:hi-IPA", "Template:sa-IPA", "Module:sa-pronunc",
            "Template:tr-IPA", "Module:tr-IPA", "Template:hu-IPA", "Module:hu-pron"];

    [TestMethod]
    [DataRow("hin", "Hindi", "hi")]
    [DataRow("san", "Sanskrit", "sa")]
    [DataRow("tur", "Turkish", "tr")]
    [DataRow("hun", "Hungarian", "hu")]
    public void LiteralFormattingTemplateBindsEachReading(string language, string heading, string code)
    {
        var row = Row(language);
        var proof = Bind(row, Html(heading, "IPA", new Dictionary<string, string> { ["1"] = code, ["2"] = "/pat/" }));
        Assert.AreEqual("dictionary-supplied", proof.Origin);
        Assert.AreEqual("literal_dictionary_ipa", proof.Status);
        Assert.AreEqual("#mwt1", proof.TransclusionId);
        var enriched = PronunciationOriginProofBuilder.Apply(row, proof);
        Assert.AreEqual(row.AlternativeWeight, enriched.AlternativeWeight);
        Assert.AreEqual(row.Source, enriched.Source);
        Assert.AreEqual(row.SourceEntry, enriched.SourceEntry);
        Assert.AreEqual(row.Snapshot, enriched.Snapshot);
        Assert.AreEqual(row.NormalizedIpa, enriched.NormalizedIpa);
        Assert.IsNull(enriched.Generator);
        Assert.IsNull(enriched.GeneratorRevision);
        Assert.IsFalse(JsonSerializer.Serialize(proof, PronunciationAudit.JsonOptions).Contains("semantic-sentinel", StringComparison.Ordinal));
        ValidateSchema(proof);
    }

    [TestMethod]
    public void LegacyLanguageParameterIsLiteral()
    {
        var proof = Bind(Row(), Html("Hindi", "IPA", new Dictionary<string, string> { ["lang"] = "hi", ["1"] = "/pat/" }));
        Assert.AreEqual("dictionary-supplied", proof.Origin);
    }

    [TestMethod]
    [DataRow("hin", "Hindi", "hi-IPA")]
    [DataRow("san", "Sanskrit", "sa-IPA")]
    [DataRow("tur", "Turkish", "tr-IPA")]
    [DataRow("hun", "Hungarian", "hu-IPA")]
    public void RenderedGeneratedReadingIsBoundButCannotEstablishGeneratorClosure(string language, string heading, string template)
    {
        var row = Row(language);
        var proof = Bind(row, Html(heading, template, []));
        Assert.AreEqual("template-generated", proof.Origin);
        var enriched = PronunciationOriginProofBuilder.Apply(row, proof);
        Assert.AreEqual("wiktionary:" + template, enriched.Generator);
        Assert.IsNull(enriched.GeneratorRevision);
        CollectionAssert.Contains(enriched.Dependencies, "unknown-generator-dependencies");
        Assert.AreEqual("exploratory", enriched.EvidenceStatus);
    }

    [TestMethod]
    [DataRow("hi-IPA")]
    [DataRow("tr-IPA")]
    [DataRow("unrecognized-IPA")]
    public void ApparentLiteralInSpecializedTemplateNeedsVerifiedSemantics(string template)
    {
        var proof = Bind(Row(), Html("Hindi", template, new Dictionary<string, string> { ["1"] = "/pat/" }));
        Assert.AreEqual("unknown", proof.Origin);
        Assert.AreEqual("template_semantics_unresolved", proof.Status);
    }

    [TestMethod]
    [DataRow("Sanskrit", "sa", "Noun", 0)]
    [DataRow("Hindi", "sa", "Noun", 0)]
    [DataRow("Hindi", "hi", "Verb", 0)]
    [DataRow("Hindi", "hi", "Noun", 2)]
    public void WrongLanguageCodePartOfSpeechOrEtymologyCannotProveReading(string heading, string code, string pos, int etymology)
    {
        var proof = Bind(Row(), Html(heading, "IPA", new Dictionary<string, string> { ["1"] = code, ["2"] = "/pat/" }, pos, etymology));
        Assert.AreEqual("unknown", proof.Origin);
    }

    [TestMethod]
    public void NumberedEtymologyAndPartOfSpeechAreBound()
    {
        var row = Row() with { LexicalIdentity = "test\u001fnoun\u001f2" };
        var proof = Bind(row, Html("Hindi", "IPA", new Dictionary<string, string> { ["1"] = "hi", ["2"] = "/pat/" }, etymology: 2));
        Assert.AreEqual("dictionary-supplied", proof.Origin);
    }

    [TestMethod]
    [DataRow("/pˈat/")]
    [DataRow("[pat]")]
    [DataRow("/pa.t/")]
    [DataRow("{{IPA-value}}")]
    [DataRow("[[pat]]")]
    [DataRow("/pat/<!-- literal -->")]
    public void NormalizedAgreementOrDynamicArgumentsDoNotProveOriginalAlternative(string argument)
    {
        var proof = Bind(Row(), Html("Hindi", "IPA", new Dictionary<string, string> { ["1"] = "hi", ["2"] = argument }));
        Assert.AreEqual("unknown", proof.Origin);
    }

    [TestMethod]
    public void TwoTransclusionsWithSameReadingAreAmbiguous()
    {
        var html = Html("Hindi", "IPA", new Dictionary<string, string> { ["1"] = "hi", ["2"] = "/pat/" });
        var start = html.IndexOf("<span typeof", StringComparison.Ordinal);
        var end = html.IndexOf("<h3>Noun", StringComparison.Ordinal);
        var duplicate = html[start..end].Replace("#mwt1", "#mwt2", StringComparison.Ordinal);
        var proof = Bind(Row(), html.Insert(end, duplicate));
        Assert.AreEqual("ambiguous_transclusion", proof.Status);
        Assert.AreEqual("unknown", proof.Origin);
    }

    [TestMethod]
    public void CommentsNowikiAndUnattributedIpaCannotBind()
    {
        var html = Html("Hindi", "IPA", new Dictionary<string, string> { ["1"] = "hi", ["2"] = "/pat/" });
        var start = html.IndexOf("<span typeof", StringComparison.Ordinal);
        var end = html.IndexOf("<h3>Noun", StringComparison.Ordinal);
        var hidden = html[start..end];
        var result = html.Remove(start, end - start).Insert(start,
            "<!--" + hidden + "--><pre>&lt;span class='IPA'&gt;/pat/&lt;/span&gt;</pre><span class='IPA'>/pat/</span>");
        Assert.AreEqual("unknown", Bind(Row(), result).Origin);
    }

    [TestMethod]
    public void IpaOutsidePronunciationOrUnderOtherLanguageDoesNotLeakIntoProof()
    {
        var html = Html("Hindi", "IPA", new Dictionary<string, string> { ["1"] = "hi", ["2"] = "/pat/" });
        Assert.AreEqual("unknown", Bind(Row(), html.Replace("<h3>Pronunciation</h3>", "<h3>Etymology</h3>", StringComparison.Ordinal)).Origin);
        Assert.AreEqual("unknown", Bind(Row(), html.Replace("<h2>Hindi</h2>", "<h2>Sanskrit</h2>", StringComparison.Ordinal)).Origin);
    }

    [TestMethod]
    public void WrongRevisionTimestampTitleOrSourceEntryIsRejected()
    {
        var html = Html("Hindi", "IPA", new Dictionary<string, string> { ["1"] = "hi", ["2"] = "/pat/" });
        _ = Assert.ThrowsExactly<InvalidDataException>(() => Bind(Row(), html.Replace("revision/42", "revision/43", StringComparison.Ordinal)));
        _ = Assert.ThrowsExactly<InvalidDataException>(() => Bind(Row(), html.Replace("<title>test", "<title>other", StringComparison.Ordinal)));
        _ = Assert.ThrowsExactly<InvalidDataException>(() => Bind(Row(), html.Replace("2026-09-01", "2026-10-02", StringComparison.Ordinal)));
        _ = Assert.ThrowsExactly<InvalidDataException>(() => Bind(Row() with { SourceEntry = "https://en.wiktionary.org/wiki/other" }, html));
    }

    [TestMethod]
    public void ChangingConventionOrWeightsInvalidatesProofApplication()
    {
        var row = Row();
        var proof = Bind(row, Html("Hindi", "IPA", new Dictionary<string, string> { ["1"] = "hi", ["2"] = "/pat/" }));
        _ = Assert.ThrowsExactly<InvalidDataException>(() => PronunciationOriginProofBuilder.Apply(row with { Convention = "other" }, proof));
        _ = Assert.ThrowsExactly<InvalidDataException>(() => PronunciationOriginProofBuilder.Apply(row with { AlternativeWeight = 1 }, proof));
    }

    [TestMethod]
    public void DeterministicSelectionPreservesWholeAlternativeDistributions()
    {
        var rows = Enumerable.Range(0, 12).SelectMany(index => new[]
        {
            Row() with { EntryId = "a" + index, LexicalIdentity = "word" + index + "\u001fnoun\u001f0", LengthBand = index < 4 ? "03-05" : index < 8 ? "06-09" : "10-20" },
            Row() with { EntryId = "b" + index, LexicalIdentity = "word" + index + "\u001fnoun\u001f0", OriginalIpa = "[pat]", LengthBand = index < 4 ? "03-05" : index < 8 ? "06-09" : "10-20" },
        }).ToArray();
        var selected = PronunciationRecovery.Select(rows, 2);
        CollectionAssert.AreEqual(selected, PronunciationRecovery.Select(rows.Reverse(), 2));
        Assert.HasCount(12, selected);
        Assert.IsTrue(selected.GroupBy(row => row.LexicalIdentity).All(group =>
            group.Count() == 2 && group.Sum(row => row.AlternativeWeight) == 1));
    }

    [TestMethod]
    public void MissingPageRemainsExplicitlyUnknownAndPlanHasNoThresholdRelaxation()
    {
        var row = Row();
        var page = Page() with { Revision = 0, Timestamp = null, Html = null };
        Assert.AreEqual("page_missing_at_cutoff", PronunciationOriginProofBuilder.Bind(row, page, null).Status);
        using var plan = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot,
            "resources", "lexicons", "pronunciation-provenance.v1.json")));
        ValidateSchema(plan.RootElement);
        Assert.AreEqual(50, plan.RootElement.GetProperty("units_per_band").GetInt32());
    }

    [TestMethod]
    public void BalancedAnchorPlanFreezesLargerSampleAndEvidenceParents()
    {
        using var plan = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot,
            "resources", "lexicons", "pronunciation-provenance.v2.json")));
        ValidateSchema(plan.RootElement);
        Assert.AreEqual("2.0.0", plan.RootElement.GetProperty("schema_version").GetString());
        Assert.AreEqual(75, plan.RootElement.GetProperty("units_per_band").GetInt32());
        Assert.AreEqual(".enoch/pronunciation-provenance/v1/snapshot.json",
            plan.RootElement.GetProperty("parent_snapshot").GetProperty("path").GetString());
        Assert.AreEqual(".enoch/pronunciation-references/v1/audit.json",
            plan.RootElement.GetProperty("reference_audit").GetProperty("path").GetString());
    }

    [TestMethod]
    public void WeightedDistributionKeyPreservesWeightsAndIgnoresReadingOrder()
    {
        double[] phoneA = [1, 0];
        double[] phoneB = [0, 1];
        (IReadOnlyList<double[]> Phones, double Weight)[] readings =
        [
            ([phoneA], 0.25),
            ([phoneB], 0.5),
            ([phoneA], 0.25),
        ];
        var key = PronunciationPolicyEvaluator.WeightedDistributionKey(readings);
        Assert.AreEqual(key, PronunciationPolicyEvaluator.WeightedDistributionKey(readings.Reverse()));
        Assert.AreNotEqual(key, PronunciationPolicyEvaluator.WeightedDistributionKey(
            readings.Select(reading => (reading.Phones, reading.Weight + 0.1))));
    }

    [TestMethod]
    public async Task OfflineRecoveryReplaysAndTamperedOriginsCannotPassEvenWithUpdatedBytePins()
    {
        var root = Path.Combine(Path.GetTempPath(), "enochian-recovery-" + Guid.NewGuid().ToString("N"));
        try
        {
            var planPath = CreateOfflineFixture(root);
            var recovery = new PronunciationRecovery(root);
            await recovery.RunAsync(planPath, acquire: false);
            var coverage = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "reports",
                "pronunciation-provenance", "v1", "coverage.json")))!;
            Assert.IsTrue(coverage["sources"]!.AsArray().All(source => source!["supported_units_after"]!.GetValue<int>() == 1));
            var output = Path.Combine(root, ".enoch", "pronunciation-provenance", "v1");
            var before = Directory.GetFiles(output, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, PronunciationAudit.Hash, StringComparer.Ordinal);
            File.WriteAllText(planPath, File.ReadAllText(planPath).ReplaceLineEndings("\r\n"), new UTF8Encoding(false));
            await recovery.RunAsync(planPath, acquire: false);
            Assert.IsTrue(before.All(pair => PronunciationAudit.Hash(pair.Key) == pair.Value));
            var manifestPath = Path.Combine(output, "profiles", "hin-dictionary-v1.0.0.manifest.json");
            Assert.AreEqual(PronunciationAudit.Hash(manifestPath), PronunciationAudit.HashConfiguration(manifestPath));
            var amendment = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "experiments", "confirmatory.provenance.v1.json")))!;
            Assert.AreEqual(PronunciationAudit.HashConfiguration(manifestPath), amendment["sources"]!.AsArray()
                .Single(source => source!["language"]!.GetValue<string>() == "hin")!["profile_manifest"]!["sha256"]!.GetValue<string>());
            var envelope = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
            var tracked = recovery.VerifyProfile(JsonSerializer.SerializeToElement(envelope));
            Assert.IsTrue(tracked.Any(pin => pin.Path.EndsWith(".html", StringComparison.Ordinal)));
            Assert.IsTrue(tracked.Any(pin => pin.Path.EndsWith("parent.json", StringComparison.Ordinal)));
            Assert.IsTrue(tracked.Any(pin => pin.Path.EndsWith(".joins.jsonl", StringComparison.Ordinal)));
            var rooted = envelope.DeepClone().AsObject();
            rooted["provenance"]!["package"]!["path"] = Path.Combine(root, rooted["provenance"]!["package"]!["path"]!
                .GetValue<string>().Replace('/', Path.DirectorySeparatorChar));
            _ = Assert.ThrowsExactly<InvalidDataException>(() => recovery.VerifyProfile(JsonSerializer.SerializeToElement(rooted)));
            var evidence = Path.Combine(output, "profiles", "hin-dictionary-v1.0.0.evidence.jsonl");
            File.WriteAllText(evidence, File.ReadAllText(evidence).Replace(
                "\"origin\":\"dictionary-supplied\"", "\"origin\":\"unknown\"", StringComparison.Ordinal), new UTF8Encoding(false));
            var artifact = envelope["artifacts"]!.AsArray().Single(pin =>
                pin!["path"]!.GetValue<string>().EndsWith(".evidence.jsonl", StringComparison.Ordinal))!;
            artifact["sha256"] = PronunciationAudit.Hash(evidence);
            _ = Assert.ThrowsExactly<InvalidDataException>(() => recovery.VerifyProfile(JsonSerializer.SerializeToElement(envelope)));
            var htmlPin = tracked.First(pin => pin.Path.EndsWith(".html", StringComparison.Ordinal));
            File.AppendAllText(Path.Combine(root, htmlPin.Path.Replace('/', Path.DirectorySeparatorChar)), "tamper");
            _ = Assert.ThrowsExactly<InvalidDataException>(() => recovery.VerifyProfile(JsonSerializer.SerializeToElement(envelope)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    private static string CreateOfflineFixture(string root)
    {
        var schemas = Path.Combine(root, "resources", "lexicons", "schemas");
        _ = Directory.CreateDirectory(schemas);
        File.Copy(Path.Combine(RepositoryRoot, "resources", "lexicons", "schemas", "pronunciation-recovery.schema.json"),
            Path.Combine(schemas, "pronunciation-recovery.schema.json"));
        var sources = new JsonArray();
        var pages = new List<RecoveryPage>();
        for (var index = 0; index < FixtureLanguages.Length; index++)
        {
            var language = FixtureLanguages[index];
            var stem = language + "-dictionary-v1.0.0";
            var row = Row(language) with { EntryId = "entry-" + language };
            var directory = Path.Combine(root, "parent-inputs");
            _ = Directory.CreateDirectory(directory);
            var evidence = Path.Combine(directory, stem + ".evidence.jsonl");
            var excluded = row with { EntryId = "excluded-" + language, Exclusions = ["unsupported_ipa"] };
            File.WriteAllText(evidence, JsonSerializer.Serialize(row, PronunciationAudit.JsonOptions) + "\n" +
                JsonSerializer.Serialize(excluded, PronunciationAudit.JsonOptions) + "\n", new UTF8Encoding(false));
            var entryPath = Path.Combine(directory, stem + ".jsonl");
            var joinsPath = Path.Combine(directory, stem + ".joins.jsonl");
            WriteFixture(joinsPath, new { status = "fixture" });
            var entry = new PronunciationNormalizedEntry
            {
                EntryId = row.EntryId,
                SourceRecordId = row.EntryId,
                Language = language,
                Family = "fixture",
                Source = row.Source,
                SourceVersion = row.Snapshot,
                Lemma = row.Lemma,
                OriginalForm = row.Lemma,
                Form = row.Lemma,
                PartOfSpeech = row.PartOfSpeech,
                Dialect = stem,
                Ipa = row.NormalizedIpa,
                License = "CC-BY-SA-4.0",
            };
            File.WriteAllText(entryPath, JsonSerializer.Serialize(entry, PronunciationAudit.JsonOptions) + "\n", new UTF8Encoding(false));
            var manifestPath = Path.Combine(directory, stem + ".manifest.json");
            WriteFixture(manifestPath, new
            {
                schema_version = "1.0.0",
                profile = new { id = language + "-dictionary", version = "1.0.0", language },
                profile_sha256 = new string('a', 64),
                panel_sha256 = new string('b', 64),
                feature_sha256 = new string('c', 64),
                encoding_sha256 = new string('d', 64),
                software_sha256 = new string('e', 64),
                source_manifests = Array.Empty<RecoveryPin>(),
                artifacts = new[] { FixturePin(root, entryPath), FixturePin(root, evidence), FixturePin(root, joinsPath) },
                status = "exploratory",
                blockers = Array.Empty<string>(),
            });
            sources.Add(JsonSerializer.SerializeToNode(new
            {
                language,
                profile_manifest = FixturePin(root, manifestPath),
            }, PronunciationAudit.JsonOptions));
        }

        var parentPath = Path.Combine(root, "experiments", "parent.json");
        WriteFixture(parentPath, new JsonObject { ["sources"] = sources });
        var planPath = Path.Combine(root, "resources", "lexicons", "pronunciation-provenance.v1.json");
        WriteFixture(planPath, new RecoveryPlan("1.0.0", FixturePin(root, parentPath), "2026-10-01T00:00:00Z", 50,
            FixtureLanguages, ".enoch/pronunciation-provenance/v1", "reports/pronunciation-provenance/v1/coverage.json",
            "experiments/confirmatory.provenance.v1.json"));
        var apiPath = Path.Combine(root, "raw", "test.json");
        WriteFixture(apiPath, new
        {
            query = new
            {
                pages = new[] { new { title = "test", revisions = new[]
            {
                new { revid = 42, timestamp = "2026-09-01T00:00:00Z", slots = new { main = new { content = "pronunciation source" } } },
            } } }
            },
        });
        var htmlPath = Path.Combine(root, "raw", "test.html");
        var html = Html(FixtureHeadings[0], "IPA",
            new Dictionary<string, string> { ["1"] = FixtureCodes[0], ["2"] = "/pat/" });
        for (var index = 1; index < FixtureLanguages.Length; index++)
        {
            var section = Html(FixtureHeadings[index], "IPA",
                new Dictionary<string, string> { ["1"] = FixtureCodes[index], ["2"] = "/pat/" });
            var start = section.IndexOf("<h2>", StringComparison.Ordinal);
            var end = section.IndexOf("</body>", StringComparison.Ordinal);
            html = html.Replace("</body>", section[start..end].Replace("#mwt1", "#mwt" + (index + 1), StringComparison.Ordinal) +
                "</body>", StringComparison.Ordinal);
        }

        File.WriteAllText(htmlPath, html, new UTF8Encoding(false));
        pages.Add(new("test", 42, "2026-09-01T00:00:00Z", FixturePin(root, apiPath), FixturePin(root, htmlPath)));
        var rightsPath = Path.Combine(root, "raw", "rights.json");
        WriteFixture(rightsPath, new
        {
            query = new { rightsinfo = new { url = "https://creativecommons.org/licenses/by-sa/4.0/deed.en", text = "Creative Commons Attribution-Share Alike 4.0" } },
        });
        var generators = FixtureGeneratorTitles.Select(title =>
        {
            var path = Path.Combine(root, "raw", PronunciationPolicyEvaluator.StableId(title) + ".json");
            WriteFixture(path, new { query = new { pages = new[] { new { title, missing = true } } } });
            return new RecoveryPage(title, 0, null, FixturePin(root, path), null);
        }).ToArray();
        WriteFixture(Path.Combine(root, ".enoch", "pronunciation-provenance", "v1", "snapshot.json"),
            new RecoveryPackage("1.0.0", new(FixturePin(root, planPath).Path, PronunciationAudit.HashConfiguration(planPath)),
                "CC-BY-SA-4.0", FixturePin(root, rightsPath), [.. pages], generators));
        return planPath;
    }

    private static RecoveryPin FixturePin(string root, string path) =>
        new(Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'), PronunciationAudit.Hash(path));

    private static void WriteFixture<T>(string path, T value)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(value, PronunciationAudit.JsonOptions) + "\n", new UTF8Encoding(false));
    }

    private static PronunciationOriginProof Bind(PronunciationEvidence row, string html) =>
        PronunciationOriginProofBuilder.Bind(row, Page(), html);

    private static RecoveryPage Page() => new("test", 42, "2026-09-01T00:00:00Z",
        new("raw.json", new string('a', 64)), new("raw.html", new string('b', 64)));

    private static PronunciationEvidence Row(string language = "hin") => new()
    {
        EntryId = "entry",
        Source = "kaikki-raw",
        Snapshot = "snapshot",
        SourceEntry = "https://en.wiktionary.org/wiki/test",
        Language = language,
        Lemma = "test",
        PartOfSpeech = "noun",
        LexicalIdentity = "test\u001fnoun\u001f0",
        Convention = "pinned-convention",
        Labels = [],
        OriginalIpa = "/pat/",
        NormalizedIpa = "pat",
        Origin = "unknown",
        Dependencies = ["enwiktionary"],
        AlternativeCount = 2,
        AlternativeWeight = 0.5,
        PhonemeLength = 3,
        LengthBand = "03-05",
        UnknownSegments = [],
        Exclusions = [],
        EvidenceStatus = "exploratory",
    };

    private static string Html(
        string language, string template, Dictionary<string, string> parameters, string pos = "Noun", int etymology = 0)
    {
        var data = JsonSerializer.Serialize(new
        {
            parts = new[] { new { template = new { target = new { wt = template }, @params =
                parameters.ToDictionary(parameter => parameter.Key, parameter => new { wt = parameter.Value }) } } },
        });
        var heading = etymology == 0 ? "" : "<h3>Etymology " + etymology + "</h3>";
        return $$"""
            <!DOCTYPE html><html about="//en.wiktionary.org/wiki/Special:Redirect/revision/42">
            <head><title>test</title><meta property="dc:modified" content="2026-09-01T00:00:00Z"/></head>
            <body><h2>{{language}}</h2>{{heading}}<h3>Pronunciation</h3>
            <span typeof="mw:Transclusion" about="#mwt1" data-mw='{{System.Net.WebUtility.HtmlEncode(data)}}'>label</span>
            <span class="IPA" about="#mwt1">/pat/</span>
            <h3>{{pos}}</h3><p>semantic-sentinel</p></body></html>
            """;
    }

    private static void ValidateSchema<T>(T value)
    {
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(RepositoryRoot, "resources", "lexicons",
            "schemas", "pronunciation-recovery.schema.json")), new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        Assert.IsTrue(schema.Evaluate(JsonSerializer.SerializeToElement(value, PronunciationAudit.JsonOptions)).IsValid);
    }
}
