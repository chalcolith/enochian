using Enochian.Provenance;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Enochian.Perseus;

public sealed class PerseusPipeline
{
    public const string AdapterVersion = "1.0.0";
    public const string ProviderId = "enochian-latin";
    public const string ProviderVersion = "1.0.0";
    public const string ProfileId = "lat-classical-restored";
    public const string ProfileVersion = "1.0.0";
    public const string TransformCommand = "dotnet run --project source/Enochian.Perseus -- acquire-normalize";

    private static readonly JsonSerializerOptions JsonLinesOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };
    private static readonly JsonSerializerOptions ReportOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private readonly string repositoryRoot;
    private readonly PerseusManifest manifest;

    public PerseusPipeline(string repositoryRoot)
    {
        this.repositoryRoot = Path.GetFullPath(repositoryRoot);
        manifest = PerseusManifest.Load(Path.Combine(
            this.repositoryRoot,
            "resources",
            "lexicons",
            "manifests",
            "perseus-lewis-short.manifest.json"));
    }

    public async Task<int> RunAsync(bool acquire, CancellationToken cancellationToken = default)
    {
        if (acquire)
        {
            using var client = new HttpClient();
            await new PerseusAcquirer(client).AcquireAsync(manifest, repositoryRoot, cancellationToken);
        }

        var paths = GetOutputPaths();
        var report = Normalize(
            PerseusAcquirer.ResolvePath(repositoryRoot, manifest.RawPath),
            paths.NormalizedPath,
            paths.ConversionPath,
            paths.QualityPath,
            paths.AuditPath,
            paths.ReviewPath,
            sampleSize: 100);
        Console.WriteLine(
            $"Normalized {report.EmittedRecords} Latin lemmas; rejected {report.RejectedRecords}; " +
            $"prepared {report.ReviewRecords} blinded review rows.");
        return report.RejectedRecords == 0 && report.ReviewRecords == 100 ? 0 : 1;
    }

    public LatinQualityReport Normalize(
        string inputPath,
        string normalizedPath,
        string conversionPath,
        string qualityPath,
        string auditPath,
        string reviewPath,
        int sampleSize)
    {
        var lemmas = PerseusTeiAdapter.Parse(inputPath);
        var normalized = new List<LatinNormalizedEntry>();
        var artifacts = new List<IpaConversionArtifact>();
        var rejections = new List<LatinRejection>();
        var unknownGraphemes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var assumedShortRecords = 0;
        var assumedShortVowels = 0;

        foreach (var lemma in lemmas)
        {
            var conversion = ClassicalLatinConverter.Convert(lemma.NormalizedForm);
            var diagnostics = new List<IpaConversionDiagnostic>();
            if (conversion.AssumedShortVowels > 0)
            {
                assumedShortRecords++;
                assumedShortVowels += conversion.AssumedShortVowels;
                diagnostics.Add(new IpaConversionDiagnostic
                {
                    Code = "assumed_short_vowel",
                    Message = $"Applied the conservative short-vowel assumption to {conversion.AssumedShortVowels} nucleus/nuclei.",
                });
            }

            foreach (var grapheme in conversion.UnknownGraphemes)
            {
                Increment(unknownGraphemes, grapheme);
                diagnostics.Add(new IpaConversionDiagnostic
                {
                    Code = "unconverted_grapheme",
                    Message = "No restored-Classical profile rule for grapheme.",
                    Text = grapheme,
                });
            }

            artifacts.Add(CreateArtifact(lemma, conversion, diagnostics));
            if (!conversion.IsComplete)
            {
                rejections.Add(new LatinRejection
                {
                    SourceRecordId = lemma.RecordId,
                    ReasonCode = "unknown_grapheme",
                    Reason = $"Unsupported grapheme(s): {string.Join(", ", conversion.UnknownGraphemes)}",
                });
                continue;
            }

            normalized.Add(CreateNormalizedEntry(lemma, conversion));
        }

        normalized.Sort((left, right) => StringComparer.Ordinal.Compare(left.EntryId, right.EntryId));
        artifacts.Sort((left, right) => StringComparer.Ordinal.Compare(left.RecordId, right.RecordId));
        WriteJsonLines(normalizedPath, normalized);
        WriteJsonLines(conversionPath, artifacts);

        var audit = CreateAuditor().Audit(conversionPath, GetProfilePath(), sampleSize);
        IpaArtifactAuditor.WriteSummary(auditPath, audit.Summary);
        IpaArtifactAuditor.WriteReviewSheet(reviewPath, audit.ReviewRows);
        var report = new LatinQualityReport
        {
            AdapterId = "perseus-tei",
            AdapterVersion = AdapterVersion,
            SourceId = manifest.SourceId,
            SourceVersion = manifest.Revision,
            InputSha256 = PerseusAcquirer.HashFile(inputPath),
            TransformCommand = TransformCommand,
            PronunciationConvention = "Restored Classical Latin",
            ProfileId = ProfileId,
            ProfileVersion = ProfileVersion,
            ParsedRecords = lemmas.Count,
            EmittedRecords = normalized.Count,
            RejectedRecords = rejections.Count,
            AssumedShortRecords = assumedShortRecords,
            AssumedShortVowels = assumedShortVowels,
            UnknownGraphemes = unknownGraphemes,
            ReviewRecords = audit.ReviewRows.Count,
            RejectionReasons = new SortedDictionary<string, int>(
                rejections.GroupBy(rejection => rejection.ReasonCode, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                StringComparer.Ordinal),
            Rejections = [.. rejections.OrderBy(rejection => rejection.SourceRecordId, StringComparer.Ordinal)],
        };
        WriteReport(qualityPath, report);
        return report;
    }

    private IpaArtifactAuditor CreateAuditor()
    {
        var flow = new Enochian.Flow.Flow(Path.Combine(repositoryRoot, "samples", "ipatransducer.json"));
        var errors = flow.Errors.Select(error => error.Message).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        var features = flow.FeatureSets.Single(featureSet => featureSet.Id == "Default");
        var encoding = flow.Encodings.Single(candidate =>
            string.Equals(candidate.Id, "IPA", StringComparison.OrdinalIgnoreCase));
        var schemas = Path.Combine(repositoryRoot, "resources", "lexicons", "schemas");
        return new IpaArtifactAuditor(
            Path.Combine(schemas, "ipa-conversion-artifact.schema.json"),
            Path.Combine(schemas, "ipa-conversion-profile.schema.json"),
            new Enochian.Text.Encoder(features, encoding));
    }

    private IpaConversionArtifact CreateArtifact(
        PerseusLemma lemma,
        LatinConversion conversion,
        IReadOnlyList<IpaConversionDiagnostic> diagnostics)
    {
        return new IpaConversionArtifact
        {
            Schema = "ipa-conversion-artifact.schema.json",
            SchemaVersion = "1.0.0",
            RecordId = lemma.RecordId,
            Source = manifest.SourceId,
            Language = "lat",
            SourceForm = lemma.OriginalForm,
            NormalizedForm = lemma.NormalizedForm,
            Ipa = conversion.Ipa,
            ProviderId = ProviderId,
            ProviderVersion = ProviderVersion,
            ProfileId = ProfileId,
            ProfileVersion = ProfileVersion,
            Status = conversion.IsComplete ? "complete" : "incomplete",
            Diagnostics = diagnostics,
        };
    }

    private LatinNormalizedEntry CreateNormalizedEntry(PerseusLemma lemma, LatinConversion conversion)
    {
        return new LatinNormalizedEntry
        {
            SchemaVersion = "1.0.0",
            EntryId = $"{manifest.SourceId}:lat:{Uri.EscapeDataString(lemma.RecordId)}",
            SourceRecordId = lemma.RecordId,
            Language = "lat",
            Family = "Indo-European/Italic",
            Source = manifest.SourceId,
            SourceVersion = manifest.Revision,
            Lemma = lemma.NormalizedForm,
            OriginalForm = lemma.OriginalForm,
            Form = lemma.NormalizedForm,
            EntryKind = "lemma",
            PartOfSpeech = lemma.PartOfSpeech,
            Definition = lemma.Definition,
            SourceEncoding = "Latin",
            Ipa = conversion.Ipa,
            IpaConversion = new LatinIpaProvenance
            {
                SourceForm = lemma.OriginalForm,
                NormalizedForm = lemma.NormalizedForm,
                GeneratedIpa = conversion.Ipa,
                ProviderId = ProviderId,
                ProviderVersion = ProviderVersion,
                ProfileId = ProfileId,
                ProfileVersion = ProfileVersion,
                Status = "complete",
            },
            UnicodeNormalization = "NFC",
            License = manifest.License,
        };
    }

    private LatinOutputPaths GetOutputPaths()
    {
        var normalizedPath = PerseusAcquirer.ResolvePath(repositoryRoot, manifest.GeneratedArtifactPath);
        var directory = Path.GetDirectoryName(normalizedPath)
            ?? throw new InvalidDataException("Generated artifact path has no directory.");
        return new LatinOutputPaths(
            normalizedPath,
            Path.Combine(directory, "perseus-lewis-short.conversions.jsonl"),
            Path.Combine(directory, "perseus-lewis-short.quality.json"),
            Path.Combine(directory, "perseus-lewis-short.g2p-audit.json"),
            Path.Combine(directory, "perseus-lewis-short.review.jsonl"));
    }

    private string GetProfilePath()
    {
        return Path.Combine(
            repositoryRoot,
            "resources",
            "lexicons",
            "profiles",
            "latin-classical-restored.profile.json");
    }

    private static void WriteJsonLines<T>(string path, IEnumerable<T> records)
    {
        WriteAtomically(path, temporaryPath =>
        {
            using var writer = new StreamWriter(temporaryPath, false, new UTF8Encoding(false)) { NewLine = "\n" };
            foreach (var record in records)
            {
                writer.WriteLine(JsonSerializer.Serialize(record, JsonLinesOptions));
            }
        });
    }

    private static void WriteReport(string path, LatinQualityReport report)
    {
        WriteAtomically(path, temporaryPath =>
        {
            var json = JsonSerializer.Serialize(report, ReportOptions).ReplaceLineEndings("\n") + "\n";
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
        });
    }

    private static void WriteAtomically(string path, Action<string> write)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            write(temporaryPath);
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void Increment(SortedDictionary<string, int> counts, string key)
    {
        _ = counts.TryGetValue(key, out var count);
        counts[key] = count + 1;
    }

    private sealed record LatinOutputPaths(
        string NormalizedPath,
        string ConversionPath,
        string QualityPath,
        string AuditPath,
        string ReviewPath);
}

public sealed class LatinNormalizedEntry
{
    public string SchemaVersion { get; set; } = string.Empty;
    public string EntryId { get; set; } = string.Empty;
    public string SourceRecordId { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Family { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public string Lemma { get; set; } = string.Empty;
    public string OriginalForm { get; set; } = string.Empty;
    public string Form { get; set; } = string.Empty;
    public string EntryKind { get; set; } = string.Empty;
    public string? Dialect { get; set; }
    public string? PartOfSpeech { get; set; }
    public string? Definition { get; set; }
    public double? Frequency { get; set; }
    public string SourceEncoding { get; set; } = string.Empty;
    public string Ipa { get; set; } = string.Empty;
    public LatinIpaProvenance IpaConversion { get; set; } = new();
    public string UnicodeNormalization { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
}

public sealed class LatinIpaProvenance
{
    public string SourceForm { get; set; } = string.Empty;
    public string NormalizedForm { get; set; } = string.Empty;
    public string GeneratedIpa { get; set; } = string.Empty;
    public string ProviderId { get; set; } = string.Empty;
    public string ProviderVersion { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileVersion { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public sealed class LatinQualityReport
{
    public string SchemaVersion { get; set; } = "1.0.0";
    public string AdapterId { get; set; } = string.Empty;
    public string AdapterVersion { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public string InputSha256 { get; set; } = string.Empty;
    public string TransformCommand { get; set; } = string.Empty;
    public string PronunciationConvention { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public string ProfileVersion { get; set; } = string.Empty;
    public int ParsedRecords { get; set; }
    public int EmittedRecords { get; set; }
    public int RejectedRecords { get; set; }
    public int AssumedShortRecords { get; set; }
    public int AssumedShortVowels { get; set; }
    public int ReviewRecords { get; set; }
    public SortedDictionary<string, int> UnknownGraphemes { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> RejectionReasons { get; set; } = new(StringComparer.Ordinal);
    public IReadOnlyList<LatinRejection> Rejections { get; set; } = [];
}

public sealed class LatinRejection
{
    public string SourceRecordId { get; set; } = string.Empty;
    public string ReasonCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
