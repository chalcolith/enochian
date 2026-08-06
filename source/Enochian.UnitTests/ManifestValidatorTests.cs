using Enochian.Provenance;
using System.Text.Json.Nodes;

namespace Enochian.UnitTests;

[TestClass]
public class ManifestValidatorTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    private static readonly string SchemaPath = GetPath("resources/lexicons/schemas/source-manifest.schema.json");

    [TestMethod]
    public void RejectsChecksumMismatch()
    {
        using var fixture = new ManifestFixture();
        var rawPath = Path.Combine(fixture.DirectoryPath, "raw.txt");
        File.WriteAllText(rawPath, "local content");
        var manifest = ManifestFixture.CreateManifest("checksum-mismatch");
        manifest["status"] = "acquired";
        manifest["retrieval_date"] = "2026-08-06";
        manifest["raw_path"] = Path.GetRelativePath(RepositoryRoot, rawPath).Replace('\\', '/');
        manifest["sha256"] = new string('0', 64);
        var path = fixture.Write("checksum-mismatch.manifest.json", manifest);

        var report = CreateValidator().Validate([path]);

        AssertHasIssue(report, "sha256");
    }

    [TestMethod]
    public void RejectsDuplicateSourceId()
    {
        using var fixture = new ManifestFixture();
        var first = fixture.Write("first.manifest.json", ManifestFixture.CreateManifest("duplicate"));
        var second = fixture.Write("second.manifest.json", ManifestFixture.CreateManifest("duplicate"));

        var report = CreateValidator().Validate([first, second]);

        AssertHasIssue(report, "source_id");
    }

    [TestMethod]
    [DataRow("main")]
    [DataRow("master")]
    public void RejectsFloatingRevision(string revision)
    {
        using var fixture = new ManifestFixture();
        var manifest = ManifestFixture.CreateManifest("floating-revision");
        manifest["revision"]!["value"] = revision;
        var path = fixture.Write("floating.manifest.json", manifest);

        var report = CreateValidator().Validate([path]);

        AssertHasIssue(report, "revision.value");
    }

    [TestMethod]
    public void RejectsAbsentLicense()
    {
        using var fixture = new ManifestFixture();
        var manifest = ManifestFixture.CreateManifest("absent-license");
        _ = manifest.Remove("license");
        var path = fixture.Write("absent-license.manifest.json", manifest);

        var report = CreateValidator().Validate([path]);

        AssertHasIssue(report, "license");
    }

    [TestMethod]
    public void RejectsNonCommercialDefaultBundle()
    {
        using var fixture = new ManifestFixture();
        var manifest = ManifestFixture.CreateManifest("non-commercial-bundle");
        manifest["license"] = "CC-BY-NC-4.0";
        manifest["usage_policy"] = "non-commercial";
        manifest["optional"] = false;
        manifest["default_bundle"] = true;
        var path = fixture.Write("non-commercial.manifest.json", manifest);

        var report = CreateValidator().Validate([path]);

        AssertHasIssue(report, "default_bundle");
    }

    [TestMethod]
    public void GeneratesStableAttributionReport()
    {
        using var fixture = new ManifestFixture();
        var beta = fixture.Write("beta.manifest.json", ManifestFixture.CreateManifest("beta"));
        var alpha = fixture.Write("alpha.manifest.json", ManifestFixture.CreateManifest("alpha"));

        var first = CreateValidator().GenerateAttribution([beta, alpha]);
        var second = CreateValidator().GenerateAttribution([alpha, beta]);

        Assert.AreEqual(first, second);
        Assert.IsTrue(first.IndexOf("## alpha", StringComparison.Ordinal) <
            first.IndexOf("## beta", StringComparison.Ordinal));
        Assert.IsTrue(first.Contains("Example Owner", StringComparison.Ordinal));
        Assert.IsTrue(first.Contains("CC-BY-4.0 (verified)", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ValidatesAllCheckedInManifestsOffline()
    {
        var directory = GetPath("resources/lexicons/manifests");
        var paths = ManifestValidator.FindManifests(directory);

        var report = CreateValidator().Validate(paths);

        Assert.IsTrue(paths.Count > 0, "No checked-in manifests were found.");
        Assert.IsTrue(report.IsValid, string.Join(Environment.NewLine, report.Issues));
    }

    [TestMethod]
    public void CheckedInAttributionMatchesManifestData()
    {
        var directory = GetPath("resources/lexicons/manifests");
        var paths = ManifestValidator.FindManifests(directory);
        var expected = File.ReadAllText(GetPath("resources/lexicons/ATTRIBUTION.md"))
            .ReplaceLineEndings("\n");

        var actual = CreateValidator().GenerateAttribution(paths);

        Assert.AreEqual(expected, actual);
    }

    private static ManifestValidator CreateValidator()
    {
        return new(RepositoryRoot, SchemaPath);
    }

    private static void AssertHasIssue(ManifestValidationReport report, string field)
    {
        Assert.IsTrue(report.Issues.Any(issue => issue.Field == field),
            $"Expected an issue for {field}:{Environment.NewLine}{string.Join(Environment.NewLine, report.Issues)}");
    }

    private static string GetPath(string relativePath)
    {
        return Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private sealed class ManifestFixture : IDisposable
    {
        public ManifestFixture()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"enochian-manifests-{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public static JsonObject CreateManifest(string sourceId)
        {
            return new JsonObject
            {
                ["$schema"] = "source-manifest.schema.json",
                ["schema_version"] = "1.0.0",
                ["source_id"] = sourceId,
                ["status"] = "planned",
                ["owner"] = "Example Owner",
                ["language"] = "eng",
                ["family"] = "Indo-European/Germanic",
                ["url"] = "https://example.org/source",
                ["revision"] = new JsonObject { ["kind"] = "tag", ["value"] = "v1.0.0" },
                ["retrieval_date"] = null,
                ["sha256"] = null,
                ["license"] = "CC-BY-4.0",
                ["license_status"] = "verified",
                ["distribution_policy"] = "metadata-only",
                ["usage_policy"] = "unrestricted",
                ["optional"] = false,
                ["default_bundle"] = false,
                ["citation"] = "Example citation.",
                ["parser"] = new JsonObject { ["id"] = "planned", ["version"] = "1.0.0" },
                ["raw_path"] = null,
                ["generated_artifact_path"] = null,
                ["filters"] = new JsonObject
                {
                    ["include"] = new JsonArray("documented records"),
                    ["exclude"] = new JsonArray("none"),
                },
            };
        }

        public string Write(string fileName, JsonObject manifest)
        {
            var path = Path.Combine(DirectoryPath, fileName);
            File.WriteAllText(path, manifest.ToJsonString(new() { WriteIndented = true }));
            return path;
        }

        public void Dispose()
        {
            Directory.Delete(DirectoryPath, true);
        }
    }
}
