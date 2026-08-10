using Enochian.Cdsl;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Enochian.UnitTests;

[TestClass]
public sealed class CdslAdapterTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    [TestMethod]
    [DataRow("ap", "akza eye note", 1)]
    [DataRow("mw", "agni ¦ m. fire", 2)]
    [DataRow("pw", "guru heavy", 1)]
    [DataRow("pwg", "veda knowledge", 1)]
    [DataRow("shs", "rAjan king", 1)]
    public void NormalizesDictionarySpecificMarkup(
        string dictionaryCode,
        string expectedDefinition,
        int expectedRecords)
    {
        using var fixture = new TemporaryDirectory();
        var manifest = CreateManifest(dictionaryCode);
        var outputPath = Path.Combine(fixture.Path, "output.jsonl");
        var reportPath = Path.Combine(fixture.Path, "quality.json");

        var report = CreateAdapter().Normalize(
            manifest,
            GetFixturePath($"{dictionaryCode}.txt"),
            outputPath,
            reportPath,
            "fixture command");

        Assert.AreEqual(expectedRecords, report.EmittedRecords);
        Assert.AreEqual(0, report.RejectedRecords);
        using var entry = JsonDocument.Parse(File.ReadLines(outputPath).First());
        Assert.AreEqual(expectedDefinition, entry.RootElement.GetProperty("definition").GetString());
        Assert.AreEqual(19, entry.RootElement.EnumerateObject().Count());
        Assert.AreEqual("1.0.0", entry.RootElement.GetProperty("schema_version").GetString());
        Assert.AreEqual("san", entry.RootElement.GetProperty("language").GetString());
        Assert.AreEqual("SLP1", entry.RootElement.GetProperty("source_encoding").GetString());
        Assert.AreEqual("NFC", entry.RootElement.GetProperty("unicode_normalization").GetString());
        Assert.AreEqual(JsonValueKind.Null, entry.RootElement.GetProperty("dialect").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, entry.RootElement.GetProperty("part_of_speech").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, entry.RootElement.GetProperty("frequency").ValueKind);
    }

    [TestMethod]
    public void PreservesHomographsAndWritesDeterministically()
    {
        using var fixture = new TemporaryDirectory();
        var manifest = CreateManifest("mw");
        var firstOutput = Path.Combine(fixture.Path, "first.jsonl");
        var secondOutput = Path.Combine(fixture.Path, "second.jsonl");
        var firstReport = Path.Combine(fixture.Path, "first.quality.json");
        var secondReport = Path.Combine(fixture.Path, "second.quality.json");
        var adapter = CreateAdapter();

        var report = adapter.Normalize(manifest, GetFixturePath("mw.txt"), firstOutput, firstReport, "fixture command");
        _ = adapter.Normalize(manifest, GetFixturePath("mw.txt"), secondOutput, secondReport, "fixture command");

        Assert.AreEqual(2, report.EmittedRecords);
        Assert.AreEqual(File.ReadAllText(firstOutput), File.ReadAllText(secondOutput));
        Assert.AreEqual(File.ReadAllText(firstReport), File.ReadAllText(secondReport));
        var lines = File.ReadAllLines(firstOutput);
        Assert.AreEqual(2, lines.Length);
        Assert.IsTrue(lines[0].Contains("cdsl-mw:san:1", StringComparison.Ordinal));
        Assert.IsTrue(lines[1].Contains("cdsl-mw:san:1.1", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ReportsMalformedBoundariesAndUnknownSlp1()
    {
        using var fixture = new TemporaryDirectory();
        var inputPath = Path.Combine(fixture.Path, "invalid.txt");
        File.WriteAllText(
            inputPath,
            "<L>1<k1>agni<k2>agni\nvalid\n<LEND>\n<LEND>\n<L>2<k1>?<k2>unknown\nunknown\n<LEND>\n<L>3<k1>guru<k2>guru\nincomplete\n",
            new UTF8Encoding(false));

        var report = CreateAdapter().Normalize(
            CreateManifest("mw"),
            inputPath,
            Path.Combine(fixture.Path, "output.jsonl"),
            Path.Combine(fixture.Path, "quality.json"),
            "fixture command");

        Assert.AreEqual(1, report.EmittedRecords);
        Assert.AreEqual(3, report.RejectedRecords);
        Assert.AreEqual(1, report.RejectionReasons["orphan_lend"]);
        Assert.AreEqual(1, report.RejectionReasons["unknown_slp1"]);
        Assert.AreEqual(1, report.RejectionReasons["incomplete_record"]);
        Assert.AreEqual(1, report.UnknownSlp1Symbols["?"]);
    }

    [TestMethod]
    public void ShsSlp1ConversionMatchesExistingEncoder()
    {
        var flow = LoadFlow();
        var features = flow.FeatureSets.Single(featureSet => featureSet.Id == "Default");
        var slp1 = flow.Encodings.Single(encoding => encoding.Id == "SLP1");
        var encoder = new Enochian.Text.Encoder(features, slp1);
        using var fixture = new TemporaryDirectory();
        var outputPath = Path.Combine(fixture.Path, "shs.jsonl");

        _ = new CdslOrigAdapter(features, slp1).Normalize(
            CreateManifest("shs"),
            GetFixturePath("shs.txt"),
            outputPath,
            Path.Combine(fixture.Path, "quality.json"),
            "fixture command");

        (_, var expectedDisplay, _) = encoder.GetTextAndPhones("rAjan", out var unknown);
        using var entry = JsonDocument.Parse(File.ReadAllText(outputPath));
        Assert.AreEqual(0, unknown.Count);
        Assert.AreEqual(expectedDisplay, entry.RootElement.GetProperty("form").GetString());
    }

    [TestMethod]
    public async Task AcquirerDownloadsPinnedContentAndRejectsChecksumMismatch()
    {
        using var fixture = new TemporaryDirectory();
        var content = new UTF8Encoding(false).GetBytes("pinned content\n");
        var checksum = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var manifest = CreateManifest("mw") with
        {
            RawPath = Path.Combine("raw", "mw.txt").Replace('\\', '/'),
            Sha256 = checksum,
        };
        using var client = new HttpClient(new FixtureHttpHandler(content));
        var acquirer = new CdslAcquirer(client);

        await acquirer.AcquireAsync(manifest, fixture.Path);

        var destination = Path.Combine(fixture.Path, "raw", "mw.txt");
        CollectionAssert.AreEqual(content, File.ReadAllBytes(destination));
        var invalid = manifest with { RawPath = "raw/invalid.txt", Sha256 = new string('0', 64) };
        _ = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => acquirer.AcquireAsync(invalid, fixture.Path));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Path, "raw", "invalid.txt")));
    }

    private static CdslOrigAdapter CreateAdapter()
    {
        var flow = LoadFlow();
        return new CdslOrigAdapter(
            flow.FeatureSets.Single(featureSet => featureSet.Id == "Default"),
            flow.Encodings.Single(encoding => encoding.Id == "SLP1"));
    }

    private static Enochian.Flow.Flow LoadFlow()
    {
        var flow = new Enochian.Flow.Flow(GetRepositoryPath("resources/lexicons/cdsl-normalization.flow.json"));
        Assert.AreEqual(0, flow.Errors.Count());
        return flow;
    }

    private static CdslManifest CreateManifest(string dictionaryCode)
    {
        return new CdslManifest(
            $"cdsl-{dictionaryCode}",
            dictionaryCode,
            "https://github.com/sanskrit-lexicon/csl-orig",
            "b7297b97cf9f7112277ea98f7969291eb1d5f495",
            new string('0', 64),
            "CC-BY-SA-4.0",
            $"raw/{dictionaryCode}.txt",
            $"generated/{dictionaryCode}.jsonl");
    }

    private static string GetFixturePath(string filename)
    {
        return GetRepositoryPath($"source/Enochian.UnitTests/Fixtures/Cdsl/{filename}");
    }

    private static string GetRepositoryPath(string relativePath)
    {
        return Path.GetFullPath(Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }

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
                $"enochian-cdsl-{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
