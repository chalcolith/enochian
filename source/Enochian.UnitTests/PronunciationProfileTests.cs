using Enochian.Provenance;
using Json.Schema;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using IpaEncoder = Enochian.Text.Encoder;

namespace Enochian.UnitTests;

[TestClass]
public sealed class PronunciationProfileTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    [TestMethod]
    [DataRow("كِتَابٌ", "kitaːbun")]
    [DataRow("كَتَبَ", "kataba")]
    [DataRow("كَتْبْ", "katb")]
    [DataRow("مُدَرِّسٌ", "mudarrisun")]
    [DataRow("صُوتٌ", "sˤuːtun")]
    [DataRow("ضَبْطٌ", "dˤabtˤun")]
    [DataRow("ظِلٌّ", "ðˤillun")]
    [DataRow("حَرْفٌ", "ħarfun")]
    [DataRow("عَيْنٌ", "ʕajnun")]
    [DataRow("جَمَلٌ", "d͡ʒamalun")]
    [DataRow("نُورٌ", "nuːrun")]
    [DataRow("فِيلٌ", "fiːlun")]
    [DataRow("كَوْنٌ", "kawnun")]
    [DataRow("هُدَى", "hudaː")]
    [DataRow("آدَمُ", "ʔaːdamu")]
    [DataRow("هَٰذَا", "haːðaː")]
    [DataRow("كِتَابًا", "kitaːban")]
    [DataRow("مَدْرَسَةٌ", "madrasatun")]
    [DataRow("الْقَمَرُ", "ʔalqamaru")]
    [DataRow("الشَّمْسُ", "ʔaʃʃamsu")]
    public void ClassicalArabicRetainsExplicitCitationPhonology(string form, string expected)
    {
        var result = ClassicalArabicConverter.Convert(form);
        Assert.IsEmpty(result.Diagnostics);
        Assert.AreEqual(expected, result.Ipa);
    }

    [TestMethod]
    [DataRow("كتب")]
    [DataRow("كِتَاب")]
    [DataRow("مَدْرَسَة")]
    [DataRow("مَدْرَسَةْ")]
    [DataRow("الشَمْسُ")]
    [DataRow("كَُتَبَ")]
    [DataRow("كَتَبَ word")]
    [DataRow("كِتَابٌ۞")]
    [DataRow("")]
    public void MissingVocalizationOrContextCannotProducePartialArabicIpa(string form)
    {
        var result = ClassicalArabicConverter.Convert(form);
        Assert.IsNull(result.Ipa);
        Assert.IsNotEmpty(result.Diagnostics);
    }

    [TestMethod]
    [DataRow("lat", "Classical-Latin")]
    [DataRow("lat", "Ecclesiastical")]
    [DataRow("hbo", "reconstructed Biblical")]
    [DataRow("hbo", "Modern")]
    [DataRow("hbo", "BHSA")]
    [DataRow("ara", "Modern-Standard-Arabic")]
    [DataRow("ara", "")]
    [DataRow("grc", "1ˢᵗ CE Egyptian")]
    [DataRow("grc", "5ᵗʰ BCE Attic")]
    [DataRow("grc", "10ᵗʰ CE Medieval")]
    [DataRow("grc", "15ᵗʰ CE Constantinopolitan")]
    public void HistoricalProfilesRejectOtherConventions(string language, string label)
    {
        var profile = LoadPanel().Profiles.Single(candidate => candidate.Language == language);
        var builder = CreateBuilder(profile);
        builder.AddReading(Reading(profile, "/pat/", [label]));
        var (coverage, evidence, entries, _) = builder.Build();
        Assert.IsEmpty(entries);
        CollectionAssert.Contains(evidence.Single().Exclusions, "convention_mismatch");
        Assert.AreEqual("exploratory", coverage.Status);
    }

    [TestMethod]
    [DataRow("η", "/i/")]
    [DataRow("ει", "/i/")]
    [DataRow("οι", "/y/")]
    [DataRow("αι", "/e/")]
    [DataRow("ου", "/u/")]
    [DataRow("αὐτός", "/aɸˈtos/")]
    [DataRow("αὐλή", "/aˈβli/")]
    [DataRow("φωνή", "/ɸoˈni/")]
    [DataRow("θεός", "/θeˈos/")]
    [DataRow("χάρις", "/ˈxa.ris/")]
    [DataRow("βίος", "/ˈβi.os/")]
    [DataRow("δέκα", "/ˈðe.ka/")]
    public void KoineDictionaryRulesPreserveSelectedSegmentsButNotStress(string lemma, string ipa)
    {
        var profile = LoadPanel().Profiles.Single(candidate => candidate.Language == "grc");
        var language = Language(profile);
        var builder = new PronunciationCoverageBuilder(language,
            new PronunciationPolicy { MinimumPhonemes = 1 }, CreateEncoder(), profile);
        builder.AddReading(Reading(profile, ipa, ["4ᵗʰ CE Koine"]) with { Lemma = lemma });
        var (_, evidence, entries, _) = builder.Build();
        Assert.AreEqual(ipa, evidence.Single().OriginalIpa);
        Assert.AreEqual(PronunciationPolicyEvaluator.NormalizeIpa(ipa), entries.Single().Ipa);
        Assert.AreEqual(profile.ArtifactStem, entries.Single().Dialect);
        Assert.IsTrue(entries.Single().Ipa.All(character => character is not ('ˈ' or 'ˌ' or '.')));
    }

    [TestMethod]
    public void ProfileIdentitySeparatesLegacyArtifactsVersionsRulesAndHomographs()
    {
        var profile = LoadPanel().Profiles.Single(candidate => candidate.Language == "lat");
        var reading = Reading(profile, "/pat/", profile.RequiredLabels);
        var legacy = new PronunciationCoverageBuilder(Language(profile), new PronunciationPolicy(), CreateEncoder());
        legacy.AddReading(reading);
        var builder = CreateBuilder(profile);
        builder.AddReading(reading);
        builder.AddReading(reading with { OriginalIpa = "/bat/" });
        builder.AddReading(reading with { LexicalIdentity = "homograph-2" });
        var (_, evidence, entries, _) = builder.Build();
        Assert.HasCount(3, entries);
        Assert.AreEqual(1.0, evidence.Where(row => row.LexicalIdentity == reading.LexicalIdentity)
            .Sum(row => row.AlternativeWeight));
        var legacyId = legacy.Build().Entries.Single().EntryId;
        Assert.IsFalse(entries.Any(entry => entry.EntryId == legacyId));

        var newer = profile with { Version = "2.0.0" };
        var other = CreateBuilder(newer);
        other.AddReading(reading);
        Assert.AreNotEqual(profile.Fingerprint, newer.Fingerprint);
        Assert.AreNotEqual(profile.ArtifactStem, newer.ArtifactStem);
        Assert.IsFalse(entries.Any(entry => entry.EntryId == other.Build().Entries.Single().EntryId));
        var changed = profile with { Rules = new SortedDictionary<string, string>(profile.Rules, StringComparer.Ordinal) };
        changed.Rules["scope"] = "Different frozen research assumption.";
        Assert.AreNotEqual(profile.Fingerprint, changed.Fingerprint);
    }

    [TestMethod]
    public void CompatibleDictionaryReadingsTakePriorityOverConverterCandidates()
    {
        var profile = LoadPanel().Profiles.Single(candidate => candidate.Language == "ara");
        var builder = CreateBuilder(profile);
        builder.AddReading(Reading(profile, "/pat/", ["Classical-Arabic"]));
        builder.AddReading(Reading(profile, "/bat/", ["Classical-Arabic"]) with
        {
            Source = "converter-source",
            Origin = "converter-generated",
            Generator = ClassicalArabicConverter.Version,
        });
        var (_, evidence, entries, _) = builder.Build();
        Assert.AreEqual("pat", entries.Single().Ipa);
        CollectionAssert.Contains(evidence.Single(row => row.Origin == "converter-generated").Exclusions, "dictionary_preferred");
    }

    [TestMethod]
    public async Task OfflineProfilePipelineIncludesClassicalArabicAndReproducesEveryArtifact()
    {
        using var fixture = new Fixture();
        var (panelPath, _) = fixture.CreateInputs();
        var audit = new PronunciationAudit(fixture.Root);
        await audit.RunPanelAsync(panelPath, acquire: false);
        var first = Directory.GetFiles(fixture.Output).ToDictionary(path => Path.GetFileName(path), PronunciationAudit.Hash);
        await audit.RunPanelAsync(panelPath, acquire: false);
        foreach (var path in Directory.GetFiles(fixture.Output))
        {
            Assert.AreEqual(first[Path.GetFileName(path)], PronunciationAudit.Hash(path), path);
        }

        Assert.IsEmpty(Directory.GetDirectories(fixture.Output));
        Assert.IsFalse(Directory.GetFiles(fixture.Output).Any(path => path.Contains("review", StringComparison.Ordinal)));
        var panel = PronunciationPanel.Load(panelPath, fixture.Root);
        foreach (var profile in panel.Profiles)
        {
            var prefix = Path.Combine(fixture.Output, profile.ArtifactStem);
            var entries = File.ReadLines(prefix + ".jsonl")
                .Select(line => JsonSerializer.Deserialize<PronunciationNormalizedEntry>(line, PronunciationAudit.JsonOptions)!)
                .ToArray();
            Assert.HasCount(1, entries, profile.Language);
            Assert.AreEqual(profile.ArtifactStem, entries[0].Dialect);
            using var manifest = JsonDocument.Parse(File.ReadAllText(prefix + ".manifest.json"));
            Assert.AreEqual(profile.Fingerprint, manifest.RootElement.GetProperty("profile_sha256").GetString());
            Assert.AreEqual("exploratory", manifest.RootElement.GetProperty("status").GetString());
        }

        var arabicEvidence = File.ReadAllText(Path.Combine(fixture.Output, "ara-classical-citation-v1.0.0.evidence.jsonl"));
        Assert.IsTrue(arabicEvidence.Contains("converter-generated", StringComparison.Ordinal));
        using var coverage = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixture.Output, "coverage.json")));
        var schema = JsonSchema.FromText(File.ReadAllText(GetPath("resources/lexicons/schemas/pronunciation-coverage.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        Assert.IsTrue(schema.Evaluate(coverage.RootElement).IsValid);
        File.AppendAllText(Path.Combine(fixture.Root, "plan.json"), " ");
        _ = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => audit.RunPanelAsync(panelPath, acquire: false));
    }

    [TestMethod]
    public async Task UnavailableClassicalDataProducesEmptyExcludedArtifactNotMsaOrGuessedVowels()
    {
        using var fixture = new Fixture();
        var (panelPath, panel) = fixture.CreateInputs();
        File.WriteAllText(panelPath, JsonSerializer.Serialize(
            panel with { FallbackSourceManifests = [] }, PronunciationAudit.JsonOptions));
        await new PronunciationAudit(fixture.Root).RunPanelAsync(panelPath, acquire: false);
        var prefix = Path.Combine(fixture.Output, "ara-classical-citation-v1.0.0");
        Assert.AreEqual(0, new FileInfo(prefix + ".jsonl").Length);
        using var manifest = JsonDocument.Parse(File.ReadAllText(prefix + ".manifest.json"));
        Assert.AreEqual("excluded_no_usable_readings", manifest.RootElement.GetProperty("status").GetString());
    }

    [TestMethod]
    public void AstronomyCatalogRulesAreDeterministicAndDoNotTranslateArabicNames()
    {
        var first = AstronomicalNameRegistry.ReadNames(new StringReader(
            "id,proper\n0,Sol\n12,\"Alpha, B\"\n3,Sirius\n4,Sirius\n5,\n"));
        var second = AstronomicalNameRegistry.ReadNames(new StringReader(
            "id,proper\n5,\n4,Sirius\n3,Sirius\n12,\"Alpha, B\"\n0,Sol\n"));
        Assert.AreEqual(JsonSerializer.Serialize(first), JsonSerializer.Serialize(second));
        Assert.HasCount(3, first);
        Assert.AreEqual("Alpha, B", first[0].OriginalName);
        Assert.AreEqual(2, first.Count(name => name.MatchKey == "Sirius"));
        Assert.IsTrue(first.Where(name => name.MatchKey == "Sirius").All(name => name.Ambiguous));
        _ = Assert.ThrowsExactly<InvalidDataException>(() => AstronomicalNameRegistry.ReadNames(
            new StringReader("id,proper\n1,Sirius\n1,Other\n")));
    }

    [TestMethod]
    public async Task AstronomyRegistrationIsPinnedOfflineAndAppliesOnePolicyToEveryPanelLanguage()
    {
        using var fixture = new Fixture();
        _ = fixture.CreateInputs();
        var path = fixture.CreateAstronomyInputs();
        var registry = new AstronomicalNameRegistry(fixture.Root);
        await registry.RunAsync(path, acquire: false);
        var reportPath = Path.Combine(fixture.Root, "astronomy", "registration.json");
        var hash = PronunciationAudit.Hash(reportPath);
        await registry.RunAsync(path, acquire: false);
        Assert.AreEqual(hash, PronunciationAudit.Hash(reportPath));
        using var report = JsonDocument.Parse(File.ReadAllText(reportPath));
        Assert.AreEqual("registered_not_evaluated", report.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(2, report.RootElement.GetProperty("catalog_names").GetInt32());
        var languages = report.RootElement.GetProperty("languages").EnumerateArray().ToArray();
        Assert.HasCount(4, languages);
        Assert.IsTrue(languages.All(language => language.GetProperty("status").GetString() == "not_evaluated"));
        File.AppendAllText(Path.Combine(fixture.Root, "stars.csv"), "4,Other\n");
        _ = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => registry.RunAsync(path, acquire: false));
        Assert.AreEqual(hash, PronunciationAudit.Hash(reportPath));
    }

    [TestMethod]
    public void ConfigurationPinsAreIndependentOfCheckoutLineEndings()
    {
        using var fixture = new Fixture();
        var path = Path.Combine(fixture.Root, "config.json");
        File.WriteAllText(path, "{\n}\n", new UTF8Encoding(false));
        var hash = PronunciationAudit.HashConfiguration(path);
        File.WriteAllText(path, "{\r\n}\r\n", new UTF8Encoding(false));
        Assert.AreEqual(hash, PronunciationAudit.HashConfiguration(path));
        var panel = LoadPanel();
        Assert.AreEqual(panel.DictionaryProtocolSha256, PronunciationAudit.HashConfiguration(GetPath(panel.DictionaryProtocol)));
    }

    [TestMethod]
    public void PublishedProfileReportsMatchTheirSchemasAndFrozenPanel()
    {
        var panel = LoadPanel();
        using var coverage = JsonDocument.Parse(File.ReadAllText(GetPath(
            "reports/pronunciation-profiles/v1/coverage.json")));
        var schema = JsonSchema.FromText(File.ReadAllText(GetPath(
            "resources/lexicons/schemas/pronunciation-coverage.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        Assert.IsTrue(schema.Evaluate(coverage.RootElement).IsValid);
        Assert.AreEqual(PronunciationAudit.HashConfiguration(GetPath(
            "resources/lexicons/pronunciation-panel.v1.json")),
            coverage.RootElement.GetProperty("policy_sha256").GetString());
        CollectionAssert.AreEquivalent(panel.Profiles.Select(profile => profile.Language).ToArray(),
            coverage.RootElement.GetProperty("languages").EnumerateArray()
                .Select(language => language.GetProperty("language").GetString()).ToArray());
        using var registration = JsonDocument.Parse(File.ReadAllText(GetPath(
            "reports/pronunciation-profiles/v1/astronomy-registration.json")));
        Assert.AreEqual("registered_not_evaluated", registration.RootElement.GetProperty("status").GetString());
        Assert.AreEqual(PronunciationAudit.HashConfiguration(GetPath("experiments/astronomical-names.v1.json")),
            registration.RootElement.GetProperty("protocol_sha256").GetString());
        var registered = registration.RootElement.GetProperty("languages").EnumerateArray().ToArray();
        Assert.HasCount(panel.Profiles.Length, registered);
        foreach (var profile in panel.Profiles)
        {
            var language = registered.Single(row => row.GetProperty("language").GetString() == profile.Language);
            Assert.AreEqual(profile.Fingerprint, language.GetProperty("profile_sha256").GetString());
            Assert.AreEqual("not_evaluated", language.GetProperty("status").GetString());
        }
    }

    private static PronunciationPanel LoadPanel() =>
        PronunciationPanel.Load(GetPath("resources/lexicons/pronunciation-panel.v1.json"), RepositoryRoot);

    private static PronunciationReading Reading(PronunciationProfile profile, string ipa, string[] labels) =>
        new("fixture", "pinned", "https://example.invalid/lemma", profile.Language, "lemma", "noun", "lemma-1",
            ipa, labels, "unknown", null, ["unknown-upstream"], "MIT");

    private static PronunciationLanguage Language(PronunciationProfile profile) => new()
    {
        Language = profile.Language,
        DictionaryCode = profile.Language,
        Family = "fixture",
        Convention = profile.Convention,
        RequiredLabels = profile.RequiredLabels,
        RejectedLabels = profile.RejectedLabels,
    };

    private static PronunciationCoverageBuilder CreateBuilder(PronunciationProfile profile) =>
        new(Language(profile), new PronunciationPolicy(), CreateEncoder(), profile);

    private static IpaEncoder CreateEncoder()
    {
        var flow = new Flow.Flow(GetPath("samples/ipatransducer.json"));
        AssertUtils.NoErrors(flow);
        return new IpaEncoder(flow.FeatureSets.Single(features => features.Id == "Default"),
            flow.Encodings.Single(encoding => encoding.Id == "IPA"));
    }

    private static string GetPath(string path) => Path.Combine(RepositoryRoot, path.Replace('/', Path.DirectorySeparatorChar));

    private sealed class Fixture : IDisposable
    {
        public Fixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "enochian-profiles-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Output => Path.Combine(Root, "profiles");

        public (string Path, PronunciationPanel Panel) CreateInputs()
        {
            foreach (var asset in new[]
            {
                "samples/ipatransducer.json",
                "resources/encodings/features.json",
                "resources/encodings/ipa.json",
                "resources/lexicons/schemas/source-manifest.schema.json",
                "resources/lexicons/schemas/pronunciation-audit.schema.json",
                "resources/lexicons/schemas/pronunciation-panel.schema.json",
                "resources/lexicons/schemas/astronomical-names.schema.json",
            })
            {
                var destination = Path.Combine(Root, asset.Replace('/', Path.DirectorySeparatorChar));
                _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(GetPath(asset), destination);
            }

            var raw = Path.Combine(Root, "raw.jsonl");
            File.WriteAllText(raw, """
                {"lang_code":"la","word":"vinum","pos":"noun","sounds":[{"ipa":"/wiːnum/","tags":["Classical-Latin"]},{"ipa":"/ˈviː.num/","note":"modern Italianate Ecclesiastical"}]}
                {"lang_code":"grc","word":"φωνή","pos":"noun","sounds":[{"ipa":"/ɸoˈne/","note":"1ˢᵗ CE Egyptian"},{"ipa":"/ɸoˈni/","note":"4ᵗʰ CE Koine"}]}
                {"lang_code":"he","word":"אב","pos":"noun","sounds":[{"ipa":"/ʔav/","tags":["Modern"]},{"ipa":"/ʔɔːv/","tags":["Tiberian"]}]}
                {"lang_code":"ar","word":"كتاب","pos":"noun","sounds":[{"ipa":"/kitaːbun/","tags":["Modern-Standard-Arabic"]}]}
                """ + "\n");
            WriteManifest("dictionary.manifest.json", "fixture", "raw.jsonl", "wiktextract-raw");
            var arabic = Path.Combine(Root, "arabic.jsonl");
            var row = new VocalizedArabicLemma
            {
                Language = "ara",
                Convention = "classical-arabic",
                Lemma = "كتاب",
                Form = "كِتَابٌ",
                PartOfSpeech = "noun",
                LexicalIdentity = "classical-1",
                EntryKind = "lemma",
            };
            PronunciationAudit.WriteLines(arabic,
            [
                row,
                row with { Form = "كتاب", LexicalIdentity = "unvocalized" },
                row with { Convention = "modern-standard-arabic", LexicalIdentity = "modern" },
                row with { EntryKind = "proper-name", LexicalIdentity = "name" },
            ]);
            WriteManifest("arabic.manifest.json", "fixture-classical", "arabic.jsonl", "classical-arabic-lexemes");
            var baseline = JsonSerializer.Deserialize<PronunciationPlan>(
                File.ReadAllText(GetPath("resources/lexicons/pronunciation-audit.protocol.json")),
                PronunciationAudit.JsonOptions)!;
            var languages = new[] { "lat", "grc", "hbo", "ara" };
            var plan = baseline with
            {
                SourceManifests = ["dictionary.manifest.json"],
                Languages = [.. baseline.Languages.Where(language => languages.Contains(language.Language))
                    .Select(language => language with { LexiconPath = null })],
            };
            var planPath = Path.Combine(Root, "plan.json");
            File.WriteAllText(planPath, JsonSerializer.Serialize(plan, PronunciationAudit.JsonOptions));
            var panel = LoadPanel() with
            {
                DictionaryProtocol = "plan.json",
                DictionaryProtocolSha256 = PronunciationAudit.HashConfiguration(planPath),
                OutputDirectory = "profiles",
                ReportDirectory = "profiles",
                FallbackSourceManifests = ["arabic.manifest.json"],
                Profiles = [.. LoadPanel().Profiles.Where(profile => languages.Contains(profile.Language))],
            };
            var panelPath = Path.Combine(Root, "panel.json");
            File.WriteAllText(panelPath, JsonSerializer.Serialize(panel, PronunciationAudit.JsonOptions));
            return (panelPath, panel);
        }

        public string CreateAstronomyInputs()
        {
            File.WriteAllText(Path.Combine(Root, "stars.csv"), "id,proper\n0,Sol\n2,Sirius\n3,Acrab\n");
            WriteManifest("stars.manifest.json", "fixture-stars", "stars.csv", "hyg-csv");
            var baseline = JsonSerializer.Deserialize<AstronomicalNameProtocol>(
                File.ReadAllText(GetPath("experiments/astronomical-names.v1.json")), PronunciationAudit.JsonOptions)!;
            var protocol = baseline with
            {
                Manifest = "stars.manifest.json",
                PronunciationPanel = "panel.json",
                PronunciationPanelSha256 = PronunciationAudit.HashConfiguration(Path.Combine(Root, "panel.json")),
                OutputDirectory = "astronomy",
                ReportPath = "astronomy/registration.json",
            };
            var path = Path.Combine(Root, "astronomy.json");
            File.WriteAllText(path, JsonSerializer.Serialize(protocol, PronunciationAudit.JsonOptions));
            return path;
        }

        public void Dispose() => Directory.Delete(Root, true);

        private void WriteManifest(string file, string id, string rawPath, string parser)
        {
            var manifest = JsonNode.Parse(File.ReadAllText(GetPath(
                "resources/lexicons/manifests/kaikki-raw.manifest.json")))!.AsObject();
            manifest["source_id"] = id;
            manifest["raw_path"] = rawPath;
            manifest["sha256"] = PronunciationAudit.Hash(Path.Combine(Root, rawPath));
            manifest["parser"]!["id"] = parser;
            if (parser == "classical-arabic-lexemes")
            {
                manifest["language"] = "ara";
            }

            File.WriteAllText(Path.Combine(Root, file), manifest.ToJsonString());
        }
    }
}
