using Enochian.Lexicons;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Enochian.UnitTests.Lexicons;

[TestClass]
[DoNotParallelize]
public sealed class NormalizedLexiconTests
{
    [TestMethod]
    public void EncodesPrecomposedNasalVowelsFromNfcInterchangeRecords()
    {
        using var environment = new NormalizedLexiconTestEnvironment();
        var sourcePath = environment.CreateSource("nfc-nasal-vowel.jsonl");
        var record = CreateRecord(0);
        record["ipa"] = "ĩ";
        File.WriteAllText(sourcePath, record.ToJsonString() + "\n", new UTF8Encoding(false));

        var lexicon = environment.Load(sourcePath);

        Assert.AreEqual(1, lexicon.Entries.Count);
        Assert.AreEqual("ĩ", lexicon.Entries.Single().Ipa);
        Assert.AreEqual(0, lexicon.QualityReport?.UnknownSymbols.Count);
    }

    private static readonly string[] ExpectedRejectionReasons =
    [
        "duplicate_entry_id",
        "empty_phonology",
        "malformed_json",
        "missing_field",
        "unknown_ipa",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    [TestMethod]
    public void LoadsValidRecordsAndReportsEveryRejectionDeterministically()
    {
        using var environment = new NormalizedLexiconTestEnvironment();
        var sourcePath = environment.CopyFixture("normalized-mixed.jsonl");

        var lexicon = environment.Load(sourcePath);
        var entries = lexicon.Entries;

        Assert.AreEqual(2, entries.Count);
        Assert.AreEqual(2, lexicon.EntriesByLemma["café"].Count);
        Assert.IsFalse(entries.First().Text.IsNormalized(NormalizationForm.FormC));
        Assert.IsTrue(entries.First().Lemma.IsNormalized(NormalizationForm.FormC));
        AssertUtils.SequenceEquals(["d", "t"], entries.Select(entry => entry.Ipa).Order(StringComparer.Ordinal));

        var report = lexicon.QualityReport ?? throw new AssertFailedException("Quality report was not created.");
        Assert.AreEqual(7, report.TotalRecords);
        Assert.AreEqual(2, report.AcceptedRecords);
        Assert.AreEqual(5, report.RejectedRecords);
        Assert.AreEqual(1, report.UniqueLemmas);
        Assert.AreEqual(1, report.UniqueForms);
        Assert.AreEqual(2, report.UniquePhonologies);
        Assert.AreEqual(1, report.DuplicateLemmas);
        Assert.AreEqual(1, report.DuplicateForms);
        Assert.AreEqual(0, report.DuplicatePhonologies);
        CollectionAssert.AreEquivalent(
            ExpectedRejectionReasons,
            report.RejectionReasons.Keys.ToArray());
        Assert.AreEqual(1, report.UnknownSymbols["☃"]);
        AssertUtils.SequenceEquals([3, 4, 5, 6, 7], report.Rejections.Select(rejection => rejection.Line));
        Assert.IsTrue(report.Rejections.Take(4).All(rejection => rejection.SourceId == "fixture"));
        Assert.AreEqual("normalized-test", report.Rejections.Last().SourceId);
        AssertUtils.NoErrors(lexicon);

        var firstReport = File.ReadAllBytes(environment.ReportPath);
        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(1));
        var reloaded = environment.Load(sourcePath);
        Assert.AreEqual(2, reloaded.Entries.Count);
        CollectionAssert.AreEqual(firstReport, File.ReadAllBytes(environment.ReportPath));

        var cached = environment.Load(sourcePath);
        Assert.AreEqual(2, cached.Entries.Count);
        Assert.AreEqual(7, cached.QualityReport?.TotalRecords);
        CollectionAssert.AreEqual(firstReport, File.ReadAllBytes(environment.ReportPath));
    }

    [TestMethod]
    public void ReportsInvalidUtf8WithSourceAndLine()
    {
        using var environment = new NormalizedLexiconTestEnvironment();
        var sourcePath = environment.CreateSource("invalid-utf8.jsonl");
        var validRecord = Encoding.UTF8.GetBytes(CreateRecord(1).ToJsonString() + "\n");
        File.WriteAllBytes(sourcePath, [.. validRecord, 0x7B, 0x22, 0x78, 0x22, 0x3A, 0xFF, 0x7D, 0x0A, .. validRecord]);

        var lexicon = environment.Load(sourcePath);
        _ = lexicon.Entries;

        var report = lexicon.QualityReport ?? throw new AssertFailedException("Quality report was not created.");
        Assert.AreEqual(3, report.TotalRecords);
        Assert.AreEqual(1, report.AcceptedRecords);
        Assert.AreEqual(2, report.RejectedRecords);
        Assert.AreEqual(1, report.RejectionReasons["invalid_unicode"]);
        Assert.AreEqual(1, report.RejectionReasons["duplicate_entry_id"]);
        var invalidUnicode = report.Rejections.Single(rejection => rejection.ReasonCode == "invalid_unicode");
        Assert.AreEqual("normalized-test", invalidUnicode.SourceId);
        Assert.AreEqual(2, invalidUnicode.Line);
    }

    [TestMethod]
    public void StreamsLargeJsonLinesInput()
    {
        using var environment = new NormalizedLexiconTestEnvironment();
        var sourcePath = environment.CreateSource("large.jsonl");
        using (var writer = new StreamWriter(sourcePath, false, new UTF8Encoding(false)))
        {
            for (var index = 0; index < 10_000; index++)
            {
                writer.WriteLine(CreateRecord(index).ToJsonString());
            }
        }

        var lexicon = environment.Load(sourcePath);

        Assert.AreEqual(10_000, lexicon.Entries.Count);
        Assert.AreEqual(10_000, lexicon.QualityReport?.AcceptedRecords);
        Assert.AreEqual(0, lexicon.QualityReport?.RejectedRecords);
    }

    private static JsonObject CreateRecord(int index)
    {
        return new JsonObject
        {
            ["schema_version"] = "1.0.0",
            ["entry_id"] = $"fixture:eng:{index}",
            ["source_record_id"] = index.ToString(CultureInfo.InvariantCulture),
            ["language"] = "eng",
            ["family"] = "Indo-European",
            ["source"] = "fixture",
            ["source_version"] = "1.0.0",
            ["lemma"] = "word" + index.ToString(CultureInfo.InvariantCulture),
            ["original_form"] = "word" + index.ToString(CultureInfo.InvariantCulture),
            ["form"] = "word" + index.ToString(CultureInfo.InvariantCulture),
            ["entry_kind"] = "lemma",
            ["dialect"] = null,
            ["part_of_speech"] = null,
            ["definition"] = null,
            ["frequency"] = null,
            ["source_encoding"] = "IPA",
            ["ipa"] = "t",
            ["unicode_normalization"] = "NFC",
            ["license"] = "CC-BY-4.0",
        };
    }

    private sealed class NormalizedLexiconTestEnvironment : IDisposable
    {
        private static readonly string RepositoryRoot = Path.GetFullPath(
            Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        private readonly string root = Path.Combine(Path.GetTempPath(), "enochian-tests", Guid.NewGuid().ToString("N"));
        private readonly string sourcePrefix = "m102-" + Guid.NewGuid().ToString("N");

        public NormalizedLexiconTestEnvironment()
        {
            _ = Directory.CreateDirectory(root);
            ManifestPath = Path.Combine(root, "manifest.json");
            ReportPath = Path.Combine(root, "quality-report.json");
            File.WriteAllText(ManifestPath, "{}", new UTF8Encoding(false));
        }

        public string ManifestPath { get; }

        public string ReportPath { get; }

        public string CopyFixture(string fileName)
        {
            var source = Path.Combine(RepositoryRoot, "source", "Enochian.UnitTests", "Fixtures", "Lexicons", fileName);
            var destination = CreateSource(fileName);
            File.Copy(source, destination, true);
            return destination;
        }

        public string CreateSource(string fileName)
        {
            return Path.Combine(root, sourcePrefix + "-" + fileName);
        }

        public NormalizedLexicon Load(string sourcePath)
        {
            var configPath = Path.Combine(root, "flow.json");
            var config = new JsonObject
            {
                ["id"] = "Normalized Lexicon Test",
                ["features"] = new JsonArray
                {
                    new JsonObject { ["path"] = Path.Combine(RepositoryRoot, "resources", "encodings", "features.json") },
                },
                ["encodings"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["features"] = "Default",
                        ["path"] = Path.Combine(RepositoryRoot, "resources", "encodings", "ipa.json"),
                    },
                },
                ["lexicons"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["id"] = "normalized-test",
                        ["type"] = "NormalizedLexicon",
                        ["features"] = "Default",
                        ["encoding"] = "IPA",
                        ["path"] = sourcePath,
                        ["manifest"] = ManifestPath,
                        ["qualityReport"] = ReportPath,
                    },
                },
                ["steps"] = new JsonArray(),
            };
            File.WriteAllText(configPath, config.ToJsonString(JsonOptions), new UTF8Encoding(false));

            var flow = new Enochian.Flow.Flow(configPath);
            AssertUtils.NoErrors(flow);
            return flow.Lexicons.OfType<NormalizedLexicon>().Single();
        }

        public void Dispose()
        {
            var cacheDirectory = Path.GetFullPath(Path.Combine(".", Configurable.CacheDir));
            if (Directory.Exists(cacheDirectory))
            {
                foreach (var cachePath in Directory.EnumerateFiles(cacheDirectory, sourcePrefix + "*.bin"))
                {
                    File.Delete(cachePath);
                }
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}
