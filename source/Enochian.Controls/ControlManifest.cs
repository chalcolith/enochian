using System.Text.Json;

namespace Enochian.Controls;

public sealed record ControlManifest(
    string SourceId,
    string Language,
    string Family,
    string Url,
    string Revision,
    string Sha256,
    string License,
    string RawPath,
    string GeneratedArtifactPath)
{
    public static ControlManifest Load(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var revision = root.GetProperty("revision");
        return new(
            Get(root, "source_id"),
            Get(root, "language"),
            Get(root, "family"),
            Get(root, "url"),
            Get(revision, "value"),
            Get(root, "sha256"),
            Get(root, "license"),
            Get(root, "raw_path"),
            Get(root, "generated_artifact_path"));
    }

    private static string Get(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value.GetString())
                ? value.GetString()!
                : throw new InvalidDataException($"Manifest field '{name}' must be a non-empty string.");
    }
}
