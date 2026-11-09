using Enochian.Provenance;
using Json.Schema;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Enochian.Benchmark;

public sealed record PinnedInput(string Path, string Sha256);

public sealed record AutomatedPhonology(PinnedInput Flow, PinnedInput Features, PinnedInput Encoding);

public sealed record AutomatedSource
{
    public required string ProfileId { get; init; }
    public required string Language { get; init; }
    public required string Family { get; init; }
    public required bool Required { get; init; }
    public required bool ConfirmatoryEligible { get; init; }
    public required PinnedInput ProfileManifest { get; init; }
    public PinnedInput? References { get; init; }
}

public sealed record AutomatedContrast(
    string Id, bool Primary, bool Required, string[] TargetGroups, string[] ControlGroups);

public sealed record AutomatedAstronomy
{
    public required PinnedInput Registration { get; init; }
    public required PinnedInput Policy { get; init; }
    public required PinnedInput Manifest { get; init; }
    public required string Status { get; init; }
    public required int MinimumSharedObjects { get; init; }
    public required string Balancing { get; init; }
    public required string Contexts { get; init; }
    public required string[] Nulls { get; init; }
    public required string Multiplicity { get; init; }
}

public sealed record AutomatedProtocolAmendment
{
    [JsonPropertyName("$schema")]
    public required string Schema { get; init; }
    public required string SchemaVersion { get; init; }
    public required string AmendmentId { get; init; }
    public required string ConfigId { get; init; }
    public required bool Frozen { get; init; }
    public required PinnedInput BaseProtocol { get; init; }
    public required PinnedInput Queries { get; init; }
    public required PinnedInput Mapping { get; init; }
    public required PinnedInput MappingApproval { get; init; }
    public required string OutputDirectory { get; init; }
    public required string AdmissionReport { get; init; }
    public required string PreflightReport { get; init; }
}

public sealed record AutomatedProtocol
{
    [JsonPropertyName("$schema")]
    public required string Schema { get; init; }
    public required string SchemaVersion { get; init; }
    public required string ConfigId { get; init; }
    public required string Phase { get; init; }
    public required bool Frozen { get; init; }
    public required AutomatedPhonology Phonology { get; init; }
    public required PinnedInput Panel { get; init; }
    public required PinnedInput RetrievalPolicy { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RetrievalRunner { get; init; }
    public required PinnedInput[] Degradations { get; init; }
    public required AutomatedSource[] Sources { get; init; }
    public required SortedDictionary<string, string[]> Groups { get; init; }
    public required AutomatedContrast[] Contrasts { get; init; }
    public required SortedDictionary<string, int> LengthQuotas { get; init; }
    public required int SamplingSeed { get; init; }
    public required int NullSeed { get; init; }
    public required int PermutationSeed { get; init; }
    public required int Repetitions { get; init; }
    public required int NullRepetitions { get; init; }
    public required int PermutationCount { get; init; }
    public required int BootstrapCount { get; init; }
    public required double ConfidenceLevel { get; init; }
    public required double Alpha { get; init; }
    public required string CalibrationNull { get; init; }
    public required string[] EvaluationLoci { get; init; }
    public required string[] HoldoutLoci { get; init; }
    public required PinnedInput? Queries { get; init; }
    public required PinnedInput? Mapping { get; init; }
    public PinnedInput? MappingApproval { get; init; }
    public required AutomatedAstronomy Astronomy { get; init; }
    public required string OutputDirectory { get; init; }
    public required string AdmissionReport { get; init; }
    public string? PreflightReport { get; init; }
    [JsonIgnore]
    public PinnedInput? BaseProtocol { get; init; }

    public static AutomatedProtocol Load(string path, string repositoryRoot)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
        if (document.RootElement.TryGetProperty("base_protocol", out _))
        {
            return LoadAmendment(path, repositoryRoot, document.RootElement);
        }

        var schemaPath = Path.Combine(repositoryRoot, "experiments", "schemas", "automated-experiment.v2.schema.json");
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        if (!schema.Evaluate(document.RootElement).IsValid)
        {
            throw new InvalidDataException($"{path}: invalid automated experiment v2 protocol.");
        }

        var protocol = JsonSerializer.Deserialize<AutomatedProtocol>(document.RootElement, JsonOptions)
            ?? throw new InvalidDataException("Empty automated experiment protocol.");
        var sourceLanguages = protocol.Sources.Select(source => source.Language).ToHashSet(StringComparer.Ordinal);
        if (sourceLanguages.Count != protocol.Sources.Length ||
            protocol.Sources.Select(source => source.ProfileId).Distinct(StringComparer.Ordinal).Count() != protocol.Sources.Length ||
            protocol.Contrasts.Select(contrast => contrast.Id).Distinct(StringComparer.Ordinal).Count() != protocol.Contrasts.Length ||
            protocol.Contrasts.Count(contrast => contrast.Primary) != 1 ||
            protocol.Contrasts.Any(contrast => contrast.Primary && !contrast.Required) ||
            protocol.EvaluationLoci.Intersect(protocol.HoldoutLoci, StringComparer.Ordinal).Any() ||
            protocol.Groups.Values.SelectMany(value => value).Any(language => !sourceLanguages.Contains(language)))
        {
            throw new InvalidDataException("Duplicate identities, invalid groups/primary contrast, or overlapping holdout in v2 protocol.");
        }

        foreach (var contrast in protocol.Contrasts)
        {
            if (contrast.TargetGroups.Concat(contrast.ControlGroups).Any(group => !protocol.Groups.ContainsKey(group)) ||
                Languages(contrast.TargetGroups, protocol.Groups).Overlaps(Languages(contrast.ControlGroups, protocol.Groups)))
            {
                throw new InvalidDataException($"Invalid or overlapping groups in contrast {contrast.Id}.");
            }
        }

        return protocol;
    }

    private static AutomatedProtocol LoadAmendment(
        string path,
        string repositoryRoot,
        JsonElement root)
    {
        var schemaPath = Path.Combine(
            repositoryRoot, "experiments", "schemas", "automated-experiment-amendment.v2.schema.json");
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        if (!schema.Evaluate(root).IsValid)
        {
            throw new InvalidDataException($"{path}: invalid automated experiment v2 amendment.");
        }

        var amendment = JsonSerializer.Deserialize<AutomatedProtocolAmendment>(root, JsonOptions)
            ?? throw new InvalidDataException("Empty automated experiment amendment.");
        var basePath = Resolve(amendment.BaseProtocol.Path, repositoryRoot);
        if (PronunciationAudit.HashConfiguration(basePath) != amendment.BaseProtocol.Sha256)
        {
            throw new InvalidDataException("pinned_input_hash_mismatch:" + amendment.BaseProtocol.Path);
        }

        var protocol = Load(basePath, repositoryRoot);
        if (protocol.Phase != "confirmatory" || protocol.Queries != null || protocol.Mapping != null ||
            protocol.MappingApproval != null || protocol.PreflightReport != null)
        {
            throw new InvalidDataException(
                "The readiness amendment must extend an unmaterialized confirmatory protocol.");
        }

        return protocol with
        {
            ConfigId = amendment.ConfigId,
            Queries = amendment.Queries,
            Mapping = amendment.Mapping,
            MappingApproval = amendment.MappingApproval,
            OutputDirectory = amendment.OutputDirectory,
            AdmissionReport = amendment.AdmissionReport,
            PreflightReport = amendment.PreflightReport,
            BaseProtocol = amendment.BaseProtocol,
        };
    }

    private static string Resolve(string path, string repositoryRoot)
    {
        if (Path.IsPathRooted(path))
        {
            throw new InvalidDataException("V2 paths must be repository-relative.");
        }

        var resolved = Path.GetFullPath(Path.Combine(
            repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(repositoryRoot, resolved);
        if (relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException("V2 paths must remain inside the repository.");
        }

        return resolved;
    }

    public static HashSet<string> Languages(IEnumerable<string> groups, IReadOnlyDictionary<string, string[]> memberships) =>
        groups.SelectMany(group => memberships[group]).ToHashSet(StringComparer.Ordinal);

    public static bool IsRevised(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.GetProperty("schema_version").GetString() == "2.0.0";
    }

    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };
}

public sealed record AutomatedReferenceSource(PinnedInput Manifest, string[] Dependencies);

public sealed record AutomatedReferenceSet
{
    public required string SchemaVersion { get; init; }
    public required string ProfileSha256 { get; init; }
    public required string GeneratorRevision { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Generator { get; init; }
    public required AutomatedReferenceSource[] ReferenceSources { get; init; }
    public required PronunciationReference[] Records { get; init; }
}

public sealed record EvidenceReading(string EntryId, string Ipa, double Weight, IReadOnlyList<double[]> Phones);

public sealed record EvidenceUnit(
    string Id, string Language, string Lemma, string LengthBand,
    EvidenceReading[] Readings, SamplingSourceMembership[] Memberships);

public sealed record AutomatedRetrievalCell(
    string DegradationId, string LengthBand, string CandidateMode, int LexicalQueries,
    MetricSummary Metrics, bool Passed, string[] Blockers);

public sealed record AutomatedSourceDecision(
    string ProfileId, string Language, string Family, bool Required, string Status,
    int CompatibleLexicalUnits, int SupportedLexicalUnits, int UniquePhonologicalUnits,
    IReadOnlyDictionary<string, int> LengthBands, string PronunciationGate,
    PronunciationAgreement? ReferenceAgreement, string RetrievalGate,
    AutomatedRetrievalCell[] Retrieval, string[] Blockers);

public sealed record AutomatedAdmissionReport(
    string SchemaVersion, string RunnerVersion, string ValidatorSha256, string ConfigId, string ProtocolSha256, bool ReadyForEvaluation,
    bool HumanAnnotationRequired, AutomatedSourceDecision[] Sources, string[] Blockers);

public sealed record AutomatedAdmissionResult(
    AutomatedProtocol Protocol, AutomatedAdmissionReport Report,
    IReadOnlyDictionary<string, EvidenceUnit[]> Units, HashedArtifact[] Inputs, string[] ProtectedPaths);
