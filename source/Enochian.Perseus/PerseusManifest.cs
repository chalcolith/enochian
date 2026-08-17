using System.Text.Json;

namespace Enochian.Perseus;

public sealed record PerseusManifest(
    string SourceId,
    string SourceUrl,
    string Revision,
    string Sha256,
    string License,
    string RawPath,
    string GeneratedArtifactPath)
{
    public static PerseusManifest Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        if (!string.Equals(GetRequiredString(root, "source_id"), "perseus-lewis-short", StringComparison.Ordinal)
            || !string.Equals(GetRequiredString(root, "status"), "acquired", StringComparison.Ordinal)
            || !string.Equals(GetRequiredString(root, "language"), "lat", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{path}: expected the acquired Latin perseus-lewis-short component.");
        }

        var revision = root.GetProperty("revision");
        if (!string.Equals(GetRequiredString(revision, "kind"), "commit", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{path}: revision.kind must be commit.");
        }

        return new PerseusManifest(
            GetRequiredString(root, "source_id"),
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