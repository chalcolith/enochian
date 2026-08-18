using Enochian.Bhsa;
using Json.Schema;
using System.IO.Compression;
using System.Text.Json;

namespace Enochian.UnitTests;

[TestClass]
public sealed class BhsaAdapterTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    private static readonly string FixturePath = Path.Combine(
        RepositoryRoot,
        "source",
        "Enochian.UnitTests",
        "Fixtures",
        "Bhsa",
        "occurrences.fixture.jsonl");

    [TestMethod]
    public void ConvertsEtcbcPhonoToAuditableSegmentalIpa()
    {
        var conversion = EtcbcPhonoConverter.Convert("*[bᵊ-rāˈšîṯ.] ḡḏḵḥṭṣśyʸᵃᵉᵒₐêôû");

        Assert.AreEqual("bəraːʃiːθɣðxħtˤsˤsjjaeoaeːoːuː", conversion.Ipa);
        Assert.HasCount(0, conversion.UnknownSymbols);
        Assert.IsTrue(conversion.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "etcbc_phono_source" && diagnostic.Text?.StartsWith('*') == true));
        Assert.IsTrue(conversion.Diagnostics.Any(diagnostic => diagnostic.Code == "removed_structural_marker"));
        Assert.IsTrue(conversion.Diagnostics.Any(diagnostic => diagnostic.Code == "removed_nonphonetic_stress"));
        Assert.IsTrue(conversion.Diagnostics.Any(diagnostic => diagnostic.Code == "normalized_reduced_vowel"));
    }

    [TestMethod]
    public void RejectsAmbiguousEtcbcShinSymbol()
    {
        var conversion = EtcbcPhonoConverter.Convert("ŝ");

        AssertUtils.SequenceEquals(["ŝ"], conversion.UnknownSymbols);
        Assert.IsTrue(conversion.Diagnostics.Any(diagnostic =>
            diagnostic.Code == "unconverted_grapheme" && diagnostic.Text == "ŝ"));
    }

    [TestMethod]
    public void AggregatesFrequencyAndPreservesMultipleReadings()
    {
        var result = BhsaOccurrenceAdapter.Parse(FixturePath);

        Assert.AreEqual(8, result.Occurrences);
        Assert.HasCount(2, result.Lexemes);
        var beginning = result.Lexemes.Single(lexeme => lexeme.LexemeId == "l1");
        Assert.AreEqual(2, beginning.Frequency);
        Assert.AreEqual("בְּרֵאשִׁית", beginning.VocalizedForm);
        Assert.AreEqual("bəreʃit", beginning.Readings.Single().Ipa);
        Assert.AreEqual(2, beginning.Readings.Single().Frequency);
        Assert.IsTrue(beginning.VocalizedForm.Contains('\u05b0'));
        Assert.IsTrue(beginning.VocalizedForm.Contains('\u05bc'));
        var call = result.Lexemes.Single(lexeme => lexeme.LexemeId == "l2");
        AssertUtils.SequenceEquals(["qara", "qora"], call.Readings.Select(reading => reading.Ipa));
        Assert.AreEqual(2, call.Readings[0].Frequency);
        Assert.AreEqual(1, call.Readings[1].Frequency);
        Assert.IsTrue(result.Rejections.Any(rejection => rejection.Category == "aramaic"));
        Assert.IsTrue(result.Rejections.Any(rejection => rejection.Category == "missing_phono"));
        Assert.IsTrue(result.Rejections.Any(rejection => rejection.Category == "frequency_mismatch"));
    }

    [TestMethod]
    public void WritesDeterministicBiblicalHebrewArtifactsAndReviewRows()
    {
        using var fixture = new TemporaryDirectory();
        var first = Path.Combine(fixture.Path, "first");
        var second = Path.Combine(fixture.Path, "second");
        var pipeline = new BhsaPipeline(RepositoryRoot);

        var report = pipeline.Normalize(FixturePath, first, sampleSize: 3);
        _ = pipeline.Normalize(FixturePath, second, sampleSize: 3);

        Assert.AreEqual("Biblical Hebrew", report.CorpusLabel);
        Assert.AreEqual(2, report.UniqueLexemes);
        Assert.AreEqual(2, report.EmittedLexemes);
        Assert.AreEqual(3, report.ConversionRecords);
        Assert.AreEqual(1, report.MultipleReadingLexemes);
        Assert.AreEqual(3, report.ReviewRecords);
        Assert.IsFalse(report.ConfirmatoryEligible);
        Assert.AreEqual("pending_blinded_review", report.EligibilityBlockers.Single());
        AssertArtifacts(first);
        foreach (var file in Directory.EnumerateFiles(first).Select(Path.GetFileName).Order(StringComparer.Ordinal))
        {
            CollectionAssert.AreEqual(
                File.ReadAllBytes(Path.Combine(first, file!)),
                File.ReadAllBytes(Path.Combine(second, file!)),
                file);
        }
    }

    [TestMethod]
    public void MissingOptionalDataIsStatusAndUnsafeArchivesAreRejected()
    {
        using var fixture = new TemporaryDirectory();

        var status = BhsaSnapshot.Inspect(fixture.Path);

        Assert.AreEqual("not-installed", status.State);
        Assert.IsFalse(status.IsReady);
        StringAssert.Contains(status.Message, "default build and tests remain available");

        var archivePath = Path.Combine(fixture.Path, "unsafe.zip");
        using (var archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            _ = archive.CreateEntry("../escape.txt");
        }

        _ = Assert.ThrowsExactly<InvalidDataException>(() =>
            BhsaSnapshot.ExtractSafely(archivePath, Path.Combine(fixture.Path, "output")));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Path, "escape.txt")));
    }

    [TestMethod]
    public void NonCommercialBhsaCannotEnterDefaultBundleOrPublishContent()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "resources",
            "lexicons",
            "manifests",
            "bhsa.manifest.json")));
        var root = manifest.RootElement;
        Assert.AreEqual("CC-BY-NC-4.0", root.GetProperty("license").GetString());
        Assert.AreEqual("metadata-only", root.GetProperty("distribution_policy").GetString());
        Assert.AreEqual("non-commercial", root.GetProperty("usage_policy").GetString());
        Assert.IsTrue(root.GetProperty("optional").GetBoolean());
        Assert.IsFalse(root.GetProperty("default_bundle").GetBoolean());

        var project = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "source",
            "Enochian.Bhsa",
            "Enochian.Bhsa.csproj"));
        Assert.IsFalse(project.Contains(".enoch", StringComparison.Ordinal));
        Assert.IsFalse(project.Contains("Content Include", StringComparison.Ordinal));
    }

    private static void AssertArtifacts(string outputDirectory)
    {
        var normalizedSchema = JsonSchema.FromText(File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "resources",
            "lexicons",
            "schemas",
            "normalized-entry.schema.json")), new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        var entries = File.ReadLines(Path.Combine(outputDirectory, "bhsa.jsonl"))
            .Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            Assert.HasCount(2, entries);
            Assert.IsTrue(entries.All(entry => normalizedSchema.Evaluate(entry.RootElement).IsValid));
            Assert.IsTrue(entries.All(entry =>
                entry.RootElement.GetProperty("dialect").GetString() == "Biblical Hebrew"));
            Assert.AreEqual("qara", entries.Single(entry =>
                entry.RootElement.GetProperty("source_record_id").GetString() == "l2")
                .RootElement.GetProperty("ipa").GetString());
            var call = entries.Single(entry =>
                entry.RootElement.GetProperty("source_record_id").GetString() == "l2").RootElement;
            Assert.AreEqual(3, call.GetProperty("frequency").GetInt32());
            Assert.AreEqual(2, call.GetProperty("rank").GetInt32());
        }
        finally
        {
            foreach (var entry in entries)
            {
                entry.Dispose();
            }
        }

        var conversions = File.ReadLines(Path.Combine(outputDirectory, "bhsa.conversions.jsonl"))
            .Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            Assert.IsTrue(conversions.All(conversion => conversion.RootElement
                .GetProperty("diagnostics").EnumerateArray().Any(diagnostic =>
                    diagnostic.GetProperty("message").GetString() == "Biblical Hebrew")));
        }
        finally
        {
            foreach (var conversion in conversions)
            {
                conversion.Dispose();
            }
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "enochian-bhsa-tests",
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
