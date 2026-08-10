using System.Text.Json;

namespace Enochian.Cdsl;

public sealed record CdslManifest(
    string SourceId,
    string DictionaryCode,
    string RepositoryUrl,
    string Revision,
    string Sha256,
    string License,
    string RawPath,
    string GeneratedArtifactPath)
{
    private static readonly HashSet<string> DictionaryCodes = new(StringComparer.Ordinal)
    {
        "ap",
        "mw",
        "pw",
        "pwg",
        "shs",
    };

    public static IReadOnlyList<CdslManifest> LoadAll(string manifestDirectory)
    {
        var manifests = Directory
            .EnumerateFiles(manifestDirectory, "cdsl-*.manifest.json", SearchOption.TopDirectoryOnly)
            .Select(Load)
            .Where(manifest => DictionaryCodes.Contains(manifest.DictionaryCode))
            .OrderBy(manifest => manifest.DictionaryCode, StringComparer.Ordinal)
            .ToArray();

        if (manifests.Length != DictionaryCodes.Count)
        {
            throw new InvalidDataException("Expected acquired CDSL manifests for AP, MW, PW, PWG, and SHS.");
        }

        var revisions = manifests.Select(manifest => manifest.Revision).Distinct(StringComparer.Ordinal).ToArray();
        if (revisions.Length != 1)
        {
            throw new InvalidDataException("All CDSL manifests must pin the same csl-orig commit.");
        }

        return manifests;
    }

    private static CdslManifest Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var sourceId = GetRequiredString(root, "source_id");
        if (!sourceId.StartsWith("cdsl-", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{path}: source_id must start with 'cdsl-'.");
        }

        if (!string.Equals(GetRequiredString(root, "status"), "acquired", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{path}: status must be acquired.");
        }

        var revision = root.GetProperty("revision");
        if (!string.Equals(GetRequiredString(revision, "kind"), "commit", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{path}: revision.kind must be commit.");
        }

        return new CdslManifest(
            sourceId,
            sourceId["cdsl-".Length..],
            GetRequiredString(root, "url"),
            GetRequiredString(revision, "value"),
            GetRequiredString(root, "sha256"),
            GetRequiredString(root, "license"),
            GetRequiredString(root, "raw_path"),
            GetRequiredString(root, "generated_artifact_path"));
    }

    private static string GetRequiredString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new InvalidDataException($"Manifest field '{propertyName}' must be a non-empty string.");
        }

        return property.GetString()!;
    }
}
