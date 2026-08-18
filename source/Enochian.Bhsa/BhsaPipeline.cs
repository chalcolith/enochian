using Enochian.Provenance;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Enochian.Bhsa;

public sealed class BhsaPipeline(string repositoryRoot)
{
    public const string AdapterVersion = "1.0.0";
    public const string SourceVersion = "v1.8.1 (b112c161cfd21eae403d51a2733740d8743460e7), TF 2021";

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
    private readonly string repositoryRoot = Path.GetFullPath(repositoryRoot);

    public BhsaQualityReport Normalize(string occurrencePath, string outputDirectory, int sampleSize)
    {
        var source = BhsaOccurrenceAdapter.Parse(occurrencePath);
        var conversionPath = Path.Combine(outputDirectory, "bhsa.conversions.jsonl");
        var normalizedPath = Path.Combine(outputDirectory, "bhsa.jsonl");
        var auditPath = Path.Combine(outputDirectory, "bhsa.g2p-audit.json");
        var reviewPath = Path.Combine(outputDirectory, "bhsa.review.jsonl");
        var qualityPath = Path.Combine(outputDirectory, "bhsa.quality.json");
        var artifacts = CreateArtifacts(source.Lexemes).ToArray();
        WriteJsonLines(conversionPath, artifacts);

        var audit = CreateAuditor().Audit(conversionPath, GetProfilePath(), sampleSize);
        IpaArtifactAuditor.WriteSummary(auditPath, audit.Summary);
        IpaArtifactAuditor.WriteReviewSheet(reviewPath, audit.ReviewRows);
        var rejectedIds = audit.Summary.Issues
            .Where(issue => issue.RecordId != null)
            .Select(issue => issue.RecordId!)
            .ToHashSet(StringComparer.Ordinal);
        var normalized = CreateNormalizedEntries(
            source.Lexemes,
            artifacts.ToDictionary(artifact => artifact.RecordId, StringComparer.Ordinal),
            rejectedIds).ToArray();
        WriteJsonLines(normalizedPath, normalized);

        var blockers = new List<string>();
        if (audit.Summary.RejectedRecords != 0)
        {
            blockers.Add("unknown_or_incomplete_ipa");
        }

        if (audit.ReviewRows.Any(row => row.Decision == "pending"))
        {
            blockers.Add("pending_blinded_review");
        }

        if (audit.ReviewRows.Count < sampleSize)
        {
            blockers.Add("insufficient_review_sample");
        }

        var rejections = source.Rejections.Concat(audit.Summary.Issues.Select(issue =>
            new BhsaRejection(issue.RecordId ?? issue.Line.ToString(CultureInfo.InvariantCulture), issue.Code, issue.Message)))
            .OrderBy(rejection => rejection.SourceRecordId, StringComparer.Ordinal)
            .ThenBy(rejection => rejection.Category, StringComparer.Ordinal)
            .ToArray();
        var report = new BhsaQualityReport
        {
            SourceVersion = SourceVersion,
            AdapterVersion = AdapterVersion,
            OccurrenceRecords = source.Occurrences,
            UniqueLexemes = source.Lexemes.Count,
            EmittedLexemes = normalized.Length,
            ConversionRecords = artifacts.Length,
            RejectedRecords = rejections.Length,
            MultipleReadingLexemes = source.Lexemes.Count(lexeme => lexeme.Readings.Count > 1),
            ReviewRecords = audit.ReviewRows.Count,
            ConfirmatoryEligible = blockers.Count == 0,
            EligibilityBlockers = blockers,
            RejectionReasons = new SortedDictionary<string, int>(
                rejections.GroupBy(rejection => rejection.Category, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                StringComparer.Ordinal),
            Rejections = rejections,
        };
        WriteReport(qualityPath, report);
        return report;
    }

    private static IEnumerable<IpaConversionArtifact> CreateArtifacts(IEnumerable<BhsaLexeme> lexemes)
    {
        foreach (var lexeme in lexemes.OrderBy(lexeme => lexeme.LexemeId, StringComparer.Ordinal))
        {
            for (var index = 0; index < lexeme.Readings.Count; index++)
            {
                var conversion = EtcbcPhonoConverter.Convert(lexeme.Readings[index].Ipa);
                var diagnostics = new List<IpaConversionDiagnostic>(conversion.Diagnostics)
                {
                    new()
                    {
                        Code = "corpus_label",
                        Message = "Biblical Hebrew",
                    },
                };
                if (lexeme.Readings.Count > 1)
                {
                    diagnostics.Add(new()
                    {
                        Code = "multiple_observed_reading",
                        Message = $"Preserved observed ETCBC phono reading {index + 1} of {lexeme.Readings.Count}.",
                    });
                }

                yield return new()
                {
                    Schema = "ipa-conversion-artifact.schema.json",
                    SchemaVersion = "1.0.0",
                    RecordId = CreateReadingId(lexeme.LexemeId, index),
                    Source = "bhsa",
                    Language = "hbo",
                    SourceForm = lexeme.VocalizedForm,
                    NormalizedForm = lexeme.VocalizedForm,
                    Ipa = conversion.Ipa,
                    ProviderId = "etcbc-phono",
                    ProviderVersion = "2.1",
                    ProfileId = "hbo-etcbc-phono",
                    ProfileVersion = "1.0.0",
                    Status = conversion.UnknownSymbols.Count == 0 ? "complete" : "incomplete",
                    Diagnostics = diagnostics,
                };
            }
        }
    }

    private static IEnumerable<BhsaNormalizedEntry> CreateNormalizedEntries(
        IEnumerable<BhsaLexeme> lexemes,
        Dictionary<string, IpaConversionArtifact> artifacts,
        HashSet<string> rejectedIds)
    {
        foreach (var lexeme in lexemes.OrderBy(lexeme => lexeme.LexemeId, StringComparer.Ordinal))
        {
            var selected = Enumerable.Range(0, lexeme.Readings.Count)
                .FirstOrDefault(index => !rejectedIds.Contains(CreateReadingId(lexeme.LexemeId, index)), -1);
            if (selected < 0)
            {
                continue;
            }

            var readingId = CreateReadingId(lexeme.LexemeId, selected);
            var artifact = artifacts[readingId];
            yield return new()
            {
                EntryId = $"bhsa:hbo:{Uri.EscapeDataString(lexeme.LexemeId)}",
                SourceRecordId = lexeme.LexemeId,
                SourceVersion = SourceVersion,
                Lemma = lexeme.Lexeme,
                OriginalForm = lexeme.VocalizedForm,
                Form = lexeme.VocalizedForm,
                PartOfSpeech = lexeme.PartOfSpeech,
                Definition = lexeme.Gloss,
                Frequency = lexeme.Frequency,
                Rank = lexeme.Rank,
                Ipa = artifact.Ipa,
                IpaConversion = new()
                {
                    SourceForm = lexeme.VocalizedForm,
                    NormalizedForm = lexeme.VocalizedForm,
                    GeneratedIpa = artifact.Ipa,
                },
            };
        }
    }

    private IpaArtifactAuditor CreateAuditor()
    {
        var flow = new Flow.Flow(Path.Combine(repositoryRoot, "samples", "ipatransducer.json"));
        var errors = flow.Errors.Select(error => error.Message).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        var schemas = Path.Combine(repositoryRoot, "resources", "lexicons", "schemas");
        return new(
            Path.Combine(schemas, "ipa-conversion-artifact.schema.json"),
            Path.Combine(schemas, "ipa-conversion-profile.schema.json"),
            new Text.Encoder(
                flow.FeatureSets.Single(featureSet => featureSet.Id == "Default"),
                flow.Encodings.Single(encoding => encoding.Id == "IPA")));
    }

    private string GetProfilePath() =>
        Path.Combine(repositoryRoot, "resources", "lexicons", "profiles", "bhsa-phono.profile.json");

    private static string CreateReadingId(string lexemeId, int index) =>
        $"{lexemeId}.reading-{index + 1}";

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

    private static void WriteReport(string path, BhsaQualityReport report)
    {
        WriteAtomically(path, temporary => File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(report, ReportOptions).ReplaceLineEndings("\n") + "\n",
            new UTF8Encoding(false)));
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
