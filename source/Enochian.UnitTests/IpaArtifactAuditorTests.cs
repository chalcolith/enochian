using Enochian.Provenance;
using Json.Schema;
using System.Text.Json;
using System.Text.Json.Nodes;
using IpaEncoder = Enochian.Text.Encoder;

namespace Enochian.UnitTests;

[TestClass]
public class IpaArtifactAuditorTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));

    [TestMethod]
    public void ValidArtifactRoundTripsToBlindedReviewSheet()
    {
        using var fixture = new AuditFixture();
        var artifact = LoadArtifact();
        var artifactPath = fixture.WriteArtifacts([artifact]);
        var result = CreateAuditor().Audit(artifactPath, ProfilePath, 10);

        Assert.AreEqual(1, result.Summary.TotalRecords);
        Assert.AreEqual(1, result.Summary.AcceptedRecords);
        Assert.AreEqual(0, result.Summary.RejectedRecords);
        Assert.HasCount(1, result.ReviewRows);
        Assert.AreEqual(
            "668bb51a6681a4ba6a7ba756a7949524a5b167142729427217dc43e9ff349eab",
            result.ReviewRows.Single().BlindedId);

        var reviewPath = Path.Combine(fixture.DirectoryPath, "review.jsonl");
        IpaArtifactAuditor.WriteReviewSheet(reviewPath, result.ReviewRows);
        using var review = JsonDocument.Parse(File.ReadAllText(reviewPath));
        var schema = JsonSchema.FromText(
            File.ReadAllText(GetPath("resources/lexicons/schemas/ipa-review-sheet.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });

        Assert.IsTrue(schema.Evaluate(review.RootElement).IsValid);
        Assert.IsFalse(review.RootElement.TryGetProperty("language", out _));
        Assert.IsFalse(review.RootElement.TryGetProperty("source", out _));
        Assert.IsFalse(review.RootElement.TryGetProperty("record_id", out _));
        Assert.IsFalse(review.RootElement.TryGetProperty("provider_id", out _));
    }

    [TestMethod]
    [DataRow("missing_provider_version", "schema")]
    [DataRow("incomplete", "incomplete_conversion")]
    [DataRow("unconverted_grapheme", "incomplete_conversion")]
    [DataRow("empty_ipa", "empty_ipa")]
    [DataRow("unknown_ipa", "unknown_ipa")]
    public void RejectsInvalidConversionArtifacts(string mutation, string expectedCode)
    {
        using var fixture = new AuditFixture();
        var artifact = LoadArtifact();
        ApplyMutation(artifact, mutation);
        var path = fixture.WriteArtifacts([artifact]);

        var result = CreateAuditor().Audit(path, ProfilePath, 10);

        Assert.AreEqual(0, result.Summary.AcceptedRecords);
        Assert.AreEqual(1, result.Summary.RejectedRecords);
        Assert.IsTrue(result.Summary.Issues.Any(issue => issue.Code == expectedCode),
            string.Join(Environment.NewLine, result.Summary.Issues));
    }

    [TestMethod]
    public void BlindedIdsAndRowOrderAreStableAcrossInputOrder()
    {
        using var fixture = new AuditFixture();
        var alpha = CreateArtifact("alpha", "ada");
        var beta = CreateArtifact("beta", "pat");
        var gamma = CreateArtifact("gamma", "kitap");
        var firstPath = fixture.WriteArtifacts([alpha, beta, gamma], "first.jsonl");
        var secondPath = fixture.WriteArtifacts([gamma, alpha, beta], "second.jsonl");
        var auditor = CreateAuditor();

        var first = auditor.Audit(firstPath, ProfilePath, 3);
        var second = auditor.Audit(secondPath, ProfilePath, 3);
        var firstOutput = Path.Combine(fixture.DirectoryPath, "first-review.jsonl");
        var secondOutput = Path.Combine(fixture.DirectoryPath, "second-review.jsonl");
        IpaArtifactAuditor.WriteReviewSheet(firstOutput, first.ReviewRows);
        IpaArtifactAuditor.WriteReviewSheet(secondOutput, second.ReviewRows);

        Assert.AreEqual(File.ReadAllText(firstOutput), File.ReadAllText(secondOutput));
    }

    private static string ProfilePath => GetPath(
        "resources/lexicons/examples/ipa-conversion-profile.example.json");

    private static IpaArtifactAuditor CreateAuditor()
    {
        var flow = new Flow.Flow(GetPath("samples/ipatransducer.json"));
        AssertUtils.NoErrors(flow);
        var features = flow.FeatureSets.Single(featureSet => featureSet.Id == "Default");
        var encoding = flow.Encodings.Single(candidate =>
            string.Equals(candidate.Id, "IPA", StringComparison.OrdinalIgnoreCase));
        return new IpaArtifactAuditor(
            GetPath("resources/lexicons/schemas/ipa-conversion-artifact.schema.json"),
            GetPath("resources/lexicons/schemas/ipa-conversion-profile.schema.json"),
            new IpaEncoder(features, encoding));
    }

    private static JsonObject LoadArtifact()
    {
        return JsonNode.Parse(File.ReadAllText(GetPath(
            "resources/lexicons/examples/ipa-conversion-artifact.example.json")))?.AsObject()
            ?? throw new AssertFailedException("Unable to load conversion artifact example.");
    }

    private static JsonObject CreateArtifact(string recordId, string form)
    {
        var artifact = LoadArtifact();
        artifact["record_id"] = recordId;
        artifact["source_form"] = form;
        artifact["normalized_form"] = form;
        artifact["ipa"] = form;
        return artifact;
    }

    private static void ApplyMutation(JsonObject artifact, string mutation)
    {
        switch (mutation)
        {
            case "missing_provider_version":
                _ = artifact.Remove("provider_version");
                break;
            case "incomplete":
                artifact["status"] = "incomplete";
                break;
            case "unconverted_grapheme":
                artifact["diagnostics"] = new JsonArray(new JsonObject
                {
                    ["code"] = "unconverted_grapheme",
                    ["message"] = "No mapping for grapheme.",
                    ["text"] = "x",
                });
                break;
            case "empty_ipa":
                artifact["ipa"] = string.Empty;
                break;
            case "unknown_ipa":
                artifact["ipa"] = "Б";
                break;
            default:
                throw new AssertFailedException($"Unknown mutation {mutation}.");
        }
    }

    private static string GetPath(string relativePath)
    {
        return Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }

    private sealed class AuditFixture : IDisposable
    {
        public AuditFixture()
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"enochian-ipa-audit-{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(DirectoryPath);
        }

        public string DirectoryPath { get; }

        public string WriteArtifacts(IEnumerable<JsonObject> artifacts, string fileName = "artifacts.jsonl")
        {
            var path = Path.Combine(DirectoryPath, fileName);
            var content = string.Join("\n", artifacts.Select(artifact => artifact.ToJsonString())) + "\n";
            File.WriteAllText(path, content, new System.Text.UTF8Encoding(false));
            return path;
        }

        public void Dispose()
        {
            Directory.Delete(DirectoryPath, true);
        }
    }
}
