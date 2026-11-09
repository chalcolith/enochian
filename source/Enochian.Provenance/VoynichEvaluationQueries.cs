using Json.Schema;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Enochian.Provenance;

public sealed record VoynichQueryAggregation(
    string TypeIdentity,
    string Frequency,
    string RepresentativeLocus);

public sealed record VoynichQueryOutputs(
    string Queries,
    string ReportJson,
    string ReportMarkdown);

public sealed record VoynichQueryPlan
{
    [JsonPropertyName("$schema")]
    public required string Schema { get; init; }
    public required string SchemaVersion { get; init; }
    public required string MaterializationId { get; init; }
    public required string MaterializerVersion { get; init; }
    public required bool Frozen { get; init; }
    public required InventoryPin InventoryPlan { get; init; }
    public required InventoryPin Inventory { get; init; }
    public required InventoryPin MappingPreregistration { get; init; }
    public required InventoryPin Mapping { get; init; }
    public required string[] EvaluationLoci { get; init; }
    public required string[] HoldoutLoci { get; init; }
    public required VoynichQueryAggregation Aggregation { get; init; }
    public required VoynichQueryOutputs Outputs { get; init; }
}

public sealed record VoynichEvaluationQuery(
    string QueryId,
    string Locus,
    string[] Symbols,
    int TokenFrequency);

public sealed record VoynichQuerySource(string Locus, int TokenFrequency);

public sealed record VoynichQueryAudit(
    string QueryId,
    string Type,
    string RepresentativeLocus,
    int TokenFrequency,
    VoynichQuerySource[] Sources);

public sealed record VoynichQueryLocus(
    string Locus,
    int AcceptedTokens,
    int SourceTypes,
    int RepresentativeTypes);

public sealed record VoynichQueryReport
{
    public required string SchemaVersion { get; init; }
    public required string MaterializationId { get; init; }
    public required string MaterializerVersion { get; init; }
    public required string SoftwareSha256 { get; init; }
    public required string PlanSha256 { get; init; }
    public required InventoryPin Transcription { get; init; }
    public required string[] EvaluationLoci { get; init; }
    public required string[] HoldoutLoci { get; init; }
    public required bool HoldoutOpened { get; init; }
    public required VoynichQueryAggregation Aggregation { get; init; }
    public required int AcceptedTokens { get; init; }
    public required int UniqueTypes { get; init; }
    public required VoynichQueryLocus[] Loci { get; init; }
    public required VoynichQueryAudit[] Queries { get; init; }
    public required string QueriesSha256 { get; init; }
    public required string MappingSha256 { get; init; }
    public required bool MappingApproved { get; init; }
    public required bool ReadyForPreflight { get; init; }
    public required string[] Blockers { get; init; }
}

public sealed class VoynichEvaluationQueries(string repositoryRoot)
{
    private static readonly string[] EvaluationLoci = ["f1r", "f1v", "f2r", "f2v"];
    private static readonly string[] HoldoutLoci = ["f3r", "f3v", "f4r", "f4v"];
    private static readonly JsonSerializerOptions IndentedOptions =
        new(PronunciationAudit.JsonOptions) { WriteIndented = true };

    public VoynichQueryReport Run(string planPath)
    {
        var plan = LoadPlan(planPath);
        ValidatePlan(planPath, plan);
        var inventoryBuilder = new VoynichEvaluationInventory(repositoryRoot);
        var inventoryPlanPath = Pin(plan.InventoryPlan);
        var inventoryPlan = inventoryBuilder.LoadPlan(inventoryPlanPath);
        inventoryBuilder.ValidatePlan(inventoryPlanPath, inventoryPlan);
        Require(inventoryPlan.EvaluationLoci.SequenceEqual(EvaluationLoci, StringComparer.Ordinal) &&
            inventoryPlan.HoldoutLoci.SequenceEqual(HoldoutLoci, StringComparer.Ordinal),
            "Pinned inventory plan has an incompatible partition boundary.");
        Require(inventoryPlan.Outputs.Inventory == plan.Inventory.Path,
            "Pinned inventory output does not match the query plan inventory.");

        var inventory = JsonSerializer.Deserialize<VoynichSymbolInventory>(
            File.ReadAllText(Pin(plan.Inventory)), PronunciationAudit.JsonOptions)
            ?? throw new InvalidDataException("Empty Voynich symbol inventory.");
        var mappingPath = Pin(plan.Mapping);
        var mapping = JsonSerializer.Deserialize<SortedDictionary<string, double[]>>(
            File.ReadAllText(mappingPath), PronunciationAudit.JsonOptions)
            ?? throw new InvalidDataException("Empty approved mapping.");
        ValidateApproval(Pin(plan.MappingPreregistration), plan.Mapping);
        var inventorySymbols = inventory.Symbols.Select(item => item.Symbol).ToHashSet(StringComparer.Ordinal);
        Require(mapping.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(inventorySymbols) &&
            mapping.Values.All(vector => vector.Length > 0 && vector.All(double.IsFinite)),
            "Approved mapping must contain one finite vector for every inventory symbol.");

        var sourcePath = inventoryBuilder.Resolve(inventoryPlan.Transcription.Snapshot.Path);
        Require(PronunciationAudit.Hash(sourcePath) == inventoryPlan.Transcription.Snapshot.Sha256,
            "Voynich transcription snapshot checksum mismatch.");
        var counts = EvaluationLoci.ToDictionary(
            locus => locus,
            _ => new SortedDictionary<string, int>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        Extract(sourcePath, inventoryPlan, counts);
        ValidateInventory(counts, inventory);

        var types = counts.Values.SelectMany(value => value.Keys).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var representatives = AssignRepresentatives(types, counts);
        var queries = types.Select((type, index) => new VoynichEvaluationQuery(
            string.Create(CultureInfo.InvariantCulture, $"eva-type-{index + 1:D4}"),
            representatives[type],
            [.. type.Select(character => character.ToString(CultureInfo.InvariantCulture))],
            EvaluationLoci.Sum(locus => counts[locus].GetValueOrDefault(type)))).ToArray();
        var queryPath = Resolve(plan.Outputs.Queries);
        WriteRows(queryPath, queries);
        ValidateQueries(queryPath);

        var audits = queries.Select(query =>
        {
            var type = string.Concat(query.Symbols);
            return new VoynichQueryAudit(
                query.QueryId,
                type,
                query.Locus,
                query.TokenFrequency,
                [.. EvaluationLoci.Where(locus => counts[locus].ContainsKey(type))
                    .Select(locus => new VoynichQuerySource(locus, counts[locus][type]))]);
        }).ToArray();
        var locusReports = EvaluationLoci.Select(locus => new VoynichQueryLocus(
            locus,
            counts[locus].Values.Sum(),
            counts[locus].Count,
            queries.Count(query => query.Locus == locus))).ToArray();
        var report = new VoynichQueryReport
        {
            SchemaVersion = "1.0.0",
            MaterializationId = plan.MaterializationId,
            MaterializerVersion = plan.MaterializerVersion,
            SoftwareSha256 = PronunciationAudit.Hash(typeof(VoynichEvaluationQueries).Assembly.Location),
            PlanSha256 = PronunciationAudit.HashConfiguration(planPath),
            Transcription = inventoryPlan.Transcription.Snapshot,
            EvaluationLoci = EvaluationLoci,
            HoldoutLoci = HoldoutLoci,
            HoldoutOpened = false,
            Aggregation = plan.Aggregation,
            AcceptedTokens = locusReports.Sum(item => item.AcceptedTokens),
            UniqueTypes = queries.Length,
            Loci = locusReports,
            Queries = audits,
            QueriesSha256 = PronunciationAudit.Hash(queryPath),
            MappingSha256 = plan.Mapping.Sha256,
            MappingApproved = true,
            ReadyForPreflight = true,
            Blockers = [],
        };
        WriteJson(Resolve(plan.Outputs.ReportJson), report);
        WriteMarkdown(Resolve(plan.Outputs.ReportMarkdown), report);
        return report;
    }

    private VoynichQueryPlan LoadPlan(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
        var schemaPath = Path.Combine(
            repositoryRoot, "experiments", "schemas", "voynich-query-materialization.schema.json");
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        Require(schema.Evaluate(document.RootElement).IsValid,
            $"{path}: invalid Voynich query materialization plan.");
        return JsonSerializer.Deserialize<VoynichQueryPlan>(
            document.RootElement, PronunciationAudit.JsonOptions)
            ?? throw new InvalidDataException("Empty Voynich query materialization plan.");
    }

    private void ValidatePlan(string planPath, VoynichQueryPlan plan)
    {
        Require(plan.EvaluationLoci.SequenceEqual(EvaluationLoci, StringComparer.Ordinal) &&
            plan.HoldoutLoci.SequenceEqual(HoldoutLoci, StringComparer.Ordinal),
            "Query materialization must preserve the frozen evaluation/holdout boundary.");
        Require(plan.Aggregation == new VoynichQueryAggregation(
            "exact-normalized-symbol-sequence",
            "sum-accepted-token-occurrences-across-evaluation-loci",
            "maximize-source-frequency-then-frozen-locus-order-subject-to-all-loci-represented"),
            "Query aggregation and representative-locus rules are frozen.");

        var inputs = new[]
        {
            Path.GetFullPath(planPath),
            Resolve(plan.InventoryPlan.Path),
            Resolve(plan.Inventory.Path),
            Resolve(plan.MappingPreregistration.Path),
            Resolve(plan.Mapping.Path),
        }.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outputs = new[]
        {
            Resolve(plan.Outputs.Queries),
            Resolve(plan.Outputs.ReportJson),
            Resolve(plan.Outputs.ReportMarkdown),
        };
        Require(outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count() == outputs.Length &&
            !outputs.Any(inputs.Contains),
            "Voynich query outputs must be distinct and cannot overwrite pinned inputs.");
    }

    private static void Extract(
        string sourcePath,
        VoynichInventoryPlan plan,
        Dictionary<string, SortedDictionary<string, int>> counts)
    {
        using var reader = new StreamReader(sourcePath, Encoding.Latin1, false);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (!VoynichEvaluationInventory.TryReadLocator(
                    line, out _, out var locus, out var transcriber, out var textStart) ||
                transcriber != plan.Transcription.TranscriberCode ||
                !counts.TryGetValue(locus, out var locusCounts))
            {
                continue;
            }

            var text = VoynichEvaluationInventory.NormalizeSelectedText(line[textStart..], lineNumber);
            foreach (var token in VoynichEvaluationInventory.Tokenize(text, plan.Normalization)
                .Where(token => token.All(character => character is >= 'a' and <= 'z')))
            {
                locusCounts[token] = locusCounts.GetValueOrDefault(token) + 1;
            }
        }
    }

    private static void ValidateInventory(
        Dictionary<string, SortedDictionary<string, int>> counts,
        VoynichSymbolInventory inventory)
    {
        Require(inventory.EvaluationLoci.SequenceEqual(EvaluationLoci, StringComparer.Ordinal) &&
            !inventory.HoldoutOpened, "Inventory must remain evaluation-only.");
        var measured = new SortedDictionary<string, (int Count, HashSet<string> Loci)>(StringComparer.Ordinal);
        foreach (var locus in EvaluationLoci)
        {
            foreach (var token in counts[locus])
            {
                foreach (var character in token.Key)
                {
                    var symbol = character.ToString(CultureInfo.InvariantCulture);
                    if (!measured.TryGetValue(symbol, out var value))
                    {
                        value = (0, new(StringComparer.Ordinal));
                    }

                    value.Count += token.Value;
                    _ = value.Loci.Add(locus);
                    measured[symbol] = value;
                }
            }
        }

        Require(measured.Count == inventory.Symbols.Length &&
            inventory.Symbols.All(symbol => measured.TryGetValue(symbol.Symbol, out var value) &&
                value.Count == symbol.TokenCount &&
                symbol.Loci.SequenceEqual(EvaluationLoci.Where(value.Loci.Contains), StringComparer.Ordinal)),
            "Query token counts and source memberships do not reconcile with the pinned inventory.");
    }

    private static SortedDictionary<string, string> AssignRepresentatives(
        string[] types,
        Dictionary<string, SortedDictionary<string, int>> counts)
    {
        var states = new Dictionary<int, AssignmentState> { [0] = new(0, string.Empty) };
        foreach (var type in types)
        {
            var next = new Dictionary<int, AssignmentState>();
            foreach (var state in states.OrderBy(item => item.Key))
            {
                for (var index = 0; index < EvaluationLoci.Length; index++)
                {
                    var frequency = counts[EvaluationLoci[index]].GetValueOrDefault(type);
                    if (frequency == 0)
                    {
                        continue;
                    }

                    var mask = state.Key | (1 << index);
                    var candidate = new AssignmentState(
                        state.Value.Score + frequency,
                        state.Value.Key + index.ToString(CultureInfo.InvariantCulture));
                    if (!next.TryGetValue(mask, out var previous) ||
                        candidate.Score > previous.Score ||
                        (candidate.Score == previous.Score &&
                            string.CompareOrdinal(candidate.Key, previous.Key) < 0))
                    {
                        next[mask] = candidate;
                    }
                }
            }

            states = next;
        }

        if (!states.TryGetValue((1 << EvaluationLoci.Length) - 1, out var selected))
        {
            throw new InvalidDataException(
                "Distinct observed query types cannot represent every evaluation locus.");
        }

        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < types.Length; index++)
        {
            result.Add(types[index], EvaluationLoci[selected.Key[index] - '0']);
        }

        return result;
    }

    private static void ValidateApproval(string path, InventoryPin mapping)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Require(root.GetProperty("status").GetString() == "approved" &&
            root.GetProperty("ready_for_confirmatory").GetBoolean() &&
            root.GetProperty("blockers").GetArrayLength() == 0,
            "Mapping preregistration is not approved for confirmatory use.");
        var proposalId = root.GetProperty("proposal_candidate_id").GetString();
        var decision = root.GetProperty("decision");
        Require(decision.GetProperty("status").GetString() == "approved" &&
            decision.GetProperty("candidate_id").GetString() == proposalId &&
            decision.GetProperty("mapping_sha256").GetString() == mapping.Sha256,
            "Mapping decision does not approve the pinned proposal checksum.");
        var candidate = root.GetProperty("candidates").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == proposalId);
        Require(candidate.GetProperty("materialized_mapping").GetString() == mapping.Path &&
            candidate.GetProperty("mapping_sha256").GetString() == mapping.Sha256,
            "Approved candidate does not identify the pinned mapping artifact.");
    }

    private void ValidateQueries(string path)
    {
        var schemaPath = Path.Combine(
            repositoryRoot, "experiments", "schemas", "voynich-evaluation-query.schema.json");
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        foreach (var line in File.ReadLines(path, new UTF8Encoding(false, true)))
        {
            using var document = JsonDocument.Parse(line);
            Require(schema.Evaluate(document.RootElement).IsValid,
                "Materialized evaluation query does not match its schema.");
        }
    }

    private string Pin(InventoryPin pin)
    {
        var path = Resolve(pin.Path);
        Require(PronunciationAudit.HashConfiguration(path) == pin.Sha256,
            "pinned_input_hash_mismatch:" + pin.Path);
        return path;
    }

    private string Resolve(string relativePath)
    {
        Require(!Path.IsPathRooted(relativePath),
            "Voynich query paths must be repository-relative.");
        var path = Path.GetFullPath(Path.Combine(
            repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(repositoryRoot, path);
        Require(relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "Voynich query paths must remain inside the repository.");
        return path;
    }

    private static void WriteRows<T>(string path, IEnumerable<T> values) =>
        VoynichEvaluationInventory.WriteBytes(path, Encoding.UTF8.GetBytes(string.Concat(
            values.Select(value => JsonSerializer.Serialize(value, PronunciationAudit.JsonOptions) + "\n"))));

    private static void WriteJson<T>(string path, T value) =>
        VoynichEvaluationInventory.WriteBytes(path, Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(value, IndentedOptions).ReplaceLineEndings("\n") + "\n"));

    private static void WriteMarkdown(string path, VoynichQueryReport report)
    {
        var lines = new List<string>
        {
            "# Evaluation-only Voynich query materialization",
            string.Empty,
            $"Materialization: `{report.MaterializationId}` (`{report.MaterializerVersion}`)",
            $"Materializer assembly SHA-256: `{report.SoftwareSha256}`",
            $"Ready for preflight: **{report.ReadyForPreflight.ToString().ToLowerInvariant()}**",
            $"Approved mapping SHA-256: `{report.MappingSha256}`",
            string.Empty,
            "## Frozen aggregation",
            string.Empty,
            $"- Type identity: `{report.Aggregation.TypeIdentity}`",
            $"- Frequency: `{report.Aggregation.Frequency}`",
            $"- Representative locus: `{report.Aggregation.RepresentativeLocus}`",
            "- Representative loci maximize retained source-locus frequency under the constraint that every " +
                "evaluation locus is represented; ties use frozen locus order.",
            string.Empty,
            "## Partition and counts",
            string.Empty,
            $"- Evaluation: `{string.Join("`, `", report.EvaluationLoci)}`",
            $"- Holdout, unopened: `{string.Join("`, `", report.HoldoutLoci)}`",
            $"- Accepted token occurrences: **{report.AcceptedTokens}**",
            $"- Unique query types: **{report.UniqueTypes}**",
            $"- Query artifact SHA-256: `{report.QueriesSha256}`",
            string.Empty,
            "| Locus | Accepted occurrences | Source types | Representative types |",
            "|---|---:|---:|---:|",
        };
        lines.AddRange(report.Loci.Select(item =>
            $"| {item.Locus} | {item.AcceptedTokens} | {item.SourceTypes} | {item.RepresentativeTypes} |"));
        lines.Add(string.Empty);
        lines.Add("The JSON audit records every query type's source-locus memberships and reconciled frequencies. " +
            "The scoring artifact contains no definitions or semantic annotations.");
        VoynichEvaluationInventory.WriteBytes(
            path, Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n"));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private sealed record AssignmentState(int Score, string Key);
}
