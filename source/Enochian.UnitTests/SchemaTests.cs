using Json.Schema;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Enochian.UnitTests;

[TestClass]
public class SchemaTests
{
    private static readonly string RepositoryRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "../../../../.."));
    private static readonly JsonSchema ExperimentSchema = LoadSchema("experiments/schemas/experiment.schema.json");
    private static readonly JsonSchema SourceManifestSchema =
        LoadSchema("resources/lexicons/schemas/source-manifest.schema.json");
    private static readonly JsonSchema NormalizedEntrySchema =
        LoadSchema("resources/lexicons/schemas/normalized-entry.schema.json");
    private static readonly JsonSchema IpaConversionArtifactSchema =
        LoadSchema("resources/lexicons/schemas/ipa-conversion-artifact.schema.json");
    private static readonly JsonSchema IpaConversionRequestSchema =
        LoadSchema("resources/lexicons/schemas/ipa-conversion-request.schema.json");
    private static readonly JsonSchema IpaConversionProfileSchema =
        LoadSchema("resources/lexicons/schemas/ipa-conversion-profile.schema.json");
    private static readonly JsonSchema IpaReviewSheetSchema =
        LoadSchema("resources/lexicons/schemas/ipa-review-sheet.schema.json");
    private static readonly JsonSchema IpaAuditSummarySchema =
        LoadSchema("resources/lexicons/schemas/ipa-audit-summary.schema.json");

    [TestMethod]
    public void ValidExamplesConformToSchemas()
    {
        AssertValid(ExperimentSchema, "experiments/exploratory.example.json");
        AssertValid(ExperimentSchema, "experiments/confirmatory.protocol.json");
        AssertValid(SourceManifestSchema, "resources/lexicons/examples/source-manifest.example.json");
        AssertValid(NormalizedEntrySchema, "resources/lexicons/examples/normalized-entry.example.json");
        AssertValid(IpaConversionArtifactSchema,
            "resources/lexicons/examples/ipa-conversion-artifact.example.json");
        AssertValid(IpaConversionRequestSchema,
            "resources/lexicons/examples/ipa-conversion-request.example.json");
        AssertValid(IpaConversionProfileSchema,
            "resources/lexicons/examples/ipa-conversion-profile.example.json");
        AssertValid(IpaConversionProfileSchema,
            "resources/lexicons/examples/ipa-conversion-profile-custom.example.json");
        AssertValid(SourceManifestSchema,
            "resources/lexicons/manifests/perseus-lewis-short.manifest.json");
        AssertValid(IpaConversionProfileSchema,
            "resources/lexicons/profiles/latin-classical-restored.profile.json");
        AssertValid(IpaReviewSheetSchema,
            "resources/lexicons/examples/ipa-review-sheet.example.json");
        AssertValid(IpaAuditSummarySchema,
            "resources/lexicons/examples/ipa-audit-summary.example.json");
    }

    [TestMethod]
    [DataRow("missing-version.json")]
    [DataRow("missing-checksum.json")]
    [DataRow("unknown-entry-kind.json")]
    [DataRow("invalid-language-code.json")]
    [DataRow("unspecified-random-seed.json")]
    [DataRow("overlapping-partitions.json")]
    [DataRow("unknown-major-version.json")]
    public void InvalidFixturesAreRejected(string fixtureName)
    {
        var (schemaName, instance) = LoadInvalidFixture(fixtureName);

        Assert.IsFalse(IsValid(schemaName, instance), $"{fixtureName} unexpectedly passed validation.");
    }

    [TestMethod]
    public void UnknownMajorVersionReportsSupportedVersion()
    {
        var (_, instance) = LoadInvalidFixture("unknown-major-version.json");

        var error = GetVersionError(instance);

        Assert.AreEqual("Unsupported schema_version major 2; supported major is 1.", error);
    }

    [TestMethod]
    public void ExampleDocumentsRoundTripWithoutFieldLoss()
    {
        var paths = new[]
        {
            "experiments/exploratory.example.json",
            "experiments/confirmatory.protocol.json",
            "resources/lexicons/examples/source-manifest.example.json",
            "resources/lexicons/examples/normalized-entry.example.json",
            "resources/lexicons/examples/ipa-conversion-artifact.example.json",
            "resources/lexicons/examples/ipa-conversion-request.example.json",
            "resources/lexicons/examples/ipa-conversion-profile.example.json",
            "resources/lexicons/examples/ipa-conversion-profile-custom.example.json",
            "resources/lexicons/examples/ipa-review-sheet.example.json",
            "resources/lexicons/examples/ipa-audit-summary.example.json",
        };

        foreach (var path in paths)
        {
            var original = LoadObject(path);
            var roundTripped = JsonNode.Parse(original.ToJsonString())?.AsObject();

            Assert.IsNotNull(roundTripped, $"Unable to deserialize round-tripped {path}.");
            Assert.IsTrue(JsonNode.DeepEquals(original, roundTripped), $"Round trip changed fields in {path}.");
        }
    }

    [TestMethod]
    public void NormalizedEntryUsesNfcAndDeterministicId()
    {
        var entry = LoadObject("resources/lexicons/examples/normalized-entry.example.json");
        var stringFields = new[] { "lemma", "original_form", "form", "ipa" };

        foreach (var field in stringFields)
        {
            var value = entry[field]?.GetValue<string>()
                ?? throw new AssertFailedException($"Missing {field}.");
            Assert.IsTrue(value.IsNormalized(NormalizationForm.FormC), $"{field} is not NFC.");
        }

        var source = entry["source"]?.GetValue<string>();
        var language = entry["language"]?.GetValue<string>();
        var sourceRecordId = entry["source_record_id"]?.GetValue<string>();
        var expectedId = $"{source}:{language}:{Uri.EscapeDataString(sourceRecordId ?? string.Empty)}";
        Assert.AreEqual(expectedId, entry["entry_id"]?.GetValue<string>());
    }

    private static void AssertValid(JsonSchema schema, string instancePath)
    {
        using var instance = JsonDocument.Parse(File.ReadAllText(GetPath(instancePath)));
        var result = schema.Evaluate(instance.RootElement,
            new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.IsTrue(result.IsValid, $"{instancePath} does not conform to its schema.");
    }

    private static bool IsValid(string schemaName, JsonObject instance)
    {
        var schema = schemaName switch
        {
            "experiment" => ExperimentSchema,
            "source-manifest" => SourceManifestSchema,
            "normalized-entry" => NormalizedEntrySchema,
            _ => throw new AssertFailedException($"Unknown fixture schema {schemaName}."),
        };
        using var document = JsonDocument.Parse(instance.ToJsonString());
        var schemaResult = schema.Evaluate(document.RootElement);
        if (!schemaResult.IsValid || GetVersionError(instance) != null)
        {
            return false;
        }

        return schemaName != "experiment" || HasDisjointPartitions(instance);
    }

    private static string? GetVersionError(JsonObject instance)
    {
        if (instance["schema_version"] is not JsonValue versionNode ||
            !versionNode.TryGetValue<string>(out var version))
        {
            return "schema_version is required.";
        }

        var majorText = version.Split('.', 2)[0];
        return int.TryParse(majorText, out var major) && major == 1
            ? null
            : $"Unsupported schema_version major {majorText}; supported major is 1.";
    }

    private static bool HasDisjointPartitions(JsonObject instance)
    {
        var split = instance["corpus_split"]?.AsObject()
            ?? throw new AssertFailedException("Experiment has no corpus_split.");
        var evaluation = split["evaluation_partition"]?["loci"]?.AsArray()
            .Select(node => node?.GetValue<string>())
            .Where(value => value != null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal) ?? [];
        var holdout = split["holdout_partition"]?["loci"]?.AsArray()
            .Select(node => node?.GetValue<string>())
            .Where(value => value != null)
            .Cast<string>() ?? [];

        return !holdout.Any(evaluation.Contains);
    }

    private static (string SchemaName, JsonObject Instance) LoadInvalidFixture(string fixtureName)
    {
        var fixture = LoadObject($"source/Enochian.UnitTests/Fixtures/Schemas/{fixtureName}");
        var schemaName = fixture["schema"]?.GetValue<string>()
            ?? throw new AssertFailedException($"Fixture {fixtureName} has no schema.");
        var basePath = fixture["base"]?.GetValue<string>()
            ?? throw new AssertFailedException($"Fixture {fixtureName} has no base.");
        var patch = fixture["patch"]?.AsObject()
            ?? throw new AssertFailedException($"Fixture {fixtureName} has no patch.");
        var instance = LoadObject(basePath);

        ApplyMergePatch(instance, patch);
        return (schemaName, instance);
    }

    private static void ApplyMergePatch(JsonObject target, JsonObject patch)
    {
        foreach (var property in patch)
        {
            if (property.Value == null)
            {
                _ = target.Remove(property.Key);
            }
            else if (property.Value is JsonObject objectPatch)
            {
                if (target[property.Key] is not JsonObject child)
                {
                    child = [];
                    target[property.Key] = child;
                }

                ApplyMergePatch(child, objectPatch);
            }
            else
            {
                target[property.Key] = property.Value.DeepClone();
            }
        }
    }

    private static JsonSchema LoadSchema(string schemaPath)
    {
        return JsonSchema.FromText(
            File.ReadAllText(GetPath(schemaPath)),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
    }

    private static JsonObject LoadObject(string relativePath)
    {
        return JsonNode.Parse(File.ReadAllText(GetPath(relativePath)))?.AsObject()
            ?? throw new AssertFailedException($"Unable to parse {relativePath}.");
    }

    private static string GetPath(string relativePath)
    {
        return Path.Combine(RepositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
    }
}
