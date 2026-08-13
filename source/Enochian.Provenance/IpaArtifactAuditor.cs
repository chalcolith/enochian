using Json.Schema;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IpaEncoder = Enochian.Text.Encoder;

namespace Enochian.Provenance;

public sealed class IpaArtifactAuditor(
    string artifactSchemaPath,
    string profileSchemaPath,
    IpaEncoder encoder)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };
    private readonly JsonSchema artifactSchema = LoadSchema(artifactSchemaPath);
    private readonly JsonSchema profileSchema = LoadSchema(profileSchemaPath);

    public IpaAuditResult Audit(string artifactPath, string profilePath, int sampleSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sampleSize);

        var profile = LoadProfile(profilePath);
        var issues = new List<IpaAuditIssue>();
        var accepted = new List<IpaConversionArtifact>();
        var unknownSegments = new Dictionary<string, int>(StringComparer.Ordinal);
        var totalRecords = 0;

        foreach (var sourceLine in ReadUtf8Lines(artifactPath))
        {
            totalRecords++;
            ValidateLine(sourceLine, profile, accepted, unknownSegments, issues);
        }

        var summary = new IpaAuditSummary
        {
            TotalRecords = totalRecords,
            AcceptedRecords = accepted.Count,
            RejectedRecords = totalRecords - accepted.Count,
            UnknownSegments = new SortedDictionary<string, int>(unknownSegments, StringComparer.Ordinal),
            Issues = [.. issues.OrderBy(issue => issue.Line).ThenBy(issue => issue.Code, StringComparer.Ordinal)],
        };
        return new IpaAuditResult(summary, CreateReviewRows(accepted, sampleSize));
    }

    public static void WriteSummary(string path, IpaAuditSummary summary)
    {
        WriteText(path, JsonSerializer.Serialize(summary, SerializerOptions) + "\n");
    }

    public static void WriteReviewSheet(string path, IEnumerable<IpaReviewRow> rows)
    {
        var content = string.Join("\n", rows.Select(row => JsonSerializer.Serialize(row, SerializerOptions)));
        WriteText(path, string.IsNullOrEmpty(content) ? content : content + "\n");
    }

    public static string CreateBlindedId(IpaConversionArtifact artifact)
    {
        var identity = string.Join('\u001f',
            artifact.ProviderId,
            artifact.ProviderVersion,
            artifact.ProfileId,
            artifact.ProfileVersion,
            artifact.Source,
            artifact.Language,
            artifact.RecordId,
            artifact.SourceForm);
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }

    private static JsonSchema LoadSchema(string path)
    {
        return JsonSchema.FromText(
            File.ReadAllText(path, new UTF8Encoding(false, true)),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
    }

    private IpaConversionProfile LoadProfile(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
        if (!profileSchema.Evaluate(document.RootElement).IsValid)
        {
            throw new InvalidDataException($"{path} does not conform to the IPA conversion profile schema.");
        }

        return JsonSerializer.Deserialize<IpaConversionProfile>(document.RootElement, SerializerOptions)
            ?? throw new InvalidDataException($"Unable to deserialize IPA conversion profile {path}.");
    }

    private void ValidateLine(
        IpaSourceLine sourceLine,
        IpaConversionProfile profile,
        List<IpaConversionArtifact> accepted,
        Dictionary<string, int> unknownSegments,
        List<IpaAuditIssue> issues)
    {
        if (sourceLine.Error != null)
        {
            issues.Add(new(sourceLine.Line, null, "invalid_utf8", sourceLine.Error));
            return;
        }

        try
        {
            using var document = JsonDocument.Parse(sourceLine.Text);
            var root = document.RootElement;
            var recordId = GetString(root, "record_id");
            if (!HasUniqueProperties(root))
            {
                issues.Add(new(sourceLine.Line, recordId, "duplicate_property", "record contains a duplicate property"));
                return;
            }

            if (!artifactSchema.Evaluate(root).IsValid)
            {
                issues.Add(new(sourceLine.Line, recordId, "schema", "record does not conform to the IPA conversion artifact schema"));
                return;
            }

            var artifact = JsonSerializer.Deserialize<IpaConversionArtifact>(root, SerializerOptions)
                ?? throw new JsonException("Unable to deserialize conversion artifact.");
            if (!MatchesProfile(artifact, profile))
            {
                issues.Add(new(sourceLine.Line, artifact.RecordId, "profile_mismatch",
                    "record provider, profile, or language does not match the pinned profile"));
                return;
            }

            if (!string.Equals(artifact.Status, "complete", StringComparison.Ordinal) ||
                artifact.Diagnostics.Any(diagnostic =>
                    diagnostic.Code.Contains("unconverted", StringComparison.Ordinal)))
            {
                issues.Add(new(sourceLine.Line, artifact.RecordId, "incomplete_conversion",
                    "conversion is incomplete or contains an unconverted grapheme"));
                return;
            }

            if (string.IsNullOrWhiteSpace(artifact.Ipa))
            {
                issues.Add(new(sourceLine.Line, artifact.RecordId, "empty_ipa", "IPA output is empty"));
                return;
            }

            if (!artifact.NormalizedForm.IsNormalized(NormalizationForm.FormC))
            {
                issues.Add(new(sourceLine.Line, artifact.RecordId, "normalization",
                    "normalized_form must use Unicode NFC"));
                return;
            }

            var (_, _, phones) = encoder.GetTextAndPhones(
                artifact.Ipa.Normalize(NormalizationForm.FormD),
                out var unknown);
            if (unknown.Count != 0)
            {
                foreach (var symbol in unknown)
                {
                    _ = unknownSegments.TryGetValue(symbol, out var count);
                    unknownSegments[symbol] = count + 1;
                }

                issues.Add(new(sourceLine.Line, artifact.RecordId, "unknown_ipa",
                    $"unknown IPA segment(s): {string.Join(", ", unknown)}"));
                return;
            }

            if (phones.Count == 0)
            {
                issues.Add(new(sourceLine.Line, artifact.RecordId, "empty_phonology",
                    "IPA output produced no phonological segments"));
                return;
            }

            accepted.Add(artifact);
        }
        catch (JsonException exception)
        {
            issues.Add(new(sourceLine.Line, null, "malformed_json", exception.Message));
        }
    }

    private static bool MatchesProfile(IpaConversionArtifact artifact, IpaConversionProfile profile)
    {
        return string.Equals(artifact.ProviderId, profile.ProviderId, StringComparison.Ordinal)
            && string.Equals(artifact.ProviderVersion, profile.ProviderVersion, StringComparison.Ordinal)
            && string.Equals(artifact.ProfileId, profile.ProfileId, StringComparison.Ordinal)
            && string.Equals(artifact.ProfileVersion, profile.ProfileVersion, StringComparison.Ordinal)
            && string.Equals(artifact.Language, profile.Language, StringComparison.Ordinal);
    }

    private static IReadOnlyList<IpaReviewRow> CreateReviewRows(
        List<IpaConversionArtifact> artifacts,
        int sampleSize)
    {
        if (sampleSize == 0 || artifacts.Count == 0)
        {
            return [];
        }

        var graphemeCounts = artifacts
            .SelectMany(artifact => artifact.NormalizedForm.EnumerateRunes().Select(rune => rune.ToString()))
            .GroupBy(grapheme => grapheme, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var candidates = artifacts
            .Select(artifact => new
            {
                Artifact = artifact,
                BlindedId = CreateBlindedId(artifact),
                Length = artifact.NormalizedForm.EnumerateRunes().Count(),
                Rarity = artifact.NormalizedForm.EnumerateRunes()
                    .Select(rune => graphemeCounts[rune.ToString()])
                    .DefaultIfEmpty(int.MaxValue)
                    .Min(),
            })
            .OrderBy(candidate => candidate.Rarity)
            .ThenByDescending(candidate => candidate.Length)
            .ThenBy(candidate => candidate.BlindedId, StringComparer.Ordinal)
            .Take(sampleSize)
            .OrderBy(candidate => candidate.BlindedId, StringComparer.Ordinal);

        return [.. candidates.Select(candidate => new IpaReviewRow
        {
            BlindedId = candidate.BlindedId,
            SourceForm = candidate.Artifact.SourceForm,
            NormalizedForm = candidate.Artifact.NormalizedForm,
            GeneratedIpa = candidate.Artifact.Ipa,
        })];
    }

    private static bool HasUniqueProperties(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        return root.EnumerateObject().All(property => names.Add(property.Name));
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out var value) &&
            value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }

    private static IEnumerable<IpaSourceLine> ReadUtf8Lines(string path)
    {
        var encoding = new UTF8Encoding(false, true);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var stream = new BufferedStream(file);
        var bytes = new List<byte>();
        var lineNumber = 0;

        while (stream.ReadByte() is var value && value >= 0)
        {
            if (value == '\n')
            {
                yield return DecodeLine(encoding, bytes, ++lineNumber);
                bytes.Clear();
            }
            else if (value != '\r')
            {
                bytes.Add((byte)value);
            }
        }

        if (bytes.Count != 0)
        {
            yield return DecodeLine(encoding, bytes, ++lineNumber);
        }
    }

    private static IpaSourceLine DecodeLine(UTF8Encoding encoding, List<byte> bytes, int lineNumber)
    {
        try
        {
            return new IpaSourceLine(lineNumber, encoding.GetString([.. bytes]), null);
        }
        catch (DecoderFallbackException exception)
        {
            return new IpaSourceLine(lineNumber, string.Empty, exception.Message);
        }
    }

    private static void WriteText(string path, string content)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, new UTF8Encoding(false));
    }

    private sealed record IpaSourceLine(int Line, string Text, string? Error);
}

public sealed class IpaConversionArtifact
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    public string SchemaVersion { get; init; } = string.Empty;

    public string RecordId { get; init; } = string.Empty;

    public string Source { get; init; } = string.Empty;

    public string Language { get; init; } = string.Empty;

    public string SourceForm { get; init; } = string.Empty;

    public string NormalizedForm { get; init; } = string.Empty;

    public string Ipa { get; init; } = string.Empty;

    public string ProviderId { get; init; } = string.Empty;

    public string ProviderVersion { get; init; } = string.Empty;

    public string ProfileId { get; init; } = string.Empty;

    public string ProfileVersion { get; init; } = string.Empty;

    public string Status { get; init; } = string.Empty;

    public IReadOnlyList<IpaConversionDiagnostic> Diagnostics { get; init; } = [];
}

public sealed class IpaConversionDiagnostic
{
    public string Code { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string? Text { get; init; }
}

public sealed class IpaConversionProfile
{
    public string ProviderId { get; init; } = string.Empty;

    public string ProviderVersion { get; init; } = string.Empty;

    public string ProfileId { get; init; } = string.Empty;

    public string ProfileVersion { get; init; } = string.Empty;

    public string Language { get; init; } = string.Empty;
}

public sealed record IpaAuditResult(IpaAuditSummary Summary, IReadOnlyList<IpaReviewRow> ReviewRows);

public sealed class IpaAuditSummary
{
    [JsonPropertyName("$schema")]
    public string Schema { get; init; } = "ipa-audit-summary.schema.json";

    public string SchemaVersion { get; init; } = "1.0.0";

    public int TotalRecords { get; init; }

    public int AcceptedRecords { get; init; }

    public int RejectedRecords { get; init; }

    public IReadOnlyDictionary<string, int> UnknownSegments { get; init; } =
        new SortedDictionary<string, int>(StringComparer.Ordinal);

    public IReadOnlyList<IpaAuditIssue> Issues { get; init; } = [];
}

public sealed record IpaAuditIssue(int Line, string? RecordId, string Code, string Message);

public sealed class IpaReviewRow
{
    [JsonPropertyName("$schema")]
    public string Schema { get; init; } = "ipa-review-sheet.schema.json";

    public string SchemaVersion { get; init; } = "1.0.0";

    public string BlindedId { get; init; } = string.Empty;

    public string SourceForm { get; init; } = string.Empty;

    public string NormalizedForm { get; init; } = string.Empty;

    public string GeneratedIpa { get; init; } = string.Empty;

    public string? ExpectedIpa { get; init; }

    public string Decision { get; init; } = "pending";

    public string? ErrorCategory { get; init; }

    public string? Notes { get; init; }
}
