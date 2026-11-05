using Enochian.Provenance;
using Json.Schema;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using IpaEncoder = Enochian.Text.Encoder;

namespace Enochian.UnitTests;

[TestClass]
public sealed class PronunciationDictionaryTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    [TestMethod]
    public void CountsMissingIpaAndExcludesFormOnlyEntries()
    {
        var builder = CreateBuilder();
        AddRaw(builder, """{"word":"alpha","pos":"noun","sounds":[{"ipa":"/pat/"}]}""");
        AddRaw(builder, """{"word":"beta","pos":"noun"}""");
        AddRaw(builder, """{"word":"alphas","pos":"noun","senses":[{"form_of":[{"word":"alpha"}]}],"sounds":[{"ipa":"/pats/"}]}""");

        var (coverage, evidence, entries, _) = builder.Build();

        Assert.AreEqual(3, coverage.SourceRecords);
        Assert.AreEqual(2, coverage.EligibleLexicalUnits);
        Assert.AreEqual(1, coverage.LemmasWithIpa);
        Assert.AreEqual(1, coverage.UsableLemmas);
        Assert.AreEqual(1, coverage.Exclusions["source_entry_kind"]);
        Assert.AreEqual("unknown", evidence.Single().Origin);
        Assert.AreEqual("exploratory", coverage.Status);
        Assert.HasCount(1, entries);
    }

    [TestMethod]
    public void PreservesConventionsTemplatesAndUnsupportedSegments()
    {
        var language = Language() with
        {
            RequiredLabels = ["modern Italianate Ecclesiastical"],
            RejectedLabels = ["Classical-Latin"],
        };
        var builder = CreateBuilder(language);
        AddRaw(builder, """
            {"word":"alpha","pos":"noun","sound_templates":[{"name":"la-IPA"}],"sounds":[
              {"ipa":"/pat/","tags":["Classical-Latin"]},
              {"ipa":"[ˈpa.t]","note":"modern Italianate Ecclesiastical"},
              {"ipa":"/Бat/","note":"modern Italianate Ecclesiastical"}]}
            """);

        var (coverage, evidence, entries, _) = builder.Build();

        Assert.HasCount(3, evidence);
        Assert.HasCount(1, entries);
        Assert.AreEqual("pat", entries.Single().Ipa);
        Assert.AreEqual(1, coverage.Exclusions["convention_mismatch"]);
        Assert.AreEqual(1, coverage.Exclusions["unknown_ipa"]);
        Assert.IsTrue(evidence.All(row => row.Origin == "template-generated" && row.Generator == "la-IPA"));
        Assert.IsTrue(evidence.All(row => row.Independence == "unknown" && row.GeneratorRevision == null));
        Assert.AreEqual(0, coverage.SupportedEvidenceLemmas);
    }

    [TestMethod]
    public void AlternativesHaveStableIdsAndCannotInflateLexicalCounts()
    {
        var alpha = Reading("alpha", "/pat/");
        var beta = Reading("alpha", "/bat/");
        var gamma = Reading("gamma", "/kat/");
        var first = CreateBuilder();
        var second = CreateBuilder();
        foreach (var reading in new[] { alpha, beta, gamma, alpha })
        {
            first.AddReading(reading);
        }

        foreach (var reading in new[] { gamma, alpha, beta, alpha })
        {
            second.AddReading(reading);
        }

        var (coverage, evidence, entries, _) = first.Build();
        var (otherCoverage, otherEvidence, otherEntries, _) = second.Build();
        Assert.AreEqual(2, coverage.UsableLemmas);
        Assert.AreEqual(1, coverage.DuplicateReadings);
        Assert.HasCount(3, evidence);
        Assert.AreEqual(1.0, evidence.Where(row => row.Lemma == "alpha").Sum(row => row.AlternativeWeight));
        Assert.IsTrue(evidence.Where(row => row.Lemma == "alpha").All(row => row.AlternativeCount == 2));
        Assert.AreEqual(Serialize(evidence), Serialize(otherEvidence));
        Assert.AreEqual(Serialize(entries), Serialize(otherEntries));
        Assert.AreEqual(Serialize(coverage), Serialize(otherCoverage));
    }

    [TestMethod]
    public void NormalizesLemmaButRetainsOriginalSourceSpellingDeterministically()
    {
        var first = CreateBuilder();
        var second = CreateBuilder();
        var decomposed = """{"word":"cafe\u0301","pos":"noun","sounds":[{"ipa":"/pat/"}]}""";
        var composed = """{"word":"caf\u00e9","pos":"noun","sounds":[{"ipa":"/pat/"}]}""";
        AddRaw(first, decomposed);
        AddRaw(first, composed);
        AddRaw(second, composed);
        AddRaw(second, decomposed);
        var (_, _, entries, _) = first.Build();
        var (_, _, otherEntries, _) = second.Build();
        Assert.AreEqual("caf\u00e9", entries.Single().Lemma);
        Assert.AreEqual("cafe\u0301", entries.Single().OriginalForm);
        Assert.AreEqual(Serialize(entries), Serialize(otherEntries));
    }

    [TestMethod]
    public void HomographsAndUnknownPartOfSpeechProduceExplicitJoinAmbiguity()
    {
        using var fixture = new TemporaryDirectory();
        var builder = CreateBuilder();
        builder.AddReading(Reading("alpha", "/pat/") with { LexicalIdentity = "alpha:noun:1" });
        builder.AddReading(Reading("alpha", "/bat/") with { LexicalIdentity = "alpha:noun:2" });
        builder.AddReading(Reading("beta", "/kat/"));
        var path = Path.Combine(fixture.Path, "targets.jsonl");
        File.WriteAllText(path, """
            {"entry_id":"target:tur:1","lemma":"alpha","entry_kind":"lemma","part_of_speech":"noun","definition":"ignore this gloss"}
            {"entry_id":"target:tur:2","lemma":"beta","entry_kind":"lemma","part_of_speech":null}
            {"entry_id":"target:tur:3","lemma":"missing","entry_kind":"lemma","part_of_speech":"noun"}
            """ + "\n");

        var (_, _, _, joins) = builder.Build(path);

        Assert.AreEqual("ambiguous", joins[0].Status);
        Assert.IsEmpty(joins[0].DictionaryEntryIds);
        Assert.AreEqual("matched", joins[1].Status);
        Assert.AreEqual("unmatched", joins[2].Status);
    }

    [TestMethod]
    public void SourceOverlapDoesNotImplyIndependentEvidence()
    {
        var builder = CreateBuilder();
        builder.AddReading(Reading("alpha", "/pat/"));
        builder.AddReading(Reading("alpha", "/pat/") with { Source = "copied-dictionary" });

        var (coverage, evidence, _, _) = builder.Build();

        Assert.AreEqual(1, coverage.SourceOverlap["copied-dictionary+fixture"]);
        Assert.IsTrue(evidence.All(row => row.Independence == "unknown"));
        Assert.AreEqual(0, coverage.SupportedEvidenceLemmas);
    }

    [TestMethod]
    public void SuppliedDictionaryEvidenceIsSupportedWithoutGrantingConfirmatoryAdmission()
    {
        var builder = CreateBuilder();
        builder.AddReading(Reading("alpha", "/pat/") with { Origin = "dictionary-supplied" });
        var (coverage, evidence, _, _) = builder.Build();
        Assert.AreEqual(1, coverage.SupportedEvidenceLemmas);
        Assert.AreEqual("supported", evidence.Single().EvidenceStatus);
        Assert.AreEqual("exploratory", coverage.Status);
        CollectionAssert.Contains(coverage.Blockers, "profile_and_retrieval_admission_pending_m2_08_m2_09");
    }

    [TestMethod]
    public void IpaDictionaryRetainsAlternativesAndRejectsWrongArabicConvention()
    {
        using var reader = new StringReader("alpha\t/pat/, /bat/\n");
        var readings = PronunciationDictionaryAdapter.ReadIpaDictionary(
            reader, "ipa-dict-ar", "pinned", "ara", "Modern-Standard-Arabic", "MIT").ToArray();
        var builder = CreateBuilder(Language() with { RequiredLabels = ["Classical-Arabic"] });
        foreach (var reading in readings)
        {
            builder.AddReading(reading);
        }

        var (coverage, evidence, entries, _) = builder.Build();
        Assert.HasCount(2, evidence);
        Assert.IsEmpty(entries);
        Assert.AreEqual(0, coverage.ConventionMatchedLemmas);
        Assert.IsTrue(evidence.All(row => row.Origin == "unknown"));
        Assert.AreEqual(0, coverage.EligibleLexicalUnits);
        Assert.IsTrue(evidence.All(row => row.Exclusions.Contains("unverified_entry_kind")));
    }

    [TestMethod]
    public void EmittedRecordsAndPolicyConformToVersionedSchemas()
    {
        var builder = CreateBuilder();
        builder.AddReading(Reading("alpha", "/pat/"));
        var (_, evidence, entries, _) = builder.Build();
        AssertSchema("pronunciation-evidence.schema.json", Serialize(evidence.Single()));
        AssertSchema("normalized-entry.schema.json", Serialize(entries.Single()));
        AssertSchema("pronunciation-audit.schema.json", File.ReadAllText(GetPath(
            "resources/lexicons/pronunciation-audit.protocol.json")));
        var invalid = JsonNode.Parse(Serialize(evidence.Single()))!.AsObject();
        invalid["origin"] = "human-validated";
        Assert.IsFalse(LoadSchema("pronunciation-evidence.schema.json")
            .Evaluate(JsonSerializer.SerializeToElement(invalid)).IsValid);
        AssertSchema("pronunciation-reference.schema.json", Serialize(References(1).Single()));
    }

    [TestMethod]
    public void KoineInventoryPreservesSourceStagesWithoutAddingAnAtticArm()
    {
        var language = Language() with
        {
            RequiredLabels = ["1ˢᵗ CE Egyptian", "4ᵗʰ CE Koine"],
        };
        var builder = CreateBuilder(language);
        AddRaw(builder, """
            {"word":"alpha","pos":"noun","sounds":[
              {"ipa":"/pat/","note":"1ˢᵗ CE Egyptian"},
              {"ipa":"/bat/","note":"4ᵗʰ CE Koine"},
              {"ipa":"/kat/","note":"5ᵗʰ BCE Attic"}]}
            """);
        var (coverage, evidence, entries, _) = builder.Build();
        Assert.HasCount(2, entries);
        Assert.HasCount(3, evidence);
        Assert.AreEqual(1, coverage.UsableLemmas);
        Assert.AreEqual(1, coverage.Exclusions["convention_mismatch"]);
        Assert.IsTrue(evidence.Where(row => row.Exclusions.Length == 0).All(row => row.Labels.Length == 1));
        Assert.AreEqual("exploratory", coverage.Status);
    }

    [TestMethod]
    public void PublishedCoverageMatrixConformsAndIncludesEveryPlannedLanguage()
    {
        var path = GetPath("reports/pronunciation-dictionaries/coverage.json");
        AssertSchema("pronunciation-coverage.schema.json", File.ReadAllText(path));
        using var report = JsonDocument.Parse(File.ReadAllText(path));
        var plan = JsonSerializer.Deserialize<PronunciationPlan>(File.ReadAllText(GetPath(
            "resources/lexicons/pronunciation-audit.protocol.json")), PronunciationAudit.JsonOptions)
            ?? throw new AssertFailedException("Missing pronunciation plan.");
        var languages = report.RootElement.GetProperty("languages").EnumerateArray().ToArray();
        CollectionAssert.AreEquivalent(plan.Languages.Select(language => language.Language).ToArray(),
            languages.Select(language => language.GetProperty("language").GetString()).ToArray());
        Assert.IsTrue(languages.All(language =>
            language.GetProperty("usable_lemmas").GetInt32() <= language.GetProperty("lemmas_with_ipa").GetInt32() &&
            language.GetProperty("lemmas_with_ipa").GetInt32() <= language.GetProperty("eligible_lexical_units").GetInt32()));
    }

    [TestMethod]
    public void IndependentHeldOutReferencesMeetExactFrozenThreshold()
    {
        var policy = new PronunciationPolicy { MinimumReferences = 100, MinimumPerBand = 25 };
        var references = References(100).ToArray();
        for (var index = 0; index < 5; index++)
        {
            references[index] = references[index] with { GeneratedIpa = "bat" };
        }

        var report = PronunciationPolicyEvaluator.EvaluateReferences(references, policy, CreateEncoder());
        Assert.AreEqual("pass", report.Status);
        Assert.AreEqual(100, report.SampledReferences);
        Assert.AreEqual(0.95, report.ExactAgreement);
        Assert.IsTrue(report.MeanPhoneEditDistance > 0);

        references[5] = references[5] with { GeneratedIpa = "bat" };
        report = PronunciationPolicyEvaluator.EvaluateReferences(references, policy, CreateEncoder());
        Assert.AreEqual("exploratory", report.Status);
        Assert.AreEqual(0.94, report.ExactAgreement);
    }

    [TestMethod]
    [DataRow("shared", "shared_upstream")]
    [DataRow("unknown", "unknown_independence")]
    [DataRow("tuning", "not_held_out")]
    [DataRow("template", "unvalidated_reference_origin")]
    public void ReferencesCannotManufactureIndependentValidation(string mutation, string diagnostic)
    {
        var rows = References(100).Select(row => mutation switch
        {
            "shared" => row with { ReferenceDependencies = ["generator-module"] },
            "unknown" => row with { IndependenceEstablished = false },
            "tuning" => row with { HeldOut = false },
            "template" => row with { ReferenceOrigin = "template-generated" },
            _ => throw new AssertFailedException("Unknown mutation."),
        });
        var report = PronunciationPolicyEvaluator.EvaluateReferences(rows, new PronunciationPolicy(), CreateEncoder());

        Assert.AreEqual("exploratory", report.Status);
        Assert.AreEqual(0, report.IndependentReferences);
        Assert.AreEqual(100, report.Diagnostics[diagnostic]);
        Assert.IsNull(report.ExactAgreement);
    }

    [TestMethod]
    public async Task AcquisitionIsPinnedAndDoesNotOverwriteCorruptLocalData()
    {
        using var fixture = new TemporaryDirectory();
        var bytes = System.Text.Encoding.UTF8.GetBytes("alpha\t/pat/\n");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        var source = new PronunciationSnapshot("fixture", Path.Combine(fixture.Path, "raw", "data"),
            "https://example.invalid/data", hash, "pinned", "tur", "ipa-dict-tsv", "MIT", "Synthetic fixture");
        using var client = new HttpClient(new FixtureHttpHandler(bytes));
        await PronunciationAudit.AcquireAsync(client, source);
        CollectionAssert.AreEqual(bytes, File.ReadAllBytes(source.Path));

        var wrong = source with { Path = Path.Combine(fixture.Path, "raw", "wrong"), Hash = new string('0', 64) };
        _ = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => PronunciationAudit.AcquireAsync(client, wrong));
        Assert.IsFalse(File.Exists(wrong.Path));
        File.WriteAllText(source.Path, "corrupt");
        _ = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => PronunciationAudit.AcquireAsync(client, source));
        Assert.AreEqual("corrupt", File.ReadAllText(source.Path));
        Assert.IsEmpty(Directory.GetFiles(Path.GetDirectoryName(source.Path)!, "*.tmp"));
    }

    [TestMethod]
    public async Task OfflineAuditStreamsPinnedInputsAndReproducesOutputsWithoutHumanSheets()
    {
        using var fixture = new TemporaryDirectory();
        var planPath = CreateOfflineFixture(fixture.Path);
        var audit = new PronunciationAudit(fixture.Path);
        await audit.RunAsync(planPath, acquire: false);
        var output = Path.Combine(fixture.Path, "generated");
        var first = Directory.GetFiles(output).ToDictionary(path => Path.GetFileName(path),
            File.ReadAllText, StringComparer.Ordinal);

        await audit.RunAsync(planPath, acquire: false);

        foreach (var path in Directory.GetFiles(output))
        {
            Assert.AreEqual(first[Path.GetFileName(path)], File.ReadAllText(path), path);
        }

        Assert.IsEmpty(Directory.GetDirectories(output));
        Assert.IsFalse(Directory.GetFiles(output).Any(path => path.Contains("review", StringComparison.Ordinal)));
        using var report = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "coverage.json")));
        var language = report.RootElement.GetProperty("languages")[0];
        Assert.AreEqual(2, language.GetProperty("eligible_lexical_units").GetInt32());
        Assert.AreEqual(1, language.GetProperty("usable_lemmas").GetInt32());
        Assert.AreEqual("exploratory", language.GetProperty("status").GetString());
        AssertSchema("pronunciation-evidence.schema.json", File.ReadAllText(Path.Combine(output, "tur.evidence.jsonl")));
        AssertSchema("normalized-entry.schema.json", File.ReadAllText(Path.Combine(output, "tur.jsonl")));
        AssertSchema("pronunciation-coverage.schema.json", File.ReadAllText(Path.Combine(output, "coverage.json")));

        File.AppendAllText(Path.Combine(fixture.Path, "raw.jsonl"), "{}\n");
        _ = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => audit.RunAsync(planPath, acquire: false));
        Assert.IsEmpty(Directory.GetDirectories(output));
    }

    [TestMethod]
    public void MissingTargetLexiconIsAnExplicitUnevaluatedJoinStatus()
    {
        using var fixture = new TemporaryDirectory();
        var builder = CreateBuilder();
        builder.AddReading(Reading("alpha", "/pat/"));
        var (coverage, _, _, joins) = builder.Build(Path.Combine(fixture.Path, "missing.jsonl"));
        Assert.AreEqual("missing", coverage.LexiconStatus);
        Assert.IsEmpty(joins);
    }

    [TestMethod]
    public void DuplicateReferenceIdsCannotCountAsIndependentLemmas()
    {
        var rows = References(100).Select(row => row with { LemmaId = "same-lemma" });
        var report = PronunciationPolicyEvaluator.EvaluateReferences(rows, new PronunciationPolicy(), CreateEncoder());
        Assert.AreEqual(0, report.IndependentReferences);
        Assert.AreEqual(100, report.Diagnostics["duplicate_reference_lemma"]);
        Assert.AreEqual("exploratory", report.Status);
    }

    private static string CreateOfflineFixture(string root)
    {
        var assets = new[]
        {
            "samples/ipatransducer.json",
            "resources/encodings/features.json",
            "resources/encodings/ipa.json",
            "resources/lexicons/schemas/source-manifest.schema.json",
            "resources/lexicons/schemas/pronunciation-audit.schema.json",
        };
        foreach (var asset in assets)
        {
            var destination = Path.Combine(root, asset.Replace('/', Path.DirectorySeparatorChar));
            _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(GetPath(asset), destination);
        }

        var rawPath = Path.Combine(root, "raw.jsonl");
        File.WriteAllText(rawPath, """
            {"lang_code":"tr","word":"alpha","pos":"noun","sounds":[{"ipa":"/pat/"}]}
            {"lang_code":"tr","word":"beta","pos":"noun"}
            {"lang_code":"en","word":"unrelated","pos":"noun","sounds":[{"ipa":"/bat/"}]}
            """ + "\n");
        var manifest = JsonNode.Parse(File.ReadAllText(GetPath(
            "resources/lexicons/manifests/kaikki-raw.manifest.json")))!.AsObject();
        manifest["source_id"] = "fixture";
        manifest["raw_path"] = "raw.jsonl";
        manifest["sha256"] = PronunciationAudit.Hash(rawPath);
        File.WriteAllText(Path.Combine(root, "manifest.json"), manifest.ToJsonString());
        var baseline = JsonSerializer.Deserialize<PronunciationPlan>(File.ReadAllText(GetPath(
            "resources/lexicons/pronunciation-audit.protocol.json")), PronunciationAudit.JsonOptions)
            ?? throw new AssertFailedException("Missing pronunciation plan.");
        var plan = baseline with
        {
            SourceManifests = ["manifest.json"],
            OutputDirectory = "generated",
            ReportDirectory = "generated",
            Languages = [Language()],
        };
        var path = Path.Combine(root, "plan.json");
        File.WriteAllText(path, Serialize(plan));
        return path;
    }

    private static PronunciationLanguage Language() => new()
    {
        Language = "tur",
        DictionaryCode = "tr",
        Family = "Turkic",
        Convention = "turkish",
    };

    private static PronunciationCoverageBuilder CreateBuilder(PronunciationLanguage? language = null) =>
        new(language ?? Language(), new PronunciationPolicy(), CreateEncoder());

    private static IpaEncoder CreateEncoder()
    {
        var flow = new Flow.Flow(GetPath("samples/ipatransducer.json"));
        AssertUtils.NoErrors(flow);
        return new IpaEncoder(flow.FeatureSets.Single(features => features.Id == "Default"),
            flow.Encodings.Single(encoding => encoding.Id == "IPA"));
    }

    private static PronunciationReading Reading(string lemma, string ipa) =>
        new("fixture", "pinned", "https://example.invalid/" + lemma, "tur", lemma, "noun",
            lemma + ":noun:1", ipa, [], "unknown", null, ["unknown-upstream"], "CC-BY-4.0");

    private static void AddRaw(PronunciationCoverageBuilder builder, string json)
    {
        using var document = JsonDocument.Parse(json);
        builder.AddWiktextract(document.RootElement, "fixture", "pinned", "CC-BY-4.0");
    }

    private static IEnumerable<PronunciationReference> References(int count) =>
        Enumerable.Range(0, count).Select(index => new PronunciationReference
        {
            LemmaId = index.ToString(CultureInfo.InvariantCulture),
            GeneratedIpa = index % 3 == 0 ? "pat" : index % 3 == 1 ? "patpat" : "patpatpatpat",
            ReferenceIpa = index % 3 == 0 ? "pat" : index % 3 == 1 ? "patpat" : "patpatpatpat",
            GeneratorDependencies = ["generator-module"],
            ReferenceDependencies = ["independent-reference"],
            HeldOut = true,
            IndependenceEstablished = true,
            ReferenceOrigin = "dictionary-supplied",
        });

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, PronunciationAudit.JsonOptions);

    private static JsonSchema LoadSchema(string name) =>
        JsonSchema.FromText(File.ReadAllText(GetPath("resources/lexicons/schemas/" + name)),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });

    private static void AssertSchema(string name, string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.IsTrue(LoadSchema(name).Evaluate(document.RootElement).IsValid, name);
    }

    private static string GetPath(string path) =>
        Path.Combine(RepositoryRoot, path.Replace('/', Path.DirectorySeparatorChar));

    private sealed class FixtureHttpHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
                RequestMessage = request,
            });
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "enochian-pronunciation-" + Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, true);
    }
}
