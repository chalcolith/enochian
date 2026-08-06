using Json.Schema;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Enochian.Provenance;

public sealed record ManifestValidationIssue(string ManifestPath, string Field, string Message)
{
    public override string ToString()
    {
        return $"{ManifestPath}: {Field}: {Message}";
    }
}

public sealed class ManifestValidationReport
{
    public IList<ManifestValidationIssue> Issues { get; } = [];

    public bool IsValid => Issues.Count == 0;
}

public sealed class ManifestValidator(string repositoryRoot, string schemaPath)
{
    private static readonly Regex Sha256Pattern = new("^[a-f0-9]{64}$", RegexOptions.Compiled);
    private static readonly HashSet<string> AllowedLicenses =
    [
        "Apache-2.0",
        "BSD-2-Clause",
        "CC-BY-4.0",
        "CC-BY-NC-4.0",
        "CC-BY-SA-3.0",
        "CC-BY-SA-4.0",
        "GPL-2.0-or-later",
        "LGPL-2.1-or-later",
        "MIT",
        "MPL-1.1",
        "NOASSERTION",
    ];
    private static readonly HashSet<string> FloatingRevisions =
        new(StringComparer.OrdinalIgnoreCase) { "head", "latest", "main", "master" };

    private readonly string repositoryRoot = Path.GetFullPath(repositoryRoot);
    private readonly JsonSchema schema = JsonSchema.FromText(File.ReadAllText(schemaPath),
        new BuildOptions { SchemaRegistry = new SchemaRegistry() });

    public ManifestValidationReport Validate(IEnumerable<string> manifestPaths)
    {
        var report = new ManifestValidationReport();
        var sourceIds = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var manifestPath in manifestPaths.Order(StringComparer.Ordinal))
        {
            var manifest = LoadManifest(manifestPath, report);
            if (manifest == null)
            {
                continue;
            }

            ValidateSchema(manifestPath, manifest, report);
            ValidateSourceId(manifestPath, manifest, sourceIds, report);
            ValidateUrl(manifestPath, manifest, report);
            ValidateRevision(manifestPath, manifest, report);
            ValidateLicense(manifestPath, manifest, report);
            ValidateDistribution(manifestPath, manifest, report);
            ValidateChecksum(manifestPath, manifest, report);
        }

        return report;
    }

    public string GenerateAttribution(IEnumerable<string> manifestPaths)
    {
        var paths = manifestPaths.Order(StringComparer.Ordinal).ToArray();
        var report = Validate(paths);
        if (!report.IsValid)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, report.Issues));
        }

        var manifests = paths
            .Select(LoadObject)
            .OrderBy(manifest => GetString(manifest, "source_id"), StringComparer.Ordinal);
        var lines = new List<string>
        {
            "# Lexicon Source Attribution",
            string.Empty,
            "This report is generated solely from checked-in source manifests.",
        };

        foreach (var manifest in manifests)
        {
            lines.Add(string.Empty);
            lines.Add($"## {GetString(manifest, "source_id")}");
            lines.Add(string.Empty);
            lines.Add($"- Owner: {GetString(manifest, "owner")}");
            lines.Add($"- Upstream: <{GetString(manifest, "url")}>");
            lines.Add($"- License: {GetString(manifest, "license")} ({GetString(manifest, "license_status")})");
            lines.Add($"- Distribution: {GetString(manifest, "distribution_policy")}");
            lines.Add($"- Citation: {GetString(manifest, "citation")}");
        }

        return string.Join("\n", lines) + "\n";
    }

    public static IReadOnlyList<string> FindManifests(string directory)
    {
        return [.. Directory.GetFiles(directory, "*.manifest.json", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)];
    }

    private static JsonObject? LoadManifest(
        string manifestPath,
        ManifestValidationReport report)
    {
        try
        {
            return LoadObject(manifestPath);
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            report.Issues.Add(new(manifestPath, "$", exception.Message));
            return null;
        }
    }

    private static JsonObject LoadObject(string path)
    {
        return JsonNode.Parse(File.ReadAllText(path))?.AsObject()
            ?? throw new JsonException("Manifest root must be an object.");
    }

    private void ValidateSchema(string path, JsonObject manifest, ManifestValidationReport report)
    {
        using var document = JsonDocument.Parse(manifest.ToJsonString());
        var result = schema.Evaluate(document.RootElement);
        if (!result.IsValid)
        {
            report.Issues.Add(new(path, "$schema", "does not conform to source-manifest schema version 1"));
        }
    }

    private static void ValidateSourceId(
        string path,
        JsonObject manifest,
        Dictionary<string, string> sourceIds,
        ManifestValidationReport report)
    {
        var sourceId = GetString(manifest, "source_id");
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            report.Issues.Add(new(path, "source_id", "is required"));
        }
        else if (sourceIds.TryGetValue(sourceId, out var existingPath))
        {
            report.Issues.Add(new(path, "source_id", $"duplicates {existingPath}"));
        }
        else
        {
            sourceIds.Add(sourceId, path);
        }
    }

    private static void ValidateUrl(string path, JsonObject manifest, ManifestValidationReport report)
    {
        var url = GetString(manifest, "url");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            report.Issues.Add(new(path, "url", "must be an absolute HTTP or HTTPS URL"));
        }
    }

    private static void ValidateRevision(string path, JsonObject manifest, ManifestValidationReport report)
    {
        var revision = manifest["revision"] is JsonObject revisionObject ? revisionObject : null;
        var kind = revision == null ? null : GetString(revision, "kind");
        var value = revision == null ? null : GetString(revision, "value");
        var status = GetString(manifest, "status");
        if (string.IsNullOrWhiteSpace(value) || FloatingRevisions.Contains(value) ||
            (kind == "unresolved" && status != "planned"))
        {
            report.Issues.Add(new(path, "revision.value", "must pin a commit, tag, or release and cannot be floating"));
        }
    }

    private static void ValidateLicense(string path, JsonObject manifest, ManifestValidationReport report)
    {
        var license = GetString(manifest, "license");
        if (string.IsNullOrWhiteSpace(license))
        {
            report.Issues.Add(new(path, "license", "is required"));
        }
        else if (!AllowedLicenses.Contains(license))
        {
            report.Issues.Add(new(path, "license", $"'{license}' is not an allowed expression"));
        }
    }

    private static void ValidateDistribution(string path, JsonObject manifest, ManifestValidationReport report)
    {
        var defaultBundle = manifest["default_bundle"]?.GetValue<bool>() == true;
        var licenseStatus = GetString(manifest, "license_status");
        var usagePolicy = GetString(manifest, "usage_policy");
        if (defaultBundle && licenseStatus != "verified")
        {
            report.Issues.Add(new(path, "default_bundle", "cannot be true when license_status is unverified"));
        }

        if (defaultBundle && usagePolicy == "non-commercial")
        {
            report.Issues.Add(new(path, "default_bundle", "cannot bundle a non-commercial source by default"));
        }
    }

    private void ValidateChecksum(string path, JsonObject manifest, ManifestValidationReport report)
    {
        var checksum = GetString(manifest, "sha256");
        if (checksum != null && !Sha256Pattern.IsMatch(checksum))
        {
            report.Issues.Add(new(path, "sha256", "must be 64 lowercase hexadecimal characters"));
            return;
        }

        var rawPath = GetString(manifest, "raw_path");
        if (rawPath == null)
        {
            return;
        }

        var absolutePath = Path.GetFullPath(Path.Combine(repositoryRoot,
            rawPath.Replace('/', Path.DirectorySeparatorChar)));
        if (!File.Exists(absolutePath) || checksum == null)
        {
            return;
        }

        using var stream = File.OpenRead(absolutePath);
        var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(checksum, actual, StringComparison.Ordinal))
        {
            report.Issues.Add(new(path, "sha256", $"does not match local file {rawPath}"));
        }
    }

    private static string? GetString(JsonObject manifest, string propertyName)
    {
        return manifest[propertyName] is JsonValue value && value.TryGetValue<string>(out var text)
            ? text
            : null;
    }
}
