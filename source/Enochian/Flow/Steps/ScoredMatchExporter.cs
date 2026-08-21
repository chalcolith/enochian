using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Enochian.Flow.Steps;

public sealed record ScoredMatchRecord(
    string SchemaVersion,
    string RecordId,
    string ConfigurationId,
    string QueryId,
    string QueryText,
    int QueryPhonemeLength,
    string LexiconId,
    string SourceId,
    string Language,
    string Family,
    string CandidateId,
    string CandidateLemma,
    string? CandidateForm,
    int CandidatePhonemeLength,
    double RawCost,
    int PathLength,
    double MeanPathCost,
    double MeanInputLengthCost,
    int WithinLexiconRank);

public sealed record ScoredMatchDefinition(
    string CandidateId,
    string Definition);

public sealed record ScoredMatchExportMetadata(
    string SchemaId,
    string SchemaVersion,
    string SchemaSha256,
    string SoftwareSha256,
    string ConfigurationSha256,
    string ConfigurationId,
    int RecordCount);

public sealed class ScoredMatchExportOptions
{
    public string Jsonl { get; init; } = string.Empty;
    public string Csv { get; init; } = string.Empty;
    public string Metadata { get; init; } = string.Empty;
    public string Schema { get; init; } = string.Empty;
    public string? Definitions { get; init; }
}

public static class ScoredMatchExporter
{
    public const string SchemaId = "https://chalcolith.github.io/enochian/schemas/scored-match-1.json";
    public const string SchemaVersion = "1.0.0";

    private static readonly JsonSerializerOptions LineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    private static readonly JsonSerializerOptions MetadataOptions = new(LineOptions)
    {
        WriteIndented = true,
    };

    private static readonly string[] CsvHeader =
    [
        "schema_version",
        "record_id",
        "configuration_id",
        "query_id",
        "query_text",
        "query_phoneme_length",
        "lexicon_id",
        "source_id",
        "language",
        "family",
        "candidate_id",
        "candidate_lemma",
        "candidate_form",
        "candidate_phoneme_length",
        "raw_cost",
        "path_length",
        "mean_path_cost",
        "mean_input_length_cost",
        "within_lexicon_rank",
    ];

    public static void Write(
        ScoredMatchExportOptions options,
        IEnumerable<ScoredMatchRecord> records,
        IEnumerable<ScoredMatchDefinition> definitions,
        string configurationId,
        string configurationPath,
        string softwarePath)
    {
        ArgumentNullException.ThrowIfNull(options);
        var orderedRecords = records
            .OrderBy(record => record.QueryId, StringComparer.Ordinal)
            .ThenBy(record => record.LexiconId, StringComparer.Ordinal)
            .ThenBy(record => record.WithinLexiconRank)
            .ThenBy(record => record.CandidateId, StringComparer.Ordinal)
            .ToArray();

        WriteJsonLines(options.Jsonl, orderedRecords);
        WriteCsv(options.Csv, orderedRecords);
        if (!string.IsNullOrWhiteSpace(options.Definitions))
        {
            WriteJsonLines(options.Definitions, definitions
                .GroupBy(definition => definition.CandidateId, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(definition => definition.CandidateId, StringComparer.Ordinal));
        }

        var metadata = new ScoredMatchExportMetadata(
            SchemaId,
            SchemaVersion,
            HashFile(options.Schema),
            HashFile(softwarePath),
            HashFile(configurationPath),
            configurationId,
            orderedRecords.Length);
        WriteJson(options.Metadata, metadata);
    }

    private static void WriteJsonLines<T>(string path, IEnumerable<T> records) =>
        WriteAtomically(path, temporary =>
        {
            using var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)) { NewLine = "\n" };
            foreach (var record in records)
            {
                writer.WriteLine(JsonSerializer.Serialize(record, LineOptions));
            }
        });

    private static void WriteCsv(string path, IEnumerable<ScoredMatchRecord> records) =>
        WriteAtomically(path, temporary =>
        {
            using var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)) { NewLine = "\r\n" };
            writer.WriteLine(string.Join(',', CsvHeader));
            foreach (var record in records)
            {
                writer.WriteLine(string.Join(',', GetCsvFields(record).Select(QuoteCsv)));
            }
        });

    private static IEnumerable<string> GetCsvFields(ScoredMatchRecord record)
    {
        yield return record.SchemaVersion;
        yield return record.RecordId;
        yield return record.ConfigurationId;
        yield return record.QueryId;
        yield return record.QueryText;
        yield return record.QueryPhonemeLength.ToString(CultureInfo.InvariantCulture);
        yield return record.LexiconId;
        yield return record.SourceId;
        yield return record.Language;
        yield return record.Family;
        yield return record.CandidateId;
        yield return record.CandidateLemma;
        yield return record.CandidateForm ?? string.Empty;
        yield return record.CandidatePhonemeLength.ToString(CultureInfo.InvariantCulture);
        yield return record.RawCost.ToString("R", CultureInfo.InvariantCulture);
        yield return record.PathLength.ToString(CultureInfo.InvariantCulture);
        yield return record.MeanPathCost.ToString("R", CultureInfo.InvariantCulture);
        yield return record.MeanInputLengthCost.ToString("R", CultureInfo.InvariantCulture);
        yield return record.WithinLexiconRank.ToString(CultureInfo.InvariantCulture);
    }

    private static string QuoteCsv(string value)
    {
        return value.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? value
            : '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }

    private static void WriteJson<T>(string path, T value) =>
        WriteAtomically(path, temporary => File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(value, MetadataOptions).ReplaceLineEndings("\n") + "\n",
            new UTF8Encoding(false)));

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void WriteAtomically(string path, Action<string> write)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

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
