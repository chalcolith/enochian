using Enochian.Controls;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Enochian.UnitTests;

[TestClass]
public sealed class ControlAcquirerTests
{
    [TestMethod]
    public async Task DownloadsExactBytesAndRejectsChecksumMismatch()
    {
        using var fixture = new TemporaryDirectory();
        var content = new UTF8Encoding(false).GetBytes("pinned control source\n");
        var checksum = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var manifest = CreateManifest("raw/control.dict", checksum);
        using var client = new HttpClient(new FixtureHttpHandler(content));
        var acquirer = new ControlAcquirer(client);

        var destination = await acquirer.AcquireAsync(manifest, fixture.Path);

        CollectionAssert.AreEqual(content, File.ReadAllBytes(destination));
        var invalid = manifest with { RawPath = "raw/invalid.dict", Sha256 = new string('0', 64) };
        _ = await Assert.ThrowsExactlyAsync<InvalidDataException>(() => acquirer.AcquireAsync(invalid, fixture.Path));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Path, "raw", "invalid.dict")));
    }

    [TestMethod]
    public void ExtractsPinnedArchiveShapeAndRejectsPathTraversal()
    {
        using var fixture = new TemporaryDirectory();
        var safeArchive = Path.Combine(fixture.Path, "safe.zip");
        using (var archive = ZipFile.Open(safeArchive, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("magyarispell-pinned/szotar/alap/fonev.1");
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write("csizma\n");
        }

        var dictionaryRoot = ControlAcquirer.ExtractMagyar(safeArchive, Path.Combine(fixture.Path, "safe"));

        Assert.AreEqual("csizma\n", File.ReadAllText(Path.Combine(dictionaryRoot, "alap", "fonev.1")));

        var unsafeArchive = Path.Combine(fixture.Path, "unsafe.zip");
        using (var archive = ZipFile.Open(unsafeArchive, ZipArchiveMode.Create))
        {
            _ = archive.CreateEntry("magyarispell-pinned/../../escape.txt");
        }

        _ = Assert.ThrowsExactly<InvalidDataException>(() =>
            ControlAcquirer.ExtractMagyar(unsafeArchive, Path.Combine(fixture.Path, "unsafe")));
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Path, "escape.txt")));
    }

    private static ControlManifest CreateManifest(string rawPath, string checksum)
    {
        return new(
            "fixture-control",
            "tur",
            "Turkic/Oghuz",
            "https://example.invalid/control.dict",
            "fixture-revision",
            checksum,
            "Apache-2.0",
            rawPath,
            "generated/control.jsonl");
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
                "enochian-control-acquirer-tests",
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
