using Enochian.Provenance;
using Enochian.Text;
using Json.Schema;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Enochian.Benchmark;

public sealed record MappingPartitions(
    string[] EvaluationLoci, string[] HoldoutLoci, bool HoldoutOpened);

public sealed record MappingNormalization(
    string UnicodeNormalization, string CaseTreatment, string UncertainSymbols);

public sealed record MappingSegmentation(
    string Method, string[] Multigraphs, string OverlapResolution);

public sealed record MappingAmbiguity(string Input, string Treatment, string Rationale);

public sealed record MappingCandidate
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required string Kind { get; init; }
    public required string Description { get; init; }
    public PinnedInput? Source { get; init; }
    public required string LinguisticRationale { get; init; }
    public required string[] DifferencesFromLegacy { get; init; }
    public required MappingNormalization Normalization { get; init; }
    public required MappingSegmentation Segmentation { get; init; }
    public required MappingAmbiguity[] TranscriptionAmbiguities { get; init; }
    public required SortedDictionary<string, string>? SymbolToIpa { get; init; }
    public required string? MaterializedMapping { get; init; }
}

public sealed record MappingDecision
{
    public required string Status { get; init; }
    public required string Researcher { get; init; }
    public required DateOnly DecidedOn { get; init; }
    public required string Rationale { get; init; }
    public required string? CandidateId { get; init; }
    public required string? MappingSha256 { get; init; }
}

public sealed record MappingOutputs(string ReportJson, string ReportMarkdown);

public sealed record MappingPreregistrationConfig
{
    [JsonPropertyName("$schema")]
    public required string Schema { get; init; }
    public required string SchemaVersion { get; init; }
    public required string PreregistrationId { get; init; }
    public required string Status { get; init; }
    public required bool ComparativeResultsInspected { get; init; }
    public required MappingPartitions Partitions { get; init; }
    public required PinnedInput Features { get; init; }
    public required PinnedInput IpaEncoding { get; init; }
    public required PinnedInput? Inventory { get; init; }
    public required MappingCandidate[] Candidates { get; init; }
    public required string? ProposalCandidateId { get; init; }
    public required MappingDecision? Decision { get; init; }
    public required MappingOutputs Outputs { get; init; }
}

public sealed record MappingInventorySymbol(string Symbol, int TokenCount, string[] Loci);

public sealed record MappingSymbolInventory
{
    public required string SchemaVersion { get; init; }
    public required string InventoryId { get; init; }
    public required PinnedInput Transcription { get; init; }
    public required string[] EvaluationLoci { get; init; }
    public required bool HoldoutOpened { get; init; }
    public required MappingInventorySymbol[] Symbols { get; init; }
}

public sealed record MappingCandidateReport(
    string Id, string Version, string Kind, string Description, PinnedInput? Source, string LinguisticRationale,
    string[] DifferencesFromLegacy, MappingNormalization Normalization, MappingSegmentation Segmentation,
    MappingAmbiguity[] TranscriptionAmbiguities, SortedDictionary<string, string>? SymbolToIpa,
    int MappedSymbols, string? MaterializedMapping, string? MappingSha256);

public sealed record MappingPreregistrationReport(
    string SchemaVersion, string PreregistrationId, string Status, string PreregistrationSha256,
    bool ComparativeResultsInspected, MappingPartitions Partitions, string[] FeatureDimensions,
    double[] AllowedFeatureValues, string? InventoryId, string[] EvaluationSymbols,
    MappingCandidateReport[] Candidates, string? ProposalCandidateId, MappingDecision? Decision,
    bool ReadyForConfirmatory, string[] Blockers);

public sealed class MappingPreregistration(
    string repositoryRoot,
    FeatureSet features,
    Enochian.Text.Encoding ipaEncoding)
{
    private static readonly string[] EvaluationLoci = ["f1r", "f1v", "f2r", "f2v"];
    private static readonly string[] HoldoutLoci = ["f3r", "f3v", "f4r", "f4v"];
    private readonly AutomatedFiles files = new(repositoryRoot);
    private readonly Enochian.Text.Encoder encoder = new(features, ipaEncoding);

    public int Run(string configPath)
    {
        var config = LoadConfig(configPath);
        ValidateFixedProtocol(config);
        _ = files.Pin(config.Features);
        _ = files.Pin(config.IpaEncoding);

        foreach (var source in config.Candidates.Where(candidate => candidate.Source != null)
            .Select(candidate => candidate.Source!))
        {
            _ = files.Pin(source);
        }

        ValidatePaths(configPath, config);
        var blockers = new List<string>();
        var inventory = LoadInventory(config, blockers);
        var proposal = SelectProposal(config, blockers);
        var mappingHash = MaterializeProposal(inventory, proposal, blockers);
        ValidateDecision(config, proposal, mappingHash, blockers);

        var report = new MappingPreregistrationReport(
            "1.0.0",
            config.PreregistrationId,
            config.Status,
            PronunciationAudit.HashConfiguration(configPath),
            config.ComparativeResultsInspected,
            config.Partitions,
            [.. features.FeatureList],
            [features.MinusValue, features.UnsetValue, features.PlusValue],
            inventory?.InventoryId,
            inventory == null ? [] : [.. inventory.Symbols.Select(item => item.Symbol).Order(StringComparer.Ordinal)],
            [.. config.Candidates.OrderBy(candidate => candidate.Id, StringComparer.Ordinal).Select(candidate =>
                new MappingCandidateReport(
                    candidate.Id,
                    candidate.Version,
                    candidate.Kind,
                    candidate.Description,
                    candidate.Source,
                    candidate.LinguisticRationale,
                    candidate.DifferencesFromLegacy,
                    candidate.Normalization,
                    candidate.Segmentation,
                    candidate.TranscriptionAmbiguities,
                    candidate.SymbolToIpa,
                    candidate.SymbolToIpa?.Count ?? 0,
                    candidate.MaterializedMapping,
                    candidate == proposal ? mappingHash : null))],
            config.ProposalCandidateId,
            config.Decision,
            config.Status == "approved" && blockers.Count == 0,
            [.. blockers.Order(StringComparer.Ordinal)]);

        AutomatedFiles.Write(files.Resolve(config.Outputs.ReportJson), report);
        WriteMarkdown(files.Resolve(config.Outputs.ReportMarkdown), report);
        return config.Status == "declined" ||
            (config.Status == "approved" && blockers.Count == 0) ? 0 : 1;
    }

    private MappingPreregistrationConfig LoadConfig(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
        var schemaPath = Path.Combine(repositoryRoot, "experiments", "schemas", "mapping-preregistration.schema.json");
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        if (!schema.Evaluate(document.RootElement).IsValid)
        {
            throw new InvalidDataException($"{path}: invalid mapping preregistration.");
        }

        return JsonSerializer.Deserialize<MappingPreregistrationConfig>(
            document.RootElement, AutomatedProtocol.JsonOptions)
            ?? throw new InvalidDataException("Empty mapping preregistration.");
    }

    private static void ValidateFixedProtocol(MappingPreregistrationConfig config)
    {
        Require(config.Partitions.EvaluationLoci.SequenceEqual(EvaluationLoci, StringComparer.Ordinal),
            "Evaluation loci must remain f1r/f1v/f2r/f2v.");
        Require(config.Partitions.HoldoutLoci.SequenceEqual(HoldoutLoci, StringComparer.Ordinal),
            "Holdout loci must remain f3r/f3v/f4r/f4v.");
        Require(!config.Partitions.HoldoutOpened, "Holdout data must remain unopened.");
        Require(!config.ComparativeResultsInspected,
            "Comparative results cannot be inspected before mapping approval.");
        Require(config.Candidates.Select(candidate => candidate.Id).Distinct(StringComparer.Ordinal).Count() ==
            config.Candidates.Length, "Mapping candidate IDs must be unique.");
        Require(config.Candidates.Count(candidate => candidate.Kind == "proposal") <= 1,
            "Only one proposal may be presented for confirmatory approval.");

        var outputPaths = new[]
        {
            config.Outputs.ReportJson,
            config.Outputs.ReportMarkdown,
        }.Concat(config.Candidates.Select(candidate => candidate.MaterializedMapping)
            .OfType<string>()).ToArray();
        Require(outputPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count() == outputPaths.Length,
            "Mapping preregistration outputs must use distinct paths.");
    }

    private void ValidatePaths(string configPath, MappingPreregistrationConfig config)
    {
        var inputPaths = new[]
        {
            Path.GetFullPath(configPath),
            files.Resolve(config.Features.Path),
            files.Resolve(config.IpaEncoding.Path),
        }.Concat(config.Inventory == null ? [] : [files.Resolve(config.Inventory.Path)])
            .Concat(config.Candidates.Where(candidate => candidate.Source != null)
                .Select(candidate => files.Resolve(candidate.Source!.Path)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outputPaths = new[]
        {
            files.Resolve(config.Outputs.ReportJson),
            files.Resolve(config.Outputs.ReportMarkdown),
        }.Concat(config.Candidates.Select(candidate => candidate.MaterializedMapping)
            .OfType<string>()
            .Select(files.Resolve));
        Require(!outputPaths.Any(inputPaths.Contains),
            "Mapping preregistration outputs cannot overwrite pinned inputs.");
    }

    private MappingSymbolInventory? LoadInventory(
        MappingPreregistrationConfig config,
        List<string> blockers)
    {
        if (config.Inventory == null)
        {
            blockers.Add("evaluation_symbol_inventory_required");
            return null;
        }

        using var document = files.Read(config.Inventory);
        var schemaPath = Path.Combine(repositoryRoot, "experiments", "schemas", "mapping-symbol-inventory.schema.json");
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        Require(schema.Evaluate(document.RootElement).IsValid, "Invalid evaluation symbol inventory.");
        var inventory = JsonSerializer.Deserialize<MappingSymbolInventory>(
            document.RootElement, AutomatedProtocol.JsonOptions)
            ?? throw new InvalidDataException("Empty evaluation symbol inventory.");
        Require(inventory.EvaluationLoci.SequenceEqual(EvaluationLoci, StringComparer.Ordinal),
            "Inventory must contain only the frozen evaluation loci.");
        Require(!inventory.HoldoutOpened, "Inventory must not inspect holdout data.");
        Require(inventory.Symbols.Select(item => item.Symbol).Distinct(StringComparer.Ordinal).Count() ==
            inventory.Symbols.Length, "Inventory symbols must be unique.");
        Require(inventory.Symbols.SelectMany(item => item.Loci).ToHashSet(StringComparer.Ordinal)
            .SetEquals(EvaluationLoci), "Every evaluation locus must be represented in the inventory.");
        Require(inventory.Symbols.All(item => item.Loci.All(EvaluationLoci.Contains)),
            "Inventory symbols cannot reference non-evaluation loci.");
        _ = files.Pin(inventory.Transcription, configuration: false);
        return inventory;
    }

    private static MappingCandidate? SelectProposal(
        MappingPreregistrationConfig config,
        List<string> blockers)
    {
        if (config.ProposalCandidateId == null)
        {
            blockers.Add("single_unscored_proposal_required");
            return null;
        }

        var proposal = config.Candidates.SingleOrDefault(candidate =>
            candidate.Id == config.ProposalCandidateId);
        Require(proposal != null && proposal.Kind == "proposal",
            "The proposal candidate ID must select the single proposal.");
        return proposal;
    }

    private string? MaterializeProposal(
        MappingSymbolInventory? inventory,
        MappingCandidate? proposal,
        List<string> blockers)
    {
        if (proposal == null)
        {
            return null;
        }

        if (proposal.SymbolToIpa == null || proposal.MaterializedMapping == null)
        {
            blockers.Add("proposal_mapping_and_output_required");
            return null;
        }

        if (inventory == null)
        {
            return null;
        }

        var inventorySymbols = inventory.Symbols.Select(item => item.Symbol).ToHashSet(StringComparer.Ordinal);
        if (!inventorySymbols.SetEquals(proposal.SymbolToIpa.Keys))
        {
            blockers.Add("proposal_must_map_exact_evaluation_inventory");
            return null;
        }

        var mapping = new SortedDictionary<string, double[]>(StringComparer.Ordinal);
        foreach (var item in proposal.SymbolToIpa)
        {
            var (_, _, phones) = encoder.GetTextAndPhones(item.Value, out var unknown);
            Require(unknown.Count == 0, $"Proposal IPA for '{item.Key}' contains unknown symbols.");
            Require(phones.Count == 1,
                $"Proposal IPA for '{item.Key}' must encode to exactly one phonological vector.");
            Require(phones[0].Length == features.NumDimensions &&
                phones[0].All(value => value == features.MinusValue ||
                    value == features.UnsetValue || value == features.PlusValue),
                $"Proposal IPA for '{item.Key}' is incompatible with the pinned feature space.");
            mapping[item.Key] = phones[0];
        }

        var path = files.Resolve(proposal.MaterializedMapping);
        AutomatedFiles.Write(path, mapping);
        return PronunciationAudit.HashConfiguration(path);
    }

    private static void ValidateDecision(
        MappingPreregistrationConfig config,
        MappingCandidate? proposal,
        string? mappingHash,
        List<string> blockers)
    {
        if (config.Status == "pending")
        {
            Require(config.Decision == null, "Pending preregistration cannot contain a decision.");
            blockers.Add("explicit_researcher_decision_required");
            return;
        }

        var decision = config.Decision
            ?? throw new InvalidDataException("A completed preregistration requires an explicit decision.");
        Require(decision.Status == config.Status,
            "Preregistration status and explicit decision must agree.");
        if (config.Status == "declined")
        {
            Require(decision.CandidateId == null && decision.MappingSha256 == null,
                "A declined decision cannot approve a candidate or mapping checksum.");
            return;
        }

        var approvedProposal = proposal
            ?? throw new InvalidDataException("Approval requires a complete materialized proposal.");
        Require(mappingHash != null, "Approval requires a complete materialized proposal.");
        Require(decision.CandidateId == approvedProposal.Id,
            "Approval must name the single proposed candidate.");
        Require(decision.MappingSha256 == mappingHash,
            "Approval must record the exact materialized mapping checksum.");
    }

    private static void WriteMarkdown(string path, MappingPreregistrationReport report)
    {
        var lines = new List<string>
        {
            "# Voynich mapping preregistration",
            string.Empty,
            $"Preregistration: `{report.PreregistrationId}`",
            $"Status: **{report.Status}**",
            $"Ready for confirmatory use: **{report.ReadyForConfirmatory.ToString(CultureInfo.InvariantCulture).ToLowerInvariant()}**",
            string.Empty,
            "Comparative results inspected before decision: **no**",
            $"Evaluation loci: `{string.Join("`, `", report.Partitions.EvaluationLoci)}`",
            $"Unopened holdout loci: `{string.Join("`, `", report.Partitions.HoldoutLoci)}`",
            string.Empty,
            "## Pinned phonological space",
            string.Empty,
            $"Dimensions ({report.FeatureDimensions.Length}): `{string.Join("`, `", report.FeatureDimensions)}`",
            $"Allowed values: `{string.Join("`, `", report.AllowedFeatureValues.Select(value => value.ToString(CultureInfo.InvariantCulture)))}`",
            string.Empty,
            "## Evaluation inventory",
            string.Empty,
            report.InventoryId == null
                ? "The canonical evaluation-only symbol inventory has not yet been supplied by #49."
                : $"Inventory `{report.InventoryId}`: `{string.Join("`, `", report.EvaluationSymbols)}`",
            string.Empty,
            "## Candidates",
            string.Empty,
        };
        foreach (var candidate in report.Candidates)
        {
            lines.Add($"### {candidate.Id} {candidate.Version} ({candidate.Kind})");
            lines.Add(string.Empty);
            lines.Add(candidate.Description);
            lines.Add(string.Empty);
            if (candidate.Source != null)
            {
                lines.Add($"Source: `{candidate.Source.Path}` (`{candidate.Source.Sha256}`)");
            }

            lines.Add($"Rationale: {candidate.LinguisticRationale}");
            lines.Add($"Normalization: Unicode {candidate.Normalization.UnicodeNormalization}; " +
                $"case {candidate.Normalization.CaseTreatment}; uncertain symbols {candidate.Normalization.UncertainSymbols}.");
            lines.Add($"Segmentation: {candidate.Segmentation.Method}; overlap resolution " +
                $"{candidate.Segmentation.OverlapResolution}; multigraphs " +
                (candidate.Segmentation.Multigraphs.Length == 0
                    ? "none."
                    : $"`{string.Join("`, `", candidate.Segmentation.Multigraphs)}`."));
            lines.Add("Differences from legacy: " +
                (candidate.DifferencesFromLegacy.Length == 0
                    ? "none recorded."
                    : string.Join("; ", candidate.DifferencesFromLegacy)));
            lines.Add("Transcription ambiguities: " +
                (candidate.TranscriptionAmbiguities.Length == 0
                    ? "none recorded."
                    : string.Join("; ", candidate.TranscriptionAmbiguities.Select(ambiguity =>
                        $"{ambiguity.Input} -> {ambiguity.Treatment} ({ambiguity.Rationale})"))));
            lines.Add($"Mapped symbols: {candidate.MappedSymbols.ToString(CultureInfo.InvariantCulture)}");
            if (candidate.SymbolToIpa != null)
            {
                lines.Add(string.Empty);
                lines.Add("| EVA symbol | Proposed IPA |");
                lines.Add("|---|---|");
                lines.AddRange(candidate.SymbolToIpa.Select(item => $"| `{item.Key}` | `{item.Value}` |"));
            }

            if (candidate.MappingSha256 != null)
            {
                lines.Add($"Materialized mapping SHA-256: `{candidate.MappingSha256}`");
            }

            lines.Add(string.Empty);
        }

        lines.Add("## Researcher decision");
        lines.Add(string.Empty);
        if (report.Decision == null)
        {
            lines.Add("No decision has been recorded.");
        }
        else
        {
            lines.Add($"Decision: **{report.Decision.Status}**");
            lines.Add($"Researcher: {report.Decision.Researcher}");
            lines.Add($"Date: {report.Decision.DecidedOn:yyyy-MM-dd}");
            lines.Add($"Rationale: {report.Decision.Rationale}");
        }

        lines.Add(string.Empty);
        lines.Add("## Blockers");
        lines.Add(string.Empty);
        lines.AddRange(report.Blockers.Length == 0
            ? ["None."]
            : report.Blockers.Select(blocker => $"- `{blocker}`"));
        WriteText(path, string.Join("\n", lines) + "\n");
    }

    private static void WriteText(string path, string value)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, value.ReplaceLineEndings("\n"), new UTF8Encoding(false));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
