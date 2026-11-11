using Json.Schema;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Enochian.Provenance;

public sealed record HoldoutMaterializationOutputs(
    string Queries,
    string Exclusions,
    string LeakageAudit,
    string ReportJson,
    string ReportMarkdown);

public sealed record HoldoutMaterializationPlan
{
    [JsonPropertyName("$schema")]
    public required string Schema { get; init; }
    public required string SchemaVersion { get; init; }
    public required string MaterializationId { get; init; }
    public required bool Frozen { get; init; }
    public required InventoryPin SourcePlan { get; init; }
    public required InventoryPin Mapping { get; init; }
    public required InventoryPin MappingApproval { get; init; }
    public required InventoryPin[] ProtectedArtifacts { get; init; }
    public required string[] EvaluationLoci { get; init; }
    public required string[] HoldoutLoci { get; init; }
    public required HoldoutMaterializationOutputs Outputs { get; init; }
}

public sealed record HoldoutLeakageFile(string Path, string Sha256);

public sealed record HoldoutLeakageAudit(
    string SchemaVersion,
    string Status,
    int ExperimentConfigsChecked,
    int RunManifestsChecked,
    HoldoutLeakageFile[] Files,
    string[] Blockers);

public sealed record HoldoutQueryExclusion(
    int SourceLine,
    string Locator,
    string Token,
    string Reason);

public sealed record HoldoutQueryReport(
    string SchemaVersion,
    string MaterializationId,
    string Status,
    string PlanSha256,
    InventoryPin Transcription,
    string[] EvaluationLoci,
    string[] HoldoutLoci,
    bool HoldoutOpened,
    int AcceptedTokens,
    int ExcludedTokens,
    int UniqueTypes,
    VoynichQueryLocus[] Loci,
    string QueriesSha256,
    string MappingSha256,
    HoldoutLeakageAudit LeakageAudit,
    string[] Blockers);

public sealed class VoynichHoldoutQueries(string repositoryRoot)
{
    private static readonly string[] EvaluationLoci = ["f1r", "f1v", "f2r", "f2v"];
    private static readonly string[] HoldoutLoci = ["f3r", "f3v", "f4r", "f4v"];
    private static readonly JsonSerializerOptions IndentedOptions =
        new(PronunciationAudit.JsonOptions) { WriteIndented = true };

    public HoldoutQueryReport Run(string planPath)
    {
        var plan = LoadPlan(planPath);
        ValidatePlan(planPath, plan);
        var outputPaths = OutputPaths(plan);
        if (outputPaths.Any(File.Exists))
        {
            throw new InvalidDataException(
                "M4-04 holdout outputs already exist; the holdout materialization is one-time and immutable.");
        }

        VerifyPins(plan.ProtectedArtifacts.Append(plan.SourcePlan)
            .Append(plan.Mapping).Append(plan.MappingApproval));
        var leakage = AuditLeakage(planPath, plan);
        if (leakage.Blockers.Length != 0)
        {
            throw new InvalidDataException(
                "Holdout leakage audit failed: " + string.Join(", ", leakage.Blockers));
        }

        var sourcePlanPath = Resolve(plan.SourcePlan.Path);
        var sourcePlan = JsonSerializer.Deserialize<VoynichInventoryPlan>(
            File.ReadAllText(sourcePlanPath, new UTF8Encoding(false, true)),
            PronunciationAudit.JsonOptions)
            ?? throw new InvalidDataException("Empty frozen Voynich source plan.");
        var sourcePath = Resolve(sourcePlan.Transcription.Snapshot.Path);
        Require(PronunciationAudit.Hash(sourcePath) == sourcePlan.Transcription.Snapshot.Sha256,
            "Voynich transcription snapshot checksum mismatch.");
        var mapping = JsonSerializer.Deserialize<SortedDictionary<string, double[]>>(
            File.ReadAllText(Resolve(plan.Mapping.Path), new UTF8Encoding(false, true)),
            PronunciationAudit.JsonOptions)
            ?? throw new InvalidDataException("Empty frozen mapping.");

        var counts = HoldoutLoci.ToDictionary(
            locus => locus,
            _ => new SortedDictionary<string, int>(StringComparer.Ordinal),
            StringComparer.Ordinal);
        var exclusions = new List<HoldoutQueryExclusion>();
        Extract(sourcePath, sourcePlan, mapping.Keys.ToHashSet(StringComparer.Ordinal), counts, exclusions);
        Require(counts.All(item => item.Value.Count > 0),
            "Every frozen holdout locus must contain at least one scorable query type.");

        var types = counts.Values.SelectMany(value => value.Keys).Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray();
        var representatives = AssignRepresentatives(types, counts);
        var queries = types.Select((type, index) => new VoynichEvaluationQuery(
            string.Create(CultureInfo.InvariantCulture, $"holdout-eva-type-{index + 1:D4}"),
            representatives[type],
            [.. type.Select(character => character.ToString(CultureInfo.InvariantCulture))],
            HoldoutLoci.Sum(locus => counts[locus].GetValueOrDefault(type)))).ToArray();
        WriteRows(Resolve(plan.Outputs.Queries), queries);
        WriteRows(Resolve(plan.Outputs.Exclusions), exclusions);
        WriteJson(Resolve(plan.Outputs.LeakageAudit), leakage);

        var loci = HoldoutLoci.Select(locus => new VoynichQueryLocus(
            locus,
            counts[locus].Values.Sum(),
            counts[locus].Count,
            queries.Count(query => query.Locus == locus))).ToArray();
        var report = new HoldoutQueryReport(
            "1.0.0",
            plan.MaterializationId,
            "completed",
            PronunciationAudit.HashConfiguration(planPath),
            sourcePlan.Transcription.Snapshot,
            EvaluationLoci,
            HoldoutLoci,
            true,
            loci.Sum(item => item.AcceptedTokens),
            exclusions.Count,
            queries.Length,
            loci,
            PronunciationAudit.Hash(Resolve(plan.Outputs.Queries)),
            plan.Mapping.Sha256,
            leakage,
            []);
        WriteJson(Resolve(plan.Outputs.ReportJson), report);
        WriteMarkdown(Resolve(plan.Outputs.ReportMarkdown), report);
        VerifyPins(plan.ProtectedArtifacts);
        return report;
    }

    private HoldoutMaterializationPlan LoadPlan(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
        var schema = JsonSchema.FromText(File.ReadAllText(
            Path.Combine(repositoryRoot, "experiments", "schemas", "m4-holdout-materialization.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        Require(schema.Evaluate(document.RootElement).IsValid,
            $"{path}: invalid M4-04 holdout materialization plan.");
        return JsonSerializer.Deserialize<HoldoutMaterializationPlan>(
            document.RootElement, PronunciationAudit.JsonOptions)
            ?? throw new InvalidDataException("Empty M4-04 holdout materialization plan.");
    }

    private void ValidatePlan(string planPath, HoldoutMaterializationPlan plan)
    {
        Require(plan.EvaluationLoci.SequenceEqual(EvaluationLoci, StringComparer.Ordinal) &&
            plan.HoldoutLoci.SequenceEqual(HoldoutLoci, StringComparer.Ordinal),
            "M4-04 must preserve the preregistered evaluation/holdout partition.");
        var inputs = plan.ProtectedArtifacts.Append(plan.SourcePlan).Append(plan.Mapping)
            .Append(plan.MappingApproval).Select(pin => Resolve(pin.Path))
            .Append(Path.GetFullPath(planPath)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outputs = OutputPaths(plan);
        Require(outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count() == outputs.Length &&
            !outputs.Any(inputs.Contains),
            "Holdout outputs must be distinct and cannot overwrite pinned inputs.");
    }

    private HoldoutLeakageAudit AuditLeakage(string planPath, HoldoutMaterializationPlan plan)
    {
        var configs = Directory.EnumerateFiles(
            Path.Combine(repositoryRoot, "experiments"), "*.json", SearchOption.AllDirectories)
            .Where(path => !Path.GetFullPath(path).Equals(
                Path.GetFullPath(planPath), StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).ToArray();
        var manifests = Directory.EnumerateFiles(
            Path.Combine(repositoryRoot, "reports"), "manifest.json", SearchOption.AllDirectories)
            .Where(path => !Path.GetFullPath(path).StartsWith(
                Path.GetFullPath(Path.Combine(repositoryRoot, "reports", "holdout-")),
                StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal).ToArray();
        var blockers = new List<string>();
        var files = new List<HoldoutLeakageFile>();
        foreach (var path in configs.Concat(manifests))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
            CheckLeakage(document.RootElement, path, plan.Outputs.Queries, blockers);
            files.Add(new(
                Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                PronunciationAudit.Hash(path)));
        }

        return new(
            "1.0.0",
            blockers.Count == 0 ? "passed" : "failed",
            configs.Length,
            manifests.Length,
            [.. files],
            [.. blockers.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
    }

    private static void CheckLeakage(
        JsonElement element,
        string path,
        string holdoutQueryPath,
        List<string> blockers)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if ((property.NameEquals("holdout_opened") || property.NameEquals("holdout_present")) &&
                    property.Value.ValueKind == JsonValueKind.True)
                {
                    blockers.Add("prior_holdout_flag:" + Path.GetFileName(path));
                }

                CheckLeakage(property.Value, path, holdoutQueryPath, blockers);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CheckLeakage(item, path, holdoutQueryPath, blockers);
            }
        }
        else if (element.ValueKind == JsonValueKind.String &&
            string.Equals(element.GetString(), holdoutQueryPath, StringComparison.Ordinal))
        {
            blockers.Add("prior_holdout_query_reference:" + Path.GetFileName(path));
        }
    }

    private static void Extract(
        string sourcePath,
        VoynichInventoryPlan sourcePlan,
        HashSet<string> mappedSymbols,
        Dictionary<string, SortedDictionary<string, int>> counts,
        List<HoldoutQueryExclusion> exclusions)
    {
        using var reader = new StreamReader(sourcePath, Encoding.Latin1, false);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (!VoynichEvaluationInventory.TryReadLocator(
                    line, out var locator, out var locus, out var transcriber, out var textStart) ||
                transcriber != sourcePlan.Transcription.TranscriberCode ||
                !counts.TryGetValue(locus, out var locusCounts))
            {
                continue;
            }

            var text = VoynichEvaluationInventory.NormalizeSelectedText(line[textStart..], lineNumber);
            foreach (var token in VoynichEvaluationInventory.Tokenize(text, sourcePlan.Normalization))
            {
                if (!token.All(character => character is >= 'a' and <= 'z'))
                {
                    exclusions.Add(new(lineNumber, locator, token, "uncertain_or_non_basic_eva"));
                    continue;
                }

                if (token.Any(character => !mappedSymbols.Contains(
                    character.ToString(CultureInfo.InvariantCulture))))
                {
                    exclusions.Add(new(lineNumber, locator, token, "symbol_absent_from_frozen_mapping"));
                    continue;
                }

                locusCounts[token] = locusCounts.GetValueOrDefault(token) + 1;
            }
        }
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
                for (var index = 0; index < HoldoutLoci.Length; index++)
                {
                    var frequency = counts[HoldoutLoci[index]].GetValueOrDefault(type);
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

        Require(states.TryGetValue((1 << HoldoutLoci.Length) - 1, out var selected),
            "Distinct holdout query types cannot represent every holdout locus.");
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < types.Length; index++)
        {
            result.Add(types[index], HoldoutLoci[selected!.Key[index] - '0']);
        }

        return result;
    }

    private void VerifyPins(IEnumerable<InventoryPin> pins)
    {
        foreach (var pin in pins)
        {
            Require(PronunciationAudit.HashConfiguration(Resolve(pin.Path)) == pin.Sha256,
                "Pinned artifact hash mismatch: " + pin.Path);
        }
    }

    private string[] OutputPaths(HoldoutMaterializationPlan plan) =>
    [
        Resolve(plan.Outputs.Queries),
        Resolve(plan.Outputs.Exclusions),
        Resolve(plan.Outputs.LeakageAudit),
        Resolve(plan.Outputs.ReportJson),
        Resolve(plan.Outputs.ReportMarkdown),
    ];

    private string Resolve(string relativePath)
    {
        Require(!Path.IsPathRooted(relativePath), "M4-04 paths must be repository-relative.");
        var path = Path.GetFullPath(Path.Combine(
            repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        Require(Path.GetRelativePath(repositoryRoot, path) is not ".." &&
            !Path.GetRelativePath(repositoryRoot, path).StartsWith(
                ".." + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "M4-04 paths must remain inside the repository.");
        return path;
    }

    private static void WriteRows<T>(string path, IEnumerable<T> values)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false))
        {
            NewLine = "\r\n",
        };
        foreach (var value in values)
        {
            writer.WriteLine(JsonSerializer.Serialize(value, PronunciationAudit.JsonOptions));
        }
    }

    private static void WriteJson<T>(string path, T value)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(value, IndentedOptions).ReplaceLineEndings("\n") + "\n",
            new UTF8Encoding(false));
    }

    private static void WriteMarkdown(string path, HoldoutQueryReport report)
    {
        var text = $"""
            # M4-04 holdout query materialization

            The pre-open leakage audit **{report.LeakageAudit.Status}** before the frozen
            `f3r`, `f3v`, `f4r`, and `f4v` partition was read.

            - Accepted token occurrences: {report.AcceptedTokens}
            - Excluded token occurrences: {report.ExcludedTokens}
            - Unique definitions-free query types: {report.UniqueTypes}
            - Frozen mapping: `{report.MappingSha256}`
            - Query checksum: `{report.QueriesSha256}`
            - Definitions present: **false**
            - Holdout opened: **true**

            The same normalization, exact-type aggregation, representative-locus rule,
            and approved mapping used for M4-01 were applied without inspecting dictionary
            definitions or comparative scores. Tokens containing symbols absent from the
            frozen evaluation mapping are excluded as complete tokens and reported.
            """;
        WriteText(path, text);
    }

    private static void WriteText(string path, string text)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text.ReplaceLineEndings("\n") + "\n", new UTF8Encoding(false));
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
