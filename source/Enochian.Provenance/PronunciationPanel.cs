using Json.Schema;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Enochian.Provenance;

public sealed record PronunciationProfile
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string Language { get; init; }
    public required string Convention { get; init; }
    public required string[] RequiredLabels { get; init; }
    public required string[] RejectedLabels { get; init; }
    public required string Fallback { get; init; }
    public required SortedDictionary<string, string> Rules { get; init; }

    [JsonIgnore]
    public string ArtifactStem => Id + "-v" + Version;

    [JsonIgnore]
    public string Fingerprint => PronunciationPolicyEvaluator.StableId(
        JsonSerializer.Serialize(this, PronunciationAudit.JsonOptions));
}

public sealed record PronunciationPanel
{
    public required string SchemaVersion { get; init; }
    public required string PolicyId { get; init; }
    public required bool Frozen { get; init; }
    public required string DictionaryProtocol { get; init; }
    public required string DictionaryProtocolSha256 { get; init; }
    public required string OutputDirectory { get; init; }
    public required string ReportDirectory { get; init; }
    public required string[] FallbackSourceManifests { get; init; }
    public required PronunciationProfile[] Profiles { get; init; }

    public static PronunciationPanel Load(string path, string repositoryRoot)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
        var schema = JsonSchema.FromText(File.ReadAllText(Path.Combine(
            repositoryRoot, "resources", "lexicons", "schemas", "pronunciation-panel.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        if (!schema.Evaluate(document.RootElement).IsValid)
        {
            throw new InvalidDataException($"{path}: invalid pronunciation panel.");
        }

        var panel = JsonSerializer.Deserialize<PronunciationPanel>(document.RootElement, PronunciationAudit.JsonOptions)
            ?? throw new InvalidDataException($"{path}: empty pronunciation panel.");
        if (panel.Profiles.Select(profile => profile.Language).Distinct(StringComparer.Ordinal).Count() != panel.Profiles.Length ||
            panel.Profiles.Select(profile => profile.Id).Distinct(StringComparer.Ordinal).Count() != panel.Profiles.Length)
        {
            throw new InvalidDataException($"{path}: profile languages and IDs must be unique.");
        }

        foreach (var profile in panel.Profiles)
        {
            if (profile.RequiredLabels.Intersect(profile.RejectedLabels, StringComparer.OrdinalIgnoreCase).Any() ||
                (profile.Fallback == "classical-arabic-vocalized-v1" && profile.Language != "ara"))
            {
                throw new InvalidDataException($"{path}: inconsistent profile {profile.Id}.");
            }
        }

        return panel with
        {
            Profiles = [.. panel.Profiles.Select(profile => profile with
            {
                Rules = new SortedDictionary<string, string>(profile.Rules, StringComparer.Ordinal),
            })],
        };
    }
}
