using Enochian.Flow;
using Enochian.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DecoderFallbackException = System.Text.DecoderFallbackException;
using NormalizationForm = System.Text.NormalizationForm;
using UTF8Encoding = System.Text.UTF8Encoding;

namespace Enochian.Lexicons;

public partial class NormalizedLexicon(IConfigurable parent, IFlowResources resources) : Lexicon(parent, resources)
{
    private static readonly ILogger Logger = Logging.CreateLogger<NormalizedLexicon>();
    private static readonly JsonSerializerOptions ReportSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };
    private static readonly HashSet<string> AllowedProperties =
    [
        "$schema",
        "schema_version",
        "entry_id",
        "source_record_id",
        "language",
        "family",
        "source",
        "source_version",
        "lemma",
        "original_form",
        "form",
        "entry_kind",
        "dialect",
        "part_of_speech",
        "definition",
        "frequency",
        "source_encoding",
        "ipa",
        "unicode_normalization",
        "license",
    ];
    private static readonly HashSet<string> RequiredProperties =
    [
        "schema_version",
        "entry_id",
        "source_record_id",
        "language",
        "family",
        "source",
        "source_version",
        "lemma",
        "original_form",
        "form",
        "entry_kind",
        "dialect",
        "part_of_speech",
        "definition",
        "frequency",
        "source_encoding",
        "ipa",
        "unicode_normalization",
        "license",
    ];
    private static readonly HashSet<string> NormalizableFields =
    [
        "lemma",
        "form",
        "dialect",
        "part_of_speech",
        "definition",
        "ipa",
    ];

    private readonly HashSet<string> normalizeFields = new(StringComparer.Ordinal)
    {
        "lemma",
        "form",
        "dialect",
        "part_of_speech",
        "definition",
        "ipa",
    };

    public override ILogger Log => Logger;

    public string? ManifestPath { get; private set; }

    public string? QualityReportPath { get; private set; }

    public NormalizedLexiconQualityReport? QualityReport { get; private set; }

    public override IConfigurable Configure(JsonObject config)
    {
        _ = base.Configure(config);

        ManifestPath = config.Get<string>("manifest", this);
        if (string.IsNullOrWhiteSpace(ManifestPath))
        {
            _ = AddError("no 'manifest' specified");
        }

        QualityReportPath = config.Get<string>("qualityReport", this);
        if (string.IsNullOrWhiteSpace(QualityReportPath))
        {
            _ = AddError("no 'qualityReport' specified");
        }

        if (config.ContainsKey("normalizeFields"))
        {
            normalizeFields.Clear();
            foreach (var fieldName in config.GetList<string>("normalizeFields", this))
            {
                if (!NormalizableFields.Contains(fieldName))
                {
                    _ = AddError("invalid normalization field '{0}'", fieldName);
                }
                else
                {
                    _ = normalizeFields.Add(fieldName);
                }
            }
        }

        return this;
    }

    protected override IEnumerable<string?> GetCacheIdentityParts()
    {
        return new[]
        {
            string.IsNullOrWhiteSpace(ManifestPath) ? null : ResolvePath(ManifestPath),
            string.IsNullOrWhiteSpace(QualityReportPath) ? null : ResolvePath(QualityReportPath),
        }
            .Concat(normalizeFields.Order(StringComparer.Ordinal));
    }

    protected override bool IsCacheCurrent(FileInfo sourceInfo, FileInfo cacheInfo)
    {
        if (string.IsNullOrWhiteSpace(QualityReportPath))
        {
            return false;
        }

        var reportPath = ResolvePath(QualityReportPath);
        if (!File.Exists(reportPath) || File.GetLastWriteTimeUtc(reportPath) < sourceInfo.LastWriteTimeUtc)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(ManifestPath))
        {
            var manifestPath = ResolvePath(ManifestPath);
            if (!File.Exists(manifestPath) || File.GetLastWriteTimeUtc(manifestPath) > cacheInfo.LastWriteTimeUtc)
            {
                return false;
            }
        }

        return true;
    }

    protected override void OnCacheLoaded()
    {
        if (!string.IsNullOrWhiteSpace(QualityReportPath))
        {
            var reportPath = ResolvePath(QualityReportPath);
            QualityReport = JsonSerializer.Deserialize<NormalizedLexiconQualityReport>(
                File.ReadAllText(reportPath),
                ReportSerializerOptions);
        }
    }

    protected override void LoadLexicon(string path)
    {
        if (Features == null || Encoding == null)
        {
            _ = AddError("normalized lexicon requires configured features and encoding");
            return;
        }

        if (string.IsNullOrWhiteSpace(ManifestPath) || string.IsNullOrWhiteSpace(QualityReportPath))
        {
            return;
        }

        var manifestPath = ResolvePath(ManifestPath);
        if (!File.Exists(manifestPath))
        {
            _ = AddError("invalid manifest path '{0}'", manifestPath);
            return;
        }

        var encoder = new Encoder(Features, Encoding);
        var entries = new List<LexiconEntry>();
        var acceptedIds = new HashSet<string>(StringComparer.Ordinal);
        var rejectionReasons = new Dictionary<string, int>(StringComparer.Ordinal);
        var unknownSymbols = new Dictionary<string, int>(StringComparer.Ordinal);
        var rejections = new List<NormalizedLexiconRejection>();
        var totalRecords = 0;
        foreach (var sourceLine in ReadUtf8Lines(path))
        {
            if (totalRecords >= MaxEntriesToLoad)
            {
                break;
            }

            if (sourceLine.Error != null)
            {
                totalRecords++;
                Reject(Id ?? "unknown", sourceLine.Number, "invalid_unicode", sourceLine.Error, rejectionReasons, rejections);
                continue;
            }

            if (string.IsNullOrWhiteSpace(sourceLine.Text))
            {
                continue;
            }

            totalRecords++;
            ProcessLine(
                sourceLine.Text,
                sourceLine.Number,
                encoder,
                entries,
                acceptedIds,
                rejectionReasons,
                unknownSymbols,
                rejections);
        }

        SetEntries(entries);
        QualityReport = CreateReport(
            SourcePath ?? path,
            ManifestPath,
            totalRecords,
            entries,
            rejectionReasons,
            unknownSymbols,
            rejections);
        WriteReport(ResolvePath(QualityReportPath), QualityReport);

        Log.LogInformation(
            "loaded {Accepted} of {Total} normalized entries; rejected {Rejected}",
            QualityReport.AcceptedRecords,
            QualityReport.TotalRecords,
            QualityReport.RejectedRecords);
    }

    private void ProcessLine(
        string line,
        int lineNumber,
        Encoder encoder,
        List<LexiconEntry> entries,
        HashSet<string> acceptedIds,
        IDictionary<string, int> rejectionReasons,
        IDictionary<string, int> unknownSymbols,
        ICollection<NormalizedLexiconRejection> rejections)
    {
        string sourceId = Id ?? "unknown";
        try
        {
            using var document = JsonDocument.Parse(line);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                Reject(sourceId, lineNumber, "invalid_record", "record is not a JSON object", rejectionReasons, rejections);
                return;
            }

            var root = document.RootElement;
            if (root.TryGetProperty("source", out var sourceProperty) && sourceProperty.ValueKind == JsonValueKind.String)
            {
                sourceId = sourceProperty.GetString() ?? sourceId;
            }

            var propertyNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!propertyNames.Add(property.Name))
                {
                    Reject(sourceId, lineNumber, "duplicate_property", $"duplicate property '{property.Name}'", rejectionReasons, rejections);
                    return;
                }

                if (!AllowedProperties.Contains(property.Name))
                {
                    Reject(sourceId, lineNumber, "unknown_property", $"unknown property '{property.Name}'", rejectionReasons, rejections);
                    return;
                }
            }

            var missing = RequiredProperties.Where(property => !propertyNames.Contains(property)).Order(StringComparer.Ordinal).ToArray();
            if (missing.Length != 0)
            {
                Reject(sourceId, lineNumber, "missing_field", $"missing required field(s): {string.Join(", ", missing)}", rejectionReasons, rejections);
                return;
            }

            if (!TryCreateEntry(root, out var entry, out var validationError))
            {
                Reject(sourceId, lineNumber, "invalid_field", validationError, rejectionReasons, rejections);
                return;
            }

            if (!acceptedIds.Add(entry.EntryId))
            {
                Reject(sourceId, lineNumber, "duplicate_entry_id", $"duplicate entry_id '{entry.EntryId}'", rejectionReasons, rejections);
                return;
            }

            (_, entry.Encoded, entry.Phones) = EncodeIpa(encoder, entry.Ipa!, out var unknown);
            if (unknown.Count != 0)
            {
                foreach (var symbol in unknown)
                {
                    Increment(unknownSymbols, symbol);
                }

                Reject(sourceId, lineNumber, "unknown_ipa", $"unknown IPA segment(s): {string.Join(", ", unknown)}", rejectionReasons, rejections);
                return;
            }

            if (entry.Phones.Count == 0)
            {
                Reject(sourceId, lineNumber, "empty_phonology", "IPA produced no phonological segments", rejectionReasons, rejections);
                return;
            }

            entries.Add(entry);
        }
        catch (JsonException exception)
        {
            Reject(sourceId, lineNumber, "malformed_json", exception.Message, rejectionReasons, rejections);
        }
    }

    private static IEnumerable<NormalizedSourceLine> ReadUtf8Lines(string path)
    {
        var encoding = new UTF8Encoding(false, true);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var stream = new BufferedStream(file);
        var bytes = new List<byte>();
        var lineNumber = 0;

        while (stream.ReadByte() is var value && value >= 0)
        {
            if (value == '\n')
            {
                yield return DecodeLine(encoding, bytes, ++lineNumber);
                bytes.Clear();
            }
            else if (value != '\r')
            {
                bytes.Add((byte)value);
            }
        }

        if (bytes.Count != 0)
        {
            yield return DecodeLine(encoding, bytes, ++lineNumber);
        }
    }

    private static NormalizedSourceLine DecodeLine(UTF8Encoding encoding, List<byte> bytes, int lineNumber)
    {
        try
        {
            return new NormalizedSourceLine(lineNumber, encoding.GetString([.. bytes]), null);
        }
        catch (DecoderFallbackException exception)
        {
            return new NormalizedSourceLine(lineNumber, string.Empty, exception.Message);
        }
    }

    private bool TryCreateEntry(JsonElement root, out LexiconEntry entry, out string error)
    {
        entry = new LexiconEntry();
        error = string.Empty;

        if (!TryGetRequiredString(root, "schema_version", out var schemaVersion, out error)
            || !SchemaVersionRegex().IsMatch(schemaVersion))
        {
            error = string.IsNullOrEmpty(error) ? "schema_version must use supported major version 1" : error;
            return false;
        }

        if (!TryGetRequiredString(root, "entry_id", out var entryId, out error)
            || !EntryIdRegex().IsMatch(entryId)
            || !TryGetRequiredString(root, "source_record_id", out var sourceRecordId, out error)
            || !TryGetRequiredString(root, "language", out var language, out error)
            || !LanguageRegex().IsMatch(language)
            || !TryGetRequiredString(root, "family", out var family, out error)
            || !TryGetRequiredString(root, "source", out var source, out error)
            || !SourceIdRegex().IsMatch(source)
            || !TryGetRequiredString(root, "source_version", out var sourceVersion, out error)
            || !TryGetRequiredString(root, "lemma", out var lemma, out error)
            || !TryGetRequiredString(root, "original_form", out var originalForm, out error)
            || !TryGetRequiredString(root, "form", out var form, out error)
            || !TryGetRequiredString(root, "entry_kind", out var entryKindText, out error)
            || !TryParseEntryKind(entryKindText, out var entryKind)
            || !TryGetNullableString(root, "dialect", out var dialect, out error)
            || !TryGetNullableString(root, "part_of_speech", out var partOfSpeech, out error)
            || !TryGetNullableString(root, "definition", out var definition, out error)
            || !TryGetNullableDouble(root, "frequency", out var frequency, out error)
            || frequency < 0
            || !TryGetRequiredString(root, "source_encoding", out var sourceEncoding, out error)
            || !TryGetRequiredString(root, "ipa", out var ipa, out error)
            || !TryGetRequiredString(root, "unicode_normalization", out var unicodeNormalization, out error)
            || !string.Equals(unicodeNormalization, "NFC", StringComparison.Ordinal)
            || !TryGetRequiredString(root, "license", out var license, out error))
        {
            error = string.IsNullOrEmpty(error) ? "one or more fields have invalid values" : error;
            return false;
        }

        entry = new LexiconEntry
        {
            EntryId = entryId,
            Language = language,
            Family = family,
            SourceId = source,
            SourceVersion = sourceVersion,
            SourceRecordId = sourceRecordId,
            Text = originalForm,
            Lemma = Normalize("lemma", lemma)!,
            Form = Normalize("form", form)!,
            EntryKind = entryKind,
            Dialect = Normalize("dialect", dialect),
            PartOfSpeech = Normalize("part_of_speech", partOfSpeech),
            Frequency = frequency,
            SourceEncoding = sourceEncoding,
            Ipa = Normalize("ipa", ipa),
            Definition = Normalize("definition", definition) ?? string.Empty,
            License = license,
        };
        return true;
    }

    private static (string Text, string Encoded, IList<double[]> Phones) EncodeIpa(
        Encoder encoder,
        string ipa,
        out IReadOnlyList<string> unknownSymbols)
    {
        var (text, encoded, phones) = encoder.GetTextAndPhones(ipa, out unknownSymbols);
        return (text, string.IsNullOrEmpty(encoded) ? ipa : encoded, phones);
    }

    private static NormalizedLexiconQualityReport CreateReport(
        string sourcePath,
        string manifestPath,
        int totalRecords,
        List<LexiconEntry> entries,
        IDictionary<string, int> rejectionReasons,
        IDictionary<string, int> unknownSymbols,
        IReadOnlyCollection<NormalizedLexiconRejection> rejections)
    {
        var uniqueLemmas = entries.Select(entry => entry.Lemma).Distinct(StringComparer.Ordinal).Count();
        var uniqueForms = entries.Select(entry => entry.Form).Distinct(StringComparer.Ordinal).Count();
        var uniquePhonologies = entries.Select(entry => entry.Ipa).Distinct(StringComparer.Ordinal).Count();
        return new NormalizedLexiconQualityReport
        {
            SourcePath = sourcePath,
            ManifestPath = manifestPath,
            TotalRecords = totalRecords,
            AcceptedRecords = entries.Count,
            RejectedRecords = totalRecords - entries.Count,
            UniqueLemmas = uniqueLemmas,
            UniqueForms = uniqueForms,
            UniquePhonologies = uniquePhonologies,
            DuplicateLemmas = entries.Count - uniqueLemmas,
            DuplicateForms = entries.Count - uniqueForms,
            DuplicatePhonologies = entries.Count - uniquePhonologies,
            RejectionReasons = ToSortedDictionary(rejectionReasons),
            UnknownSymbols = ToSortedDictionary(unknownSymbols),
            PhonemeLengthHistogram = entries
                .GroupBy(entry => entry.Phones.Count)
                .OrderBy(group => group.Key)
                .ToDictionary(group => group.Key, group => group.Count()),
            Rejections = [.. rejections.OrderBy(rejection => rejection.Line)],
        };
    }

    private static void WriteReport(string path, NormalizedLexiconQualityReport report)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            var json = JsonSerializer.Serialize(report, ReportSerializerOptions).ReplaceLineEndings("\n") + "\n";
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
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

    private string ResolvePath(string path)
    {
        return GetChildPath(AbsoluteFilePath, path);
    }

    private string? Normalize(string fieldName, string? value)
    {
        return value != null && normalizeFields.Contains(fieldName)
            ? value.Normalize(NormalizationForm.FormC)
            : value;
    }

    private static bool TryGetRequiredString(JsonElement root, string propertyName, out string value, out string error)
    {
        var property = root.GetProperty(propertyName);
        if (property.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(property.GetString()))
        {
            value = string.Empty;
            error = $"{propertyName} must be a non-empty string";
            return false;
        }

        value = property.GetString()!;
        error = string.Empty;
        return true;
    }

    private static bool TryGetNullableString(JsonElement root, string propertyName, out string? value, out string error)
    {
        var property = root.GetProperty(propertyName);
        if (property.ValueKind == JsonValueKind.Null)
        {
            value = null;
            error = string.Empty;
            return true;
        }

        if (property.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(property.GetString()))
        {
            value = null;
            error = $"{propertyName} must be null or a non-empty string";
            return false;
        }

        value = property.GetString();
        error = string.Empty;
        return true;
    }

    private static bool TryGetNullableDouble(JsonElement root, string propertyName, out double? value, out string error)
    {
        var property = root.GetProperty(propertyName);
        if (property.ValueKind == JsonValueKind.Null)
        {
            value = null;
            error = string.Empty;
            return true;
        }

        if (property.ValueKind != JsonValueKind.Number || !property.TryGetDouble(out var number) || !double.IsFinite(number))
        {
            value = null;
            error = $"{propertyName} must be null or a finite number";
            return false;
        }

        value = number;
        error = string.Empty;
        return true;
    }

    private static bool TryParseEntryKind(string value, out LexiconEntryKind entryKind)
    {
        entryKind = value switch
        {
            "lemma" => LexiconEntryKind.Lemma,
            "inflected" => LexiconEntryKind.Inflected,
            "proper-name" => LexiconEntryKind.ProperName,
            "abbreviation" => LexiconEntryKind.Abbreviation,
            _ => default,
        };
        return value is "lemma" or "inflected" or "proper-name" or "abbreviation";
    }

    private static void Reject(
        string sourceId,
        int line,
        string reasonCode,
        string reason,
        IDictionary<string, int> rejectionReasons,
        ICollection<NormalizedLexiconRejection> rejections)
    {
        Increment(rejectionReasons, reasonCode);
        rejections.Add(new NormalizedLexiconRejection
        {
            SourceId = string.IsNullOrWhiteSpace(sourceId) ? "unknown" : sourceId,
            Line = line,
            ReasonCode = reasonCode,
            Reason = reason,
        });
    }

    private static void Increment(IDictionary<string, int> counts, string key)
    {
        _ = counts.TryGetValue(key, out var count);
        counts[key] = count + 1;
    }

    private static SortedDictionary<string, int> ToSortedDictionary(IDictionary<string, int> source)
    {
        return new SortedDictionary<string, int>(source, StringComparer.Ordinal);
    }

    [GeneratedRegex(@"^1\.[0-9]+\.[0-9]+$", RegexOptions.CultureInvariant)]
    private static partial Regex SchemaVersionRegex();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]*:[a-z]{2,3}(?:-[A-Za-z0-9]+)*:[A-Za-z0-9._~%-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex EntryIdRegex();

    [GeneratedRegex(@"^[a-z]{2,3}(?:-[A-Z][a-z]{3})?(?:-(?:[A-Z]{2}|[0-9]{3}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex LanguageRegex();

    [GeneratedRegex(@"^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex SourceIdRegex();

    private sealed record NormalizedSourceLine(int Number, string Text, string? Error);
}

public sealed class NormalizedLexiconQualityReport
{
    public string SchemaVersion { get; set; } = "1.0.0";
    public string SourcePath { get; set; } = string.Empty;
    public string ManifestPath { get; set; } = string.Empty;
    public int TotalRecords { get; set; }
    public int AcceptedRecords { get; set; }
    public int RejectedRecords { get; set; }
    public int UniqueLemmas { get; set; }
    public int UniqueForms { get; set; }
    public int UniquePhonologies { get; set; }
    public int DuplicateLemmas { get; set; }
    public int DuplicateForms { get; set; }
    public int DuplicatePhonologies { get; set; }
    public SortedDictionary<string, int> RejectionReasons { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> UnknownSymbols { get; set; } = new(StringComparer.Ordinal);
    public IDictionary<int, int> PhonemeLengthHistogram { get; set; } = new SortedDictionary<int, int>();
    public IList<NormalizedLexiconRejection> Rejections { get; set; } = [];
}

public sealed class NormalizedLexiconRejection
{
    public string SourceId { get; set; } = string.Empty;
    public int Line { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}
