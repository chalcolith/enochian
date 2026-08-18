using Enochian.Controls;
using Enochian.Provenance;
using Json.Schema;
using System.Text;
using System.Text.Json;

namespace Enochian.UnitTests;

[TestClass]
public sealed class ControlPipelineTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    private static readonly string FixtureRoot = Path.Combine(
        RepositoryRoot,
        "source",
        "Enochian.UnitTests",
        "Fixtures",
        "Controls");
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    [TestMethod]
    public void UniMorphPipelineKeepsFormsOutOfPrimaryLemmas()
    {
        using var fixture = new TemporaryDirectory();
        var first = Path.Combine(fixture.Path, "first");
        var second = Path.Combine(fixture.Path, "second");
        var source = UniMorphAdapter.Parse(Path.Combine(FixtureRoot, "unimorph-indic.fixture.tsv"));
        var manifest = LoadManifest("unimorph-hin.manifest.json");
        var provider = new FixtureIpaProvider(source.Lemmas.ToDictionary(
            lemma => lemma.NormalizedForm,
            _ => "kalam",
            StringComparer.Ordinal));
        var pipeline = new ControlPipeline(RepositoryRoot);

        var report = pipeline.Normalize(manifest, source, provider, sampleSize: 4, first);
        _ = pipeline.Normalize(manifest, source, provider, sampleSize: 4, second);

        Assert.AreEqual(5, report.SourceRecords);
        Assert.AreEqual(4, report.LemmaRecords);
        Assert.AreEqual(5, report.InflectedFormRecords);
        Assert.AreEqual(0, report.GeneratedMorphologyRecords);
        Assert.AreEqual(4, report.EmittedRecords);
        Assert.AreEqual("pending_blinded_review", report.EligibilityBlockers.Single());
        Assert.IsTrue(provider.SeenRecordIds.All(recordId => recordId.StartsWith("lemma:", StringComparison.Ordinal)));
        Assert.IsFalse(provider.SeenForms.Contains("क़लमों", StringComparer.Ordinal));
        AssertInflectedEntries(first, "unimorph-hin.inflected-forms.jsonl", expectedCount: 5);
        AssertDeterministic(first, second, "unimorph-hin");
    }

    [TestMethod]
    public void UnsupportedGujaratiProfileProducesExploratoryOutputsWithoutCallingProvider()
    {
        using var fixture = new TemporaryDirectory();
        var source = UniMorphAdapter.Parse(Path.Combine(FixtureRoot, "unimorph-indic.fixture.tsv"));
        var manifest = LoadManifest("unimorph-guj.manifest.json");
        var provider = new FixtureIpaProvider(new Dictionary<string, string>(StringComparer.Ordinal));

        var report = new ControlPipeline(RepositoryRoot).Normalize(
            manifest,
            source,
            provider,
            sampleSize: 4,
            fixture.Path);

        Assert.HasCount(0, provider.SeenRecordIds);
        Assert.AreEqual(0, report.EmittedRecords);
        Assert.AreEqual(4, report.ExcludedRecords);
        Assert.AreEqual(4, report.ExclusionCounts["inadequate_or_unavailable_g2p_profile"]);
        Assert.AreEqual(0, report.ReviewRecords);
        Assert.IsFalse(report.ConfirmatoryEligible);
        CollectionAssert.Contains(report.EligibilityBlockers.ToArray(), "inadequate_or_unavailable_g2p_profile");
        AssertInflectedEntries(fixture.Path, "unimorph-guj.inflected-forms.jsonl", expectedCount: 5);
    }

    [TestMethod]
    public void PersianRemainsExploratoryAfterUnvocalizedLemmasAreExcluded()
    {
        using var fixture = new TemporaryDirectory();
        var source = UniMorphAdapter.Parse(
            Path.Combine(FixtureRoot, "unimorph-persian.fixture.tsv"),
            requireArabicVowelMarks: true);
        var manifest = LoadManifest("unimorph-fas.manifest.json");
        var provider = new FixtureIpaProvider(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["کِتاب"] = "ketab",
        });

        var report = new ControlPipeline(RepositoryRoot).Normalize(
            manifest,
            source,
            provider,
            sampleSize: 1,
            fixture.Path);

        Assert.AreEqual(1, report.EmittedRecords);
        Assert.AreEqual(2, report.InflectedFormRecords);
        Assert.AreEqual(1, report.ExclusionCounts["uncertain_unvocalized_orthography"]);
        CollectionAssert.Contains(report.EligibilityBlockers.ToArray(), "unvocalized_orthography");
        Assert.IsFalse(report.ConfirmatoryEligible);
    }

    [TestMethod]
    public void TurkishPipelinePreservesProviderOutputAndBlocksUnknownIpa()
    {
        using var fixture = new TemporaryDirectory();
        var first = Path.Combine(fixture.Path, "first");
        var second = Path.Combine(fixture.Path, "second");
        var source = ZemberekDictionaryAdapter.Parse(Path.Combine(FixtureRoot, "zemberek.fixture.dict"));
        var manifest = LoadManifest("zemberek.manifest.json");
        var pipeline = new ControlPipeline(RepositoryRoot);
        var provider = new FixtureIpaProvider(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ışık"] = "ɯʃɯk",
            ["iğne"] = "iɰne",
            ["kâğıt"] = "kâɰɯt",
        });

        var report = pipeline.Normalize(manifest, source, provider, sampleSize: 2, first);
        _ = pipeline.Normalize(manifest, source, provider, sampleSize: 2, second);

        Assert.AreEqual(3, report.LemmaRecords);
        Assert.AreEqual(0, report.GeneratedMorphologyRecords);
        Assert.AreEqual(2, report.EmittedRecords);
        Assert.AreEqual(2, report.ReviewRecords);
        Assert.AreEqual(1, report.ExclusionCounts["unknown_ipa"]);
        Assert.IsTrue(report.UnknownIpaSegments.ContainsKey("̂"));
        Assert.IsFalse(report.ConfirmatoryEligible);
        CollectionAssert.Contains(report.EligibilityBlockers.ToArray(), "unknown_or_incomplete_ipa");
        CollectionAssert.Contains(report.EligibilityBlockers.ToArray(), "pending_blinded_review");

        var conversion = File.ReadLines(Path.Combine(first, "zemberek.conversions.jsonl"))
            .Select(line => JsonDocument.Parse(line))
            .Single(document => document.RootElement.GetProperty("source_form").GetString() == "kâğıt");
        using (conversion)
        {
            Assert.AreEqual("kâɰɯt", conversion.RootElement.GetProperty("ipa").GetString());
        }

        AssertNormalizedEntries(first, "zemberek.jsonl", expectedCount: 2);
        AssertDeterministic(first, second, "zemberek");
    }

    [TestMethod]
    public void HungarianPipelineRetainsStemsAndProducesSeparateReviewSet()
    {
        using var fixture = new TemporaryDirectory();
        var source = MagyarIspellAdapter.Parse(Path.Combine(FixtureRoot, "magyar"));
        var manifest = LoadManifest("magyar-ispell.manifest.json");
        var provider = new FixtureIpaProvider(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["asszony"] = "ɒsːoɲ",
            ["csizma"] = "t͡ʃizmɒ",
            ["dzsungel"] = "d͡ʒuŋɡɛl",
            ["hosszú"] = "hosːuː",
            ["kenyér"] = "kɛɲeːr",
            ["kulcscsomó"] = "kult͡ʃt͡ʃomoː",
        });

        var report = new ControlPipeline(RepositoryRoot).Normalize(
            manifest,
            source,
            provider,
            sampleSize: 6,
            fixture.Path);

        Assert.AreEqual(6, report.LemmaRecords);
        Assert.AreEqual(0, report.GeneratedMorphologyRecords);
        Assert.AreEqual(6, report.EmittedRecords);
        Assert.AreEqual(6, report.ReviewRecords);
        Assert.AreEqual(0, report.UnknownIpaRate);
        Assert.IsFalse(report.ConfirmatoryEligible);
        Assert.AreEqual("pending_blinded_review", report.EligibilityBlockers.Single());
        AssertNormalizedEntries(fixture.Path, "magyar-ispell.jsonl", expectedCount: 6);
        Assert.AreEqual(6, File.ReadLines(Path.Combine(fixture.Path, "magyar-ispell.review.jsonl")).Count());
    }

    private static ControlManifest LoadManifest(string fileName)
    {
        return ControlManifest.Load(Path.Combine(
            RepositoryRoot,
            "resources",
            "lexicons",
            "manifests",
            fileName));
    }

    private static void AssertNormalizedEntries(string directory, string fileName, int expectedCount)
    {
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "resources",
            "lexicons",
            "schemas",
            "normalized-entry.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        var entries = File.ReadLines(Path.Combine(directory, fileName))
            .Select(line => JsonDocument.Parse(line))
            .ToArray();
        try
        {
            Assert.HasCount(expectedCount, entries);
            Assert.IsTrue(entries.All(entry => schema.Evaluate(entry.RootElement).IsValid));
            Assert.IsTrue(entries.All(entry => entry.RootElement.GetProperty("entry_kind").GetString() == "lemma"));
            Assert.IsTrue(entries.All(entry =>
                entry.RootElement.GetProperty("ipa_conversion").GetProperty("provider_id").GetString() == "epitran"));
        }
        finally
        {
            foreach (var entry in entries)
            {
                entry.Dispose();
            }
        }
    }

    private static void AssertInflectedEntries(string directory, string fileName, int expectedCount)
    {
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "resources",
            "lexicons",
            "schemas",
            "inflected-form.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        var entries = File.ReadLines(Path.Combine(directory, fileName))
            .Select(line => JsonDocument.Parse(line))
            .ToArray();
        try
        {
            Assert.HasCount(expectedCount, entries);
            Assert.IsTrue(entries.All(entry => schema.Evaluate(entry.RootElement).IsValid));
            Assert.IsTrue(entries.All(entry => entry.RootElement.GetProperty("entry_kind").GetString() == "inflected_form"));
            Assert.IsTrue(entries.All(entry => entry.RootElement.GetProperty("source_record_id").GetString()!
                .StartsWith("form:", StringComparison.Ordinal)));
        }
        finally
        {
            foreach (var entry in entries)
            {
                entry.Dispose();
            }
        }
    }

    private static void AssertDeterministic(string first, string second, string prefix)
    {
        foreach (var suffix in new[] { ".jsonl", ".conversions.jsonl", ".g2p-audit.json", ".review.jsonl", ".quality.json", ".inflected-forms.jsonl" })
        {
            CollectionAssert.AreEqual(
                File.ReadAllBytes(Path.Combine(first, prefix + suffix)),
                File.ReadAllBytes(Path.Combine(second, prefix + suffix)),
                prefix + suffix);
        }
    }

    private sealed class FixtureIpaProvider(IReadOnlyDictionary<string, string> outputs) : IControlIpaProvider
    {
        public List<string> SeenRecordIds { get; } = [];

        public List<string> SeenForms { get; } = [];

        public void Convert(
            string profileId,
            string sourceId,
            string language,
            IEnumerable<ControlSourceLemma> lemmas,
            string outputPath)
        {
            _ = Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false)) { NewLine = "\n" };
            foreach (var lemma in lemmas)
            {
                SeenRecordIds.Add(lemma.RecordId);
                SeenForms.Add(lemma.NormalizedForm);
                var artifact = new IpaConversionArtifact
                {
                    Schema = "ipa-conversion-artifact.schema.json",
                    SchemaVersion = "1.0.0",
                    RecordId = lemma.RecordId,
                    Source = sourceId,
                    Language = language,
                    SourceForm = lemma.NormalizedForm,
                    NormalizedForm = lemma.NormalizedForm,
                    Ipa = outputs[lemma.NormalizedForm],
                    ProviderId = "epitran",
                    ProviderVersion = ControlPipeline.ProviderVersion,
                    ProfileId = profileId,
                    ProfileVersion = ControlPipeline.ProfileVersion,
                    Status = "complete",
                    Diagnostics = [],
                };
                writer.WriteLine(JsonSerializer.Serialize(artifact, SerializerOptions));
            }
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "enochian-control-tests",
                Guid.NewGuid().ToString("N"));
            _ = Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
