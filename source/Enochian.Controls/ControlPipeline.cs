using Enochian.Provenance;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Enochian.Controls;

public sealed class ControlPipeline
{
    public const string AdapterVersion = "1.0.0";
    public const string ProviderVersion = "1.35.2";
    public const string ProfileVersion = "1.0.0";
    public const string TransformCommand = "dotnet run --project source/Enochian.Controls -- acquire-normalize";

    private static readonly JsonSerializerOptions LineOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
    private static readonly JsonSerializerOptions ReportOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };
    private readonly string repositoryRoot;
    private readonly IReadOnlyList<ControlManifest> manifests;

    public ControlPipeline(string repositoryRoot)
    {
        this.repositoryRoot = Path.GetFullPath(repositoryRoot);
        var manifestRoot = Path.Combine(this.repositoryRoot, "resources", "lexicons", "manifests");
        manifests =
        [
            ControlManifest.Load(Path.Combine(manifestRoot, "zemberek.manifest.json")),
            ControlManifest.Load(Path.Combine(manifestRoot, "magyar-ispell.manifest.json")),
        ];
    }

    public async Task<int> RunAsync(
        bool acquire,
        string pythonPath,
        CancellationToken cancellationToken = default)
    {
        using var client = new HttpClient();
        var acquirer = new ControlAcquirer(client);
        var worker = Path.Combine(repositoryRoot, "tools", "epitran", "convert.py");
        var provider = new EpitranProcessProvider(pythonPath, worker);
        var exitCode = 0;
        foreach (var manifest in manifests)
        {
            var rawPath = ControlAcquirer.Resolve(repositoryRoot, manifest.RawPath);
            if (acquire)
            {
                rawPath = await acquirer.AcquireAsync(manifest, repositoryRoot, cancellationToken);
            }

            if (!File.Exists(rawPath))
            {
                throw new FileNotFoundException($"{manifest.SourceId}: raw source is absent; run acquire-normalize.", rawPath);
            }

            var actualHash = ControlAcquirer.Hash(rawPath);
            if (!string.Equals(actualHash, manifest.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{manifest.SourceId}: local SHA-256 {actualHash} does not match {manifest.Sha256}.");
            }

            var source = LoadSource(manifest, rawPath);
            var report = Normalize(manifest, source, provider, sampleSize: 100);
            Console.WriteLine(
                $"{manifest.SourceId}: emitted {report.EmittedRecords}; excluded {report.ExcludedRecords}; " +
                $"prepared {report.ReviewRecords} review rows; confirmatory eligible: {report.ConfirmatoryEligible}.");
            if (!report.ConfirmatoryEligible)
            {
                exitCode = 1;
            }
        }

        return exitCode;
    }

    public ControlQualityReport Normalize(
        ControlManifest manifest,
        ControlSourceResult source,
        IControlIpaProvider provider,
        int sampleSize,
        string? outputDirectory = null)
    {
        var normalizedPath = ControlAcquirer.Resolve(repositoryRoot, manifest.GeneratedArtifactPath);
        var directory = outputDirectory ?? Path.GetDirectoryName(normalizedPath)
            ?? throw new InvalidDataException("Generated artifact path has no directory.");
        normalizedPath = Path.Combine(directory, Path.GetFileName(normalizedPath));
        var prefix = Path.Combine(directory, manifest.SourceId);
        var conversionPath = prefix + ".conversions.jsonl";
        var auditPath = prefix + ".g2p-audit.json";
        var reviewPath = prefix + ".review.jsonl";
        var qualityPath = prefix + ".quality.json";
        var profileId = manifest.Language == "tur" ? "tur-Latn" : "hun-Latn";
        provider.Convert(profileId, manifest.SourceId, manifest.Language, source.Lemmas, conversionPath);

        var audit = CreateAuditor().Audit(conversionPath, GetProfilePath(profileId), sampleSize);
        IpaArtifactAuditor.WriteSummary(auditPath, audit.Summary);
        IpaArtifactAuditor.WriteReviewSheet(reviewPath, audit.ReviewRows);
        var rejectedIds = audit.Summary.Issues
            .Where(issue => issue.RecordId != null)
            .Select(issue => issue.RecordId!)
            .ToHashSet(StringComparer.Ordinal);
        var artifacts = ReadArtifacts(conversionPath).ToDictionary(artifact => artifact.RecordId, StringComparer.Ordinal);
        var normalized = source.Lemmas
            .Where(lemma => !rejectedIds.Contains(lemma.RecordId))
            .Select(lemma => CreateNormalized(manifest, profileId, lemma, artifacts[lemma.RecordId]))
            .OrderBy(entry => entry.EntryId, StringComparer.Ordinal)
            .ToArray();
        WriteJsonLines(normalizedPath, normalized);

        var exclusionCounts = source.Rejections
            .GroupBy(rejection => rejection.Category, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var issueGroup in audit.Summary.Issues.GroupBy(issue => issue.Code, StringComparer.Ordinal))
        {
            exclusionCounts[issueGroup.Key] = issueGroup.Count();
        }

        var blockers = new List<string>();
        if (audit.Summary.RejectedRecords != 0)
        {
            blockers.Add("unknown_or_incomplete_ipa");
        }

        if (audit.ReviewRows.Any(row => string.Equals(row.Decision, "pending", StringComparison.Ordinal)))
        {
            blockers.Add("pending_blinded_review");
        }

        if (audit.ReviewRows.Count < sampleSize)
        {
            blockers.Add("insufficient_review_sample");
        }

        var report = new ControlQualityReport
        {
            AdapterVersion = AdapterVersion,
            SourceId = manifest.SourceId,
            SourceVersion = manifest.Revision,
            InputSha256 = manifest.Sha256,
            Language = manifest.Language,
            ProfileId = profileId,
            ProfileVersion = ProfileVersion,
            ProviderVersion = ProviderVersion,
            TransformCommand = TransformCommand,
            SourceRecords = source.Lemmas.Count + source.Rejections.Count,
            LemmaRecords = source.Lemmas.Count,
            GeneratedMorphologyRecords = 0,
            EmittedRecords = normalized.Length,
            ExcludedRecords = source.Rejections.Count + audit.Summary.RejectedRecords,
            ReviewRecords = audit.ReviewRows.Count,
            UnknownIpaRate = source.Lemmas.Count == 0 ? 0 : (double)audit.Summary.RejectedRecords / source.Lemmas.Count,
            ConfirmatoryEligible = blockers.Count == 0,
            EligibilityBlockers = blockers,
            ExclusionCounts = new SortedDictionary<string, int>(exclusionCounts, StringComparer.Ordinal),
            UnknownIpaSegments = new SortedDictionary<string, int>(
                audit.Summary.UnknownSegments.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
                StringComparer.Ordinal),
            SourceRejections = source.Rejections,
        };
        WriteReport(qualityPath, report);
        return report;
    }

    private ControlSourceResult LoadSource(ControlManifest manifest, string rawPath)
    {
        if (manifest.SourceId == "zemberek")
        {
            return ZemberekDictionaryAdapter.Parse(rawPath);
        }

        var extractionRoot = Path.Combine(repositoryRoot, ".enoch", "controls", "magyarispell-v1.9.1");
        var dictionaryRoot = ControlAcquirer.ExtractMagyar(rawPath, extractionRoot);
        return MagyarIspellAdapter.Parse(dictionaryRoot);
    }

    private IpaArtifactAuditor CreateAuditor()
    {
        var flow = new Flow.Flow(Path.Combine(repositoryRoot, "samples", "ipatransducer.json"));
        var errors = flow.Errors.Select(error => error.Message).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        var features = flow.FeatureSets.Single(featureSet => featureSet.Id == "Default");
        var encoding = flow.Encodings.Single(candidate => candidate.Id == "IPA");
        var schemas = Path.Combine(repositoryRoot, "resources", "lexicons", "schemas");
        return new(
            Path.Combine(schemas, "ipa-conversion-artifact.schema.json"),
            Path.Combine(schemas, "ipa-conversion-profile.schema.json"),
            new Text.Encoder(features, encoding));
    }

    private string GetProfilePath(string profileId) =>
        Path.Combine(repositoryRoot, "resources", "lexicons", "profiles", $"epitran-{profileId}.profile.json");

    private static IEnumerable<IpaConversionArtifact> ReadArtifacts(string path)
    {
        foreach (var line in File.ReadLines(path, new UTF8Encoding(false, true)))
        {
            yield return JsonSerializer.Deserialize<IpaConversionArtifact>(line, LineOptions)
                ?? throw new InvalidDataException("Unable to deserialize Epitran artifact.");
        }
    }

    private static ControlNormalizedEntry CreateNormalized(
        ControlManifest manifest,
        string profileId,
        ControlSourceLemma lemma,
        IpaConversionArtifact artifact)
    {
        return new()
        {
            EntryId = $"{manifest.SourceId}:{manifest.Language}:{Uri.EscapeDataString(lemma.RecordId)}",
            SourceRecordId = lemma.RecordId,
            Language = manifest.Language,
            Family = manifest.Family,
            Source = manifest.SourceId,
            SourceVersion = manifest.Revision,
            Lemma = lemma.NormalizedForm,
            OriginalForm = lemma.OriginalForm,
            Form = lemma.NormalizedForm,
            PartOfSpeech = lemma.PartOfSpeech,
            SourceEncoding = "Latin",
            Ipa = artifact.Ipa,
            License = manifest.License,
            IpaConversion = new()
            {
                SourceForm = lemma.OriginalForm,
                NormalizedForm = lemma.NormalizedForm,
                GeneratedIpa = artifact.Ipa,
                ProviderId = "epitran",
                ProviderVersion = ProviderVersion,
                ProfileId = profileId,
                ProfileVersion = ProfileVersion,
            },
        };
    }

    private static void WriteJsonLines<T>(string path, IEnumerable<T> records)
    {
        WriteAtomically(path, temporary =>
        {
            using var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)) { NewLine = "\n" };
            foreach (var record in records)
            {
                writer.WriteLine(JsonSerializer.Serialize(record, LineOptions));
            }
        });
    }

    private static void WriteReport(string path, ControlQualityReport report)
    {
        WriteAtomically(path, temporary =>
            File.WriteAllText(temporary, JsonSerializer.Serialize(report, ReportOptions).ReplaceLineEndings("\n") + "\n", new UTF8Encoding(false)));
    }

    private static void WriteAtomically(string path, Action<string> write)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            write(temporary);
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
}
