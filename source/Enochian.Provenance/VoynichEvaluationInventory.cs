using Json.Schema;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Enochian.Provenance;

public sealed record InventoryPin(string Path, string Sha256);

public sealed record VoynichTranscriptionSource
{
    public required string Id { get; init; }
    public required string Release { get; init; }
    public required string TranscriberCode { get; init; }
    public required string Convention { get; init; }
    public required string CharacterEncoding { get; init; }
    public required InventoryPin Snapshot { get; init; }
    public required string ReleaseUrl { get; init; }
    public required string PermissionUrl { get; init; }
    public required string License { get; init; }
    public required string Attribution { get; init; }
}

public sealed record VoynichInventoryNormalization
{
    public required string UnicodeNormalization { get; init; }
    public required string CaseTreatment { get; init; }
    public required string[] BreakCharacters { get; init; }
    public required string[] AlignmentFillers { get; init; }
    public required string InlineComments { get; init; }
    public required string AcceptedSymbols { get; init; }
    public required string UncertainTokenPolicy { get; init; }
}

public sealed record VoynichInventoryOutputs(
    string Inventory, string Exclusions, string ReportJson, string ReportMarkdown);

public sealed record VoynichInventoryPlan
{
    [JsonPropertyName("$schema")]
    public required string Schema { get; init; }
    public required string SchemaVersion { get; init; }
    public required string ExtractionId { get; init; }
    public required string ExtractorVersion { get; init; }
    public required bool Frozen { get; init; }
    public required VoynichTranscriptionSource Transcription { get; init; }
    public required string[] EvaluationLoci { get; init; }
    public required string[] HoldoutLoci { get; init; }
    public required VoynichInventoryNormalization Normalization { get; init; }
    public required VoynichInventoryOutputs Outputs { get; init; }
}

public sealed record VoynichInventorySymbol(string Symbol, int TokenCount, string[] Loci);

public sealed record VoynichSymbolInventory
{
    public required string SchemaVersion { get; init; }
    public required string InventoryId { get; init; }
    public required InventoryPin Transcription { get; init; }
    public required string[] EvaluationLoci { get; init; }
    public required bool HoldoutOpened { get; init; }
    public required VoynichInventorySymbol[] Symbols { get; init; }
}

public sealed record VoynichInventoryExclusion(
    int SourceLine, string Locator, string Token, string Reason);

public sealed record VoynichLocusInventory(
    string Locus, int SourceLines, int AcceptedTokens, int ExcludedTokens, int SymbolOccurrences);

public sealed record VoynichInventoryReport
{
    public required string SchemaVersion { get; init; }
    public required string ExtractionId { get; init; }
    public required string ExtractorVersion { get; init; }
    public required string PlanSha256 { get; init; }
    public required VoynichTranscriptionSource Transcription { get; init; }
    public required string[] EvaluationLoci { get; init; }
    public required string[] HoldoutLoci { get; init; }
    public required bool HoldoutOpened { get; init; }
    public required VoynichInventoryNormalization Normalization { get; init; }
    public required VoynichLocusInventory[] Loci { get; init; }
    public required int AcceptedTokens { get; init; }
    public required int ExcludedTokens { get; init; }
    public required int SymbolOccurrences { get; init; }
    public required int UniqueSymbols { get; init; }
    public required string[] Symbols { get; init; }
    public required string InventorySha256 { get; init; }
    public required bool ReadyForMappingDecision { get; init; }
    public required string[] Blockers { get; init; }
}

public sealed class VoynichEvaluationInventory(string repositoryRoot)
{
    private static readonly string[] EvaluationLoci = ["f1r", "f1v", "f2r", "f2v"];
    private static readonly string[] HoldoutLoci = ["f3r", "f3v", "f4r", "f4v"];
    private static readonly JsonSerializerOptions IndentedOptions =
        new(PronunciationAudit.JsonOptions) { WriteIndented = true };

    public VoynichInventoryReport Run(string planPath)
    {
        var plan = LoadPlan(planPath);
        ValidatePlan(planPath, plan);
        var sourcePath = Resolve(plan.Transcription.Snapshot.Path);
        var sourceHash = PronunciationAudit.Hash(sourcePath);
        Require(sourceHash == plan.Transcription.Snapshot.Sha256,
            "Voynich transcription snapshot checksum mismatch.");

        var loci = EvaluationLoci.ToDictionary(
            locus => locus,
            _ => new LocusAccumulator(),
            StringComparer.Ordinal);
        var symbols = new SortedDictionary<string, SymbolAccumulator>(StringComparer.Ordinal);
        var exclusions = new List<VoynichInventoryExclusion>();
        Extract(sourcePath, plan, loci, symbols, exclusions);
        Require(loci.All(item => item.Value.SourceLines > 0),
            "Every frozen evaluation locus must have at least one selected transcription line.");

        var inventory = new VoynichSymbolInventory
        {
            SchemaVersion = "1.0.0",
            InventoryId = plan.ExtractionId,
            Transcription = plan.Transcription.Snapshot,
            EvaluationLoci = EvaluationLoci,
            HoldoutOpened = false,
            Symbols =
            [
                .. symbols.Select(item => new VoynichInventorySymbol(
                    item.Key,
                    item.Value.Occurrences,
                    [.. EvaluationLoci.Where(item.Value.Loci.Contains)])),
            ],
        };
        var inventoryPath = Resolve(plan.Outputs.Inventory);
        WriteJson(inventoryPath, inventory);
        ValidateInventory(inventoryPath);
        WriteRows(Resolve(plan.Outputs.Exclusions), exclusions);

        var locusReports = EvaluationLoci.Select(locus =>
        {
            var value = loci[locus];
            return new VoynichLocusInventory(
                locus,
                value.SourceLines,
                value.AcceptedTokens,
                value.ExcludedTokens,
                value.SymbolOccurrences);
        }).ToArray();
        var report = new VoynichInventoryReport
        {
            SchemaVersion = "1.0.0",
            ExtractionId = plan.ExtractionId,
            ExtractorVersion = plan.ExtractorVersion,
            PlanSha256 = PronunciationAudit.HashConfiguration(planPath),
            Transcription = plan.Transcription,
            EvaluationLoci = EvaluationLoci,
            HoldoutLoci = HoldoutLoci,
            HoldoutOpened = false,
            Normalization = plan.Normalization,
            Loci = locusReports,
            AcceptedTokens = locusReports.Sum(item => item.AcceptedTokens),
            ExcludedTokens = locusReports.Sum(item => item.ExcludedTokens),
            SymbolOccurrences = locusReports.Sum(item => item.SymbolOccurrences),
            UniqueSymbols = symbols.Count,
            Symbols = [.. symbols.Keys],
            InventorySha256 = PronunciationAudit.HashConfiguration(inventoryPath),
            ReadyForMappingDecision = symbols.Count > 0,
            Blockers = symbols.Count == 0 ? ["empty_evaluation_symbol_inventory"] : [],
        };
        WriteJson(Resolve(plan.Outputs.ReportJson), report);
        WriteMarkdown(Resolve(plan.Outputs.ReportMarkdown), report);
        return report;
    }

    internal VoynichInventoryPlan LoadPlan(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
        var schemaPath = Path.Combine(
            repositoryRoot, "experiments", "schemas", "voynich-inventory-extraction.schema.json");
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        Require(schema.Evaluate(document.RootElement).IsValid,
            $"{path}: invalid Voynich inventory extraction plan.");
        return JsonSerializer.Deserialize<VoynichInventoryPlan>(
            document.RootElement, PronunciationAudit.JsonOptions)
            ?? throw new InvalidDataException("Empty Voynich inventory extraction plan.");
    }

    internal void ValidatePlan(string planPath, VoynichInventoryPlan plan)
    {
        Require(plan.EvaluationLoci.SequenceEqual(EvaluationLoci, StringComparer.Ordinal),
            "Evaluation loci must remain f1r/f1v/f2r/f2v.");
        Require(plan.HoldoutLoci.SequenceEqual(HoldoutLoci, StringComparer.Ordinal),
            "Holdout loci must remain f3r/f3v/f4r/f4v.");
        Require(plan.Transcription.TranscriberCode == "H",
            "The frozen inventory uses Takeshi Takahashi's H transcription layer.");
        Require(plan.Normalization.BreakCharacters.SequenceEqual([".", ",", "-", "="], StringComparer.Ordinal) &&
            plan.Normalization.AlignmentFillers.SequenceEqual(["!", "%"], StringComparer.Ordinal),
            "Frozen EVA break and alignment characters cannot change.");

        var inputs = new[]
        {
            Path.GetFullPath(planPath),
            Resolve(plan.Transcription.Snapshot.Path),
            Path.Combine(repositoryRoot, "experiments", "schemas", "voynich-inventory-extraction.schema.json"),
            Path.Combine(repositoryRoot, "experiments", "schemas", "mapping-symbol-inventory.schema.json"),
        }.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var outputs = new[]
        {
            Resolve(plan.Outputs.Inventory),
            Resolve(plan.Outputs.Exclusions),
            Resolve(plan.Outputs.ReportJson),
            Resolve(plan.Outputs.ReportMarkdown),
        };
        Require(outputs.Distinct(StringComparer.OrdinalIgnoreCase).Count() == outputs.Length,
            "Voynich inventory outputs must use distinct paths.");
        Require(!outputs.Any(inputs.Contains),
            "Voynich inventory outputs cannot overwrite plan or source inputs.");
    }

    private static void Extract(
        string sourcePath,
        VoynichInventoryPlan plan,
        IReadOnlyDictionary<string, LocusAccumulator> loci,
        IDictionary<string, SymbolAccumulator> symbols,
        List<VoynichInventoryExclusion> exclusions)
    {
        using var reader = new StreamReader(sourcePath, Encoding.Latin1, false);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (!TryReadLocator(line, out var locator, out var locus, out var transcriber, out var textStart) ||
                transcriber != plan.Transcription.TranscriberCode ||
                !loci.TryGetValue(locus, out var locusCounts))
            {
                continue;
            }

            locusCounts.SourceLines++;
            var text = NormalizeSelectedText(line[textStart..], lineNumber);
            foreach (var token in Tokenize(text, plan.Normalization))
            {
                if (!token.All(character => character is >= 'a' and <= 'z'))
                {
                    locusCounts.ExcludedTokens++;
                    exclusions.Add(new(lineNumber, locator, token, "uncertain_or_non_basic_eva"));
                    continue;
                }

                locusCounts.AcceptedTokens++;
                locusCounts.SymbolOccurrences += token.Length;
                foreach (var character in token)
                {
                    var symbol = character.ToString(CultureInfo.InvariantCulture);
                    if (!symbols.TryGetValue(symbol, out var symbolCounts))
                    {
                        symbolCounts = new();
                        symbols.Add(symbol, symbolCounts);
                    }

                    symbolCounts.Occurrences++;
                    _ = symbolCounts.Loci.Add(locus);
                }
            }
        }
    }

    internal static bool TryReadLocator(
        string line,
        out string locator,
        out string locus,
        out string transcriber,
        out int textStart)
    {
        locator = string.Empty;
        locus = string.Empty;
        transcriber = string.Empty;
        textStart = 0;
        if (line.Length == 0 || line[0] != '<')
        {
            return false;
        }

        var close = line.IndexOf('>');
        if (close < 0)
        {
            return false;
        }

        locator = line[1..close];
        var semicolon = locator.LastIndexOf(';');
        var period = locator.IndexOf('.');
        if (semicolon < 0 || period < 0 || semicolon + 2 != locator.Length)
        {
            return false;
        }

        locus = locator[..period];
        transcriber = locator[(semicolon + 1)..];
        textStart = close + 1;
        return true;
    }

    internal static string NormalizeSelectedText(string text, int lineNumber)
    {
        var normalized = text.Normalize(NormalizationForm.FormC);
        var result = new StringBuilder(normalized.Length);
        var inComment = false;
        foreach (var character in normalized)
        {
            switch (character)
            {
                case '{' when !inComment:
                    inComment = true;
                    break;
                case '}' when inComment:
                    inComment = false;
                    break;
                case '{' or '}':
                    throw new InvalidDataException(
                        $"Malformed inline comment on selected source line {lineNumber.ToString(CultureInfo.InvariantCulture)}.");
                default:
                    if (!inComment)
                    {
                        _ = result.Append(character);
                    }

                    break;
            }
        }

        Require(!inComment,
            $"Unclosed inline comment on selected source line {lineNumber.ToString(CultureInfo.InvariantCulture)}.");
        var value = result.ToString();
        var trimmed = value.TrimEnd();
        Require(trimmed.Length > 0 && trimmed[^1] is '.' or ',' or '-' or '=',
            $"Selected source line {lineNumber.ToString(CultureInfo.InvariantCulture)} lacks an EVMT terminator.");
        return value;
    }

    internal static IEnumerable<string> Tokenize(
        string text,
        VoynichInventoryNormalization normalization)
    {
        var fillers = normalization.AlignmentFillers.Select(item => item[0]).ToHashSet();
        var breaks = normalization.BreakCharacters.Select(item => item[0]).ToHashSet();
        var token = new StringBuilder();
        foreach (var character in text)
        {
            if (fillers.Contains(character))
            {
                continue;
            }

            if (breaks.Contains(character) || char.IsWhiteSpace(character))
            {
                if (token.Length > 0)
                {
                    yield return token.ToString();
                    _ = token.Clear();
                }

                continue;
            }

            _ = token.Append(character);
        }

        if (token.Length > 0)
        {
            yield return token.ToString();
        }
    }

    private void ValidateInventory(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var schemaPath = Path.Combine(
            repositoryRoot, "experiments", "schemas", "mapping-symbol-inventory.schema.json");
        var schema = JsonSchema.FromText(File.ReadAllText(schemaPath),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        Require(schema.Evaluate(document.RootElement).IsValid,
            "Generated mapping symbol inventory does not conform to its schema.");
    }

    internal string Resolve(string relativePath)
    {
        Require(!Path.IsPathRooted(relativePath), "Voynich inventory paths must be repository-relative.");
        var path = Path.GetFullPath(Path.Combine(
            repositoryRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(repositoryRoot, path);
        Require(relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal),
            "Voynich inventory paths must remain inside the repository.");
        return path;
    }

    private static void WriteJson<T>(string path, T value) =>
        WriteBytes(path, Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(value, IndentedOptions).ReplaceLineEndings("\n") + "\n"));

    private static void WriteRows<T>(string path, IEnumerable<T> values) =>
        WriteBytes(path, Encoding.UTF8.GetBytes(string.Concat(
            values.Select(value => JsonSerializer.Serialize(value, PronunciationAudit.JsonOptions) + "\n"))));

    private static void WriteMarkdown(string path, VoynichInventoryReport report)
    {
        var lines = new List<string>
        {
            "# Evaluation-only Voynich symbol inventory",
            string.Empty,
            $"Extraction: `{report.ExtractionId}` (`{report.ExtractorVersion}`)",
            $"Ready for the mapping decision: **{report.ReadyForMappingDecision.ToString().ToLowerInvariant()}**",
            string.Empty,
            "## Source and permission",
            string.Empty,
            $"- Release: {report.Transcription.Id} {report.Transcription.Release}, " +
                $"transcriber layer `{report.Transcription.TranscriberCode}`",
            $"- Convention: {report.Transcription.Convention}",
            $"- Snapshot SHA-256: `{report.Transcription.Snapshot.Sha256}`",
            $"- Permission: {report.Transcription.License}",
            $"- Attribution: {report.Transcription.Attribution}",
            $"- Archived permission statement: {report.Transcription.PermissionUrl}",
            string.Empty,
            "## Partition boundary",
            string.Empty,
            $"- Evaluation: `{string.Join("`, `", report.EvaluationLoci)}`",
            $"- Holdout, unopened: `{string.Join("`, `", report.HoldoutLoci)}`",
            string.Empty,
            "## Counts",
            string.Empty,
            "| Locus | H-layer lines | Accepted tokens | Excluded uncertain tokens | Symbol occurrences |",
            "|---|---:|---:|---:|---:|",
        };
        lines.AddRange(report.Loci.Select(item =>
            $"| {item.Locus} | {item.SourceLines} | {item.AcceptedTokens} | " +
            $"{item.ExcludedTokens} | {item.SymbolOccurrences} |"));
        lines.Add(string.Empty);
        lines.Add($"Unique accepted EVA symbols: **{report.UniqueSymbols}**.");
        lines.Add($"Symbols: `{string.Join("`, `", report.Symbols)}`.");
        lines.Add($"Inventory SHA-256: `{report.InventorySha256}`.");
        lines.Add(string.Empty);
        lines.Add("Tokens containing `*`, `?`, extended characters, or other non-basic EVA symbols are " +
            "excluded under the frozen uncertain-token rule and retained in the machine-readable exclusions file.");
        WriteBytes(path, Encoding.UTF8.GetBytes(string.Join("\n", lines) + "\n"));
    }

    internal static void WriteBytes(string path, byte[] bytes)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private sealed class LocusAccumulator
    {
        public int SourceLines { get; set; }
        public int AcceptedTokens { get; set; }
        public int ExcludedTokens { get; set; }
        public int SymbolOccurrences { get; set; }
    }

    private sealed class SymbolAccumulator
    {
        public int Occurrences { get; set; }
        public HashSet<string> Loci { get; } = new(StringComparer.Ordinal);
    }
}
