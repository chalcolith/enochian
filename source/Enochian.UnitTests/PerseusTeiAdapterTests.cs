using Enochian.Perseus;
using Json.Schema;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Enochian.UnitTests;

[TestClass]
public sealed class PerseusTeiAdapterTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    [TestMethod]
    public void ParsesNestedSensesHomographsVariantsAndGreekText()
    {
        var records = PerseusTeiAdapter.Parse(GetFixturePath("lewis-short.fixture.xml"));

        Assert.HasCount(4, records);
        AssertUtils.SequenceEquals(["n1", "n2", "n3", "n4"], records.Select(record => record.RecordId));
        Assert.AreEqual("to love to be fond of φιλῶ", records[0].Definition);
        Assert.AreEqual("mălum", records[1].NormalizedForm);
        Assert.AreEqual("mălum", records[2].NormalizedForm);
        Assert.AreEqual("ablūdŭoe", records[3].NormalizedForm);
        Assert.AreEqual("v.", records[3].PartOfSpeech);
    }

    [TestMethod]
    [DataRow("Caesar", "kaesar", 1)]
    [DataRow("quī", "kwiː", 0)]
    [DataRow("philosophia", "pʰilosopʰia", 5)]
    [DataRow("Iūlius", "juːlius", 2)]
    [DataRow("vīnum", "wiːnum", 1)]
    [DataRow("axis", "aksis", 2)]
    [DataRow("poena", "poena", 1)]
    [DataRow("thesaurus", "tʰesaurus", 2)]
    [DataRow("charta", "kʰarta", 2)]
    public void AppliesRestoredClassicalRules(string source, string expectedIpa, int assumedShortVowels)
    {
        var result = ClassicalLatinConverter.Convert(source);

        Assert.IsTrue(result.IsComplete);
        Assert.AreEqual(expectedIpa, result.Ipa);
        Assert.AreEqual(assumedShortVowels, result.AssumedShortVowels);
        Assert.HasCount(0, result.UnknownGraphemes);
    }

    [TestMethod]
    public void ReportsUnknownGraphemes()
    {
        var result = ClassicalLatinConverter.Convert("rosa?");

        Assert.IsFalse(result.IsComplete);
        AssertUtils.SequenceEquals(["?"], result.UnknownGraphemes);
    }

    [TestMethod]
    public async Task AcquirerDownloadsPinnedContentAndRejectsChecksumMismatch()
    {
        using var fixture = new TemporaryDirectory();
        var content = new UTF8Encoding(false).GetBytes("pinned TEI\n");
        var checksum = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var manifest = new PerseusManifest(
            "perseus-lewis-short",
            "https://example.test/lewis-short.xml",
            "40038e40937fa639639802e73dac15e6c938496b",
            checksum,
            "CC-BY-SA-4.0",
            "raw/lewis-short.xml",
            "generated/lewis-short.jsonl");
        using var client = new HttpClient(new FixtureHttpHandler(content));
        var acquirer = new PerseusAcquirer(client);

        await acquirer.AcquireAsync(manifest, fixture.Path);

        var destination = Path.Combine(fixture.Path, "raw", "lewis-short.xml");
        CollectionAssert.AreEqual(content, File.ReadAllBytes(destination));
        var invalid = manifest with { RawPath = "raw/invalid.xml", Sha256 = new string('0', 64) };
        _ = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => acquirer.AcquireAsync(invalid, fixture.Path));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Path, "raw", "invalid.xml")));
    }

    [TestMethod]
    public void PipelineWritesDeterministicNormalizedAuditAndReviewArtifacts()
    {
        using var fixture = new TemporaryDirectory();
        var first = CreateOutputPaths(fixture.Path, "first");
        var second = CreateOutputPaths(fixture.Path, "second");
        var pipeline = new PerseusPipeline(RepositoryRoot);

        var report = pipeline.Normalize(
            GetFixturePath("lewis-short.fixture.xml"),
            first.Normalized,
            first.Conversions,
            first.Quality,
            first.Audit,
            first.Review,
            sampleSize: 4);
        _ = pipeline.Normalize(
            GetFixturePath("lewis-short.fixture.xml"),
            second.Normalized,
            second.Conversions,
            second.Quality,
            second.Audit,
            second.Review,
            sampleSize: 4);

        Assert.AreEqual(4, report.ParsedRecords);
        Assert.AreEqual(4, report.EmittedRecords);
        Assert.AreEqual(0, report.RejectedRecords);
        Assert.AreEqual(4, report.ReviewRecords);
        Assert.AreEqual("Restored Classical Latin", report.PronunciationConvention);
        Assert.IsTrue(report.AssumedShortRecords > 0);
        AssertDeterministic(first, second);

        var normalizedSchema = LoadSchema("resources/lexicons/schemas/normalized-entry.schema.json");
        var entries = File.ReadLines(first.Normalized).Select(line => JsonDocument.Parse(line)).ToArray();
        try
        {
            Assert.HasCount(4, entries);
            Assert.IsTrue(entries.All(entry => normalizedSchema.Evaluate(entry.RootElement).IsValid));
            Assert.AreEqual(2, entries.Count(entry =>
                entry.RootElement.GetProperty("lemma").GetString() == "mălum"));
            var homographIpa = entries
                .Where(entry => entry.RootElement.GetProperty("lemma").GetString() == "mălum")
                .Select(entry => entry.RootElement.GetProperty("ipa").GetString())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            Assert.HasCount(1, homographIpa, "Definitions and parts of speech must not affect conversion.");
        }
        finally
        {
            foreach (var entry in entries)
            {
                entry.Dispose();
            }
        }

        using var audit = JsonDocument.Parse(File.ReadAllText(first.Audit));
        Assert.AreEqual(4, audit.RootElement.GetProperty("accepted_records").GetInt32());
        Assert.AreEqual(0, audit.RootElement.GetProperty("rejected_records").GetInt32());
        Assert.AreEqual(4, File.ReadLines(first.Review).Count());
    }

    private static void AssertDeterministic(LatinTestPaths first, LatinTestPaths second)
    {
        Assert.AreEqual(File.ReadAllText(first.Normalized), File.ReadAllText(second.Normalized));
        Assert.AreEqual(File.ReadAllText(first.Conversions), File.ReadAllText(second.Conversions));
        Assert.AreEqual(File.ReadAllText(first.Quality), File.ReadAllText(second.Quality));
        Assert.AreEqual(File.ReadAllText(first.Audit), File.ReadAllText(second.Audit));
        Assert.AreEqual(File.ReadAllText(first.Review), File.ReadAllText(second.Review));
    }

    private static JsonSchema LoadSchema(string relativePath)
    {
        return JsonSchema.FromText(
            File.ReadAllText(Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
    }

    private static LatinTestPaths CreateOutputPaths(string root, string prefix)
    {
        return new LatinTestPaths(
            Path.Combine(root, prefix + ".jsonl"),
            Path.Combine(root, prefix + ".conversions.jsonl"),
            Path.Combine(root, prefix + ".quality.json"),
            Path.Combine(root, prefix + ".audit.json"),
            Path.Combine(root, prefix + ".review.jsonl"));
    }

    private static string GetFixturePath(string fileName)
    {
        return Path.Combine(
            RepositoryRoot,
            "source",
            "Enochian.UnitTests",
            "Fixtures",
            "Perseus",
            fileName);
    }

    private sealed record LatinTestPaths(
        string Normalized,
        string Conversions,
        string Quality,
        string Audit,
        string Review);

    private sealed class FixtureHttpHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content),
                RequestMessage = request,
            });
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "enochian-perseus-tests",
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
