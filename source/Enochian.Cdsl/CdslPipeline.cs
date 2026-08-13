using Enochian.Lexicons;
using System.Text.Json;

namespace Enochian.Cdsl;

public sealed class CdslPipeline
{
    public const string AdapterVersion = "1.0.0";
    public const string TransformCommand = "dotnet run --project source/Enochian.Cdsl -- acquire-normalize";
    public const int ShsComparisonTolerance = 0;

    private static readonly JsonSerializerOptions ReportSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private readonly string repositoryRoot;
    private readonly IReadOnlyList<CdslManifest> manifests;
    private readonly CdslOrigAdapter adapter;

    public CdslPipeline(string repositoryRoot)
    {
        this.repositoryRoot = Path.GetFullPath(repositoryRoot);
        manifests = CdslManifest.LoadAll(Path.Combine(this.repositoryRoot, "resources", "lexicons", "manifests"));

        var flowPath = Path.Combine(this.repositoryRoot, "resources", "lexicons", "cdsl-normalization.flow.json");
        var flow = new Enochian.Flow.Flow(flowPath);
        var errors = flow.Errors.Select(error => error.Message).Where(message => !string.IsNullOrWhiteSpace(message)).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        var features = flow.FeatureSets.Single(featureSet => string.Equals(featureSet.Id, "Default", StringComparison.Ordinal));
        var slp1 = flow.Encodings.Single(encoding => string.Equals(encoding.Id, "SLP1", StringComparison.Ordinal));
        adapter = new CdslOrigAdapter(features, slp1);
    }

    public async Task<int> RunAsync(bool acquire, CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();
        var acquirer = new CdslAcquirer(httpClient);

        foreach (var manifest in manifests)
        {
            if (acquire)
            {
                Console.WriteLine($"Acquiring {manifest.SourceId} at {manifest.Revision}...");
                await acquirer.AcquireAsync(manifest, repositoryRoot, cancellationToken);
            }

            var rawPath = ResolvePath(manifest.RawPath);
            if (!File.Exists(rawPath))
            {
                throw new FileNotFoundException(
                    $"{manifest.SourceId}: raw source is absent; run acquire-normalize.",
                    rawPath);
            }

            var actualHash = CdslAcquirer.HashFile(rawPath);
            if (!string.Equals(actualHash, manifest.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{manifest.SourceId}: local SHA-256 {actualHash} does not match manifest {manifest.Sha256}.");
            }

            var outputPath = ResolvePath(manifest.GeneratedArtifactPath);
            var reportPath = Path.ChangeExtension(outputPath, ".quality.json");
            Console.WriteLine($"Normalizing {manifest.SourceId}...");
            var report = adapter.Normalize(manifest, rawPath, outputPath, reportPath, TransformCommand);
            Console.WriteLine(
                $"  wrote {report.EmittedRecords} records; rejected {report.RejectedRecords}; unknown SLP1 symbols {report.UnknownSlp1Symbols.Count}");

            var unreviewedUnknowns = report.Rejections
                .Where(rejection => string.Equals(rejection.ReasonCode, "unknown_slp1", StringComparison.Ordinal))
                .Where(rejection => !IsReviewedUnknown(rejection))
                .ToArray();
            if (unreviewedUnknowns.Length != 0)
            {
                throw new InvalidDataException(
                    $"{manifest.SourceId}: {unreviewedUnknowns.Length} unknown SLP1 rejection(s) require review.");
            }
        }

        return 0;
    }

    public int WriteCorpusReport()
    {
        var panel = LoadFlow("samples/sanskrit-panel.json");
        var normalizedLexicons = panel.Lexicons.OfType<NormalizedLexicon>().ToArray();
        var entries = normalizedLexicons.SelectMany(lexicon => lexicon.Entries).ToArray();
        ThrowForFlowErrors(panel);

        var unknownSources = normalizedLexicons
            .Where(lexicon => lexicon.QualityReport?.UnknownSymbols.Count > 0)
            .Select(lexicon => lexicon.Id ?? lexicon.SourcePath ?? "unknown")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (unknownSources.Length != 0)
        {
            throw new InvalidDataException($"Unknown IPA symbols remain in: {string.Join(", ", unknownSources)}.");
        }

        var outputDirectory = Path.Combine(repositoryRoot, ".enoch", "cdsl-generated");
        var builder = new SanskritCorpusBuilder(SanskritCorpusFilters.Primary);
        var corpus = builder.Build(entries);
        SanskritCorpusBuilder.Write(Path.Combine(outputDirectory, "sanskrit-corpus-report.json"), corpus.Report);

        var legacyFlow = LoadFlow("samples/voynich.json");
        var legacy = legacyFlow.Lexicons.Single(lexicon => string.Equals(lexicon.Id, "SHS", StringComparison.Ordinal));
        var normalized = normalizedLexicons.Single(lexicon => string.Equals(lexicon.Id, "CDSL-SHS", StringComparison.Ordinal));
        var legacyEntries = legacy.Entries.ToArray();
        ThrowForFlowErrors(legacyFlow);
        var explanations = BuildShsExplanations(legacyEntries, normalized.Entries);
        var comparison = SanskritCorpusBuilder.CompareShs(
            legacyEntries,
            normalized.Entries,
            ShsComparisonTolerance,
            explanations);
        SanskritCorpusBuilder.Write(Path.Combine(outputDirectory, "shs-comparison-report.json"), comparison);
        if (comparison.UnexplainedAboveTolerance != 0)
        {
            throw new InvalidDataException(
                $"SHS comparison has {comparison.UnexplainedAboveTolerance} unexplained discrepancy(s) above tolerance {ShsComparisonTolerance}.");
        }

        Console.WriteLine($"Wrote Sanskrit corpus report with {corpus.Report.UnionCount} union entries.");
        Console.WriteLine($"Wrote SHS comparison report with {comparison.Discrepancies.Count} explained discrepancy(s).");
        return 0;
    }

    private static bool IsReviewedUnknown(CdslAdapterRejection rejection)
    {
        return string.Equals(rejection.SourceId, "cdsl-ap", StringComparison.Ordinal)
            && string.Equals(rejection.SourceRecordId, "6082.002", StringComparison.Ordinal)
            && string.Equals(rejection.Reason, "Unknown SLP1 symbol(s): V", StringComparison.Ordinal);
    }

    private Dictionary<string, string> BuildShsExplanations(
        IReadOnlyCollection<LexiconEntry> legacyEntries,
        IEnumerable<LexiconEntry> normalizedEntries)
    {
        var normalized = normalizedEntries.ToArray();
        var explanations = new Dictionary<string, string>(StringComparer.Ordinal);
        var shsManifest = manifests.Single(manifest => string.Equals(manifest.DictionaryCode, "shs", StringComparison.Ordinal));
        var adapterReportPath = Path.ChangeExtension(ResolvePath(shsManifest.GeneratedArtifactPath), ".quality.json");
        if (File.Exists(adapterReportPath))
        {
            var report = JsonSerializer.Deserialize<CdslAdapterQualityReport>(
                File.ReadAllText(adapterReportPath),
                ReportSerializerOptions);
            foreach (var rejection in report?.Rejections.Where(rejection => rejection.SourceRecordId != null) ?? [])
            {
                explanations[rejection.SourceRecordId!] = $"Normalized adapter rejection ({rejection.ReasonCode}): {rejection.Reason}";
            }
        }

        var legacyIds = legacyEntries.Select(entry => entry.SourceRecordId).ToHashSet(StringComparer.Ordinal);
        var normalizedIds = normalized.Select(entry => entry.SourceRecordId).ToHashSet(StringComparer.Ordinal);
        foreach (var legacyId in legacyIds)
        {
            if (normalizedIds.Any(normalizedId => normalizedId.StartsWith(legacyId + ".", StringComparison.Ordinal)))
            {
                _ = explanations.TryAdd(legacyId, "Legacy SHS truncates decimal source record IDs at the first non-digit.");
            }
        }

        var legacyEncodedForms = legacyEntries.Select(entry => entry.Lemma).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in normalized)
        {
            var decimalSeparator = entry.SourceRecordId.IndexOf('.', StringComparison.Ordinal);
            if (decimalSeparator > 0 && legacyIds.Contains(entry.SourceRecordId[..decimalSeparator]))
            {
                _ = explanations.TryAdd(entry.SourceRecordId, "Legacy SHS truncates decimal source record IDs at the first non-digit.");
            }
            else if (legacyEncodedForms.Contains(entry.Form))
            {
                _ = explanations.TryAdd(entry.SourceRecordId, "Legacy SHS collapses records with the same encoded phonological form.");
            }
        }

        var legacySnapshotPath = Path.Combine(repositoryRoot, "resources", "lexicons", "shstxt", "shs.txt");
        using var reader = new StreamReader(legacySnapshotPath);
        var legacySnapshotRecords = CdslOrigAdapter.ParseRecords(reader, "legacy-shs")
            .Where(result => result.Record != null)
            .Select(result => result.Record!)
            .GroupBy(record => record.RecordId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var entry in normalized.Where(entry => !explanations.ContainsKey(entry.SourceRecordId)))
        {
            if (legacySnapshotRecords.TryGetValue(entry.SourceRecordId, out var legacyRecord)
                && !string.Equals(legacyRecord.Headword, entry.Text, StringComparison.Ordinal))
            {
                explanations[entry.SourceRecordId] =
                    $"Pinned csl-orig changes the SLP1 headword from '{legacyRecord.Headword}' to '{entry.Text}'; the legacy loader collapsed the old form.";
            }
            else if (entry.SourceRecordId.Contains('.', StringComparison.Ordinal)
                && !legacySnapshotRecords.ContainsKey(entry.SourceRecordId))
            {
                explanations[entry.SourceRecordId] = "Pinned csl-orig adds this supplemental decimal record; it is absent from the legacy snapshot.";
            }
        }

        return explanations;
    }

    private Enochian.Flow.Flow LoadFlow(string relativePath)
    {
        var flow = new Enochian.Flow.Flow(ResolvePath(relativePath));
        ThrowForFlowErrors(flow);
        return flow;
    }

    private static void ThrowForFlowErrors(Enochian.Flow.Flow flow)
    {
        var errors = flow.Errors.Select(error => error.Message).Where(message => !string.IsNullOrWhiteSpace(message)).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }
    }

    private string ResolvePath(string relativePath)
    {
        return Path.GetFullPath(Path.Combine(
            repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
