using Enochian.Text;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using PhonologicalEncoder = Enochian.Text.Encoder;

namespace Enochian.Cdsl;

public sealed partial class CdslOrigAdapter(FeatureSet features, Enochian.Text.Encoding slp1Encoding)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false,
    };
    private static readonly JsonSerializerOptions ReportSerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    private readonly PhonologicalEncoder encoder = new(features, slp1Encoding);
    private readonly IReadOnlyList<EncodingPattern> ipaPatterns =
        [.. slp1Encoding.Patterns
            .Where(pattern => pattern.IsReplacement && !string.IsNullOrEmpty(pattern.Ipa))
            .OrderByDescending(pattern => pattern.Input.Length)
            .ThenBy(pattern => pattern.Input, StringComparer.Ordinal)];

    public CdslAdapterQualityReport Normalize(
        CdslManifest manifest,
        string inputPath,
        string outputPath,
        string reportPath,
        string command)
    {
        var profile = CdslMarkupProfile.Create(manifest.DictionaryCode);
        var records = new List<CdslNormalizedEntry>();
        var rejections = new List<CdslAdapterRejection>();
        var unknownSymbols = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var parsedRecords = 0;

        using (var reader = new StreamReader(inputPath, new UTF8Encoding(false, true), true))
        {
            foreach (var result in ParseRecords(reader, manifest.SourceId))
            {
                if (result.Rejection != null)
                {
                    rejections.Add(result.Rejection);
                    continue;
                }

                parsedRecords++;
                var record = result.Record!;
                if (!TryConvert(record.Headword, out var display, out var ipa, out var unknown))
                {
                    foreach (var symbol in unknown)
                    {
                        Increment(unknownSymbols, symbol);
                    }

                    rejections.Add(new CdslAdapterRejection
                    {
                        SourceId = manifest.SourceId,
                        Line = record.Line,
                        SourceRecordId = record.RecordId,
                        ReasonCode = "unknown_slp1",
                        Reason = $"Unknown SLP1 symbol(s): {string.Join(", ", unknown)}",
                    });
                    continue;
                }

                var entryId = $"{manifest.SourceId}:san:{Uri.EscapeDataString(record.RecordId)}";
                records.Add(new CdslNormalizedEntry
                {
                    SchemaVersion = "1.0.0",
                    EntryId = entryId,
                    SourceRecordId = record.RecordId,
                    Language = "san",
                    Family = "Indo-European/Indo-Aryan",
                    Source = manifest.SourceId,
                    SourceVersion = manifest.Revision,
                    Lemma = record.Headword.Normalize(NormalizationForm.FormC),
                    OriginalForm = record.Headword,
                    Form = display,
                    EntryKind = "lemma",
                    Definition = profile.CleanDefinition(record.Body),
                    SourceEncoding = "SLP1",
                    Ipa = ipa.Normalize(NormalizationForm.FormC),
                    UnicodeNormalization = "NFC",
                    License = manifest.License,
                });
            }
        }

        records.Sort((left, right) => StringComparer.Ordinal.Compare(left.EntryId, right.EntryId));
        WriteJsonLines(outputPath, records);

        var report = new CdslAdapterQualityReport
        {
            AdapterId = "cdsl-orig",
            AdapterVersion = CdslPipeline.AdapterVersion,
            SourceId = manifest.SourceId,
            SourceVersion = manifest.Revision,
            InputSha256 = manifest.Sha256,
            TransformCommand = command,
            ParsedRecords = parsedRecords,
            EmittedRecords = records.Count,
            RejectedRecords = rejections.Count,
            UnknownSlp1Symbols = unknownSymbols,
            RejectionReasons = new SortedDictionary<string, int>(
                rejections.GroupBy(rejection => rejection.ReasonCode, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                StringComparer.Ordinal),
            Rejections = [.. rejections
                .OrderBy(rejection => rejection.Line)
                .ThenBy(rejection => rejection.SourceRecordId, StringComparer.Ordinal)],
        };
        WriteReport(reportPath, report);
        return report;
    }

    public static IEnumerable<CdslRecordResult> ParseRecords(TextReader reader, string sourceId)
    {
        var lineNumber = 0;
        CdslRecordBuilder? current = null;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (line.StartsWith("<L>", StringComparison.Ordinal))
            {
                if (current != null)
                {
                    yield return RejectIncomplete(sourceId, current, "record began before the previous LEND");
                }

                current = TryParseHeader(line, lineNumber, out var headerError);
                if (current == null)
                {
                    yield return new CdslRecordResult(null, new CdslAdapterRejection
                    {
                        SourceId = sourceId,
                        Line = lineNumber,
                        ReasonCode = "malformed_header",
                        Reason = headerError,
                    });
                }

                continue;
            }

            if (line.StartsWith("<LEND>", StringComparison.Ordinal))
            {
                if (current == null)
                {
                    yield return new CdslRecordResult(null, new CdslAdapterRejection
                    {
                        SourceId = sourceId,
                        Line = lineNumber,
                        ReasonCode = "orphan_lend",
                        Reason = "LEND appeared without an active record.",
                    });
                }
                else
                {
                    yield return new CdslRecordResult(current.Build(), null);
                    current = null;
                }

                continue;
            }

            current?.Body.Add(line);
        }

        if (current != null)
        {
            yield return RejectIncomplete(sourceId, current, "record reached end of file without LEND");
        }
    }

    private static CdslRecordBuilder? TryParseHeader(string line, int lineNumber, out string error)
    {
        var recordId = ExtractTagValue(line, "L");
        var headword = ExtractTagValue(line, "k1");
        var display = ExtractTagValue(line, "k2");
        if (string.IsNullOrWhiteSpace(recordId) || string.IsNullOrWhiteSpace(headword) || display == null)
        {
            error = "Header must contain non-empty L and k1 tags and a k2 tag.";
            return null;
        }

        error = string.Empty;
        return new CdslRecordBuilder(recordId, headword, display, lineNumber);
    }

    private static string? ExtractTagValue(string line, string tag)
    {
        var marker = $"<{tag}>";
        var start = line.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = line.IndexOf('<', start);
        return end < 0 ? line[start..] : line[start..end];
    }

    private bool TryConvert(
        string source,
        out string display,
        out string ipa,
        out IReadOnlyList<string> unknownSymbols)
    {
        (_, display, _) = encoder.GetTextAndPhones(source, out unknownSymbols);
        if (unknownSymbols.Count != 0)
        {
            ipa = string.Empty;
            return false;
        }

        var result = new StringBuilder();
        var position = 0;
        while (position < source.Length)
        {
            var pattern = ipaPatterns.FirstOrDefault(pattern => source.AsSpan(position).StartsWith(pattern.Input, StringComparison.Ordinal));
            if (pattern == null)
            {
                ipa = string.Empty;
                unknownSymbols = [source[position].ToString()];
                return false;
            }

            _ = result.Append(pattern.Ipa);
            position += pattern.Input.Length;
        }

        ipa = result.ToString();
        return true;
    }

    private static CdslRecordResult RejectIncomplete(string sourceId, CdslRecordBuilder record, string reason)
    {
        return new CdslRecordResult(null, new CdslAdapterRejection
        {
            SourceId = sourceId,
            Line = record.Line,
            SourceRecordId = record.RecordId,
            ReasonCode = "incomplete_record",
            Reason = reason,
        });
    }

    private static void WriteJsonLines(string path, IEnumerable<CdslNormalizedEntry> records)
    {
        WriteAtomically(path, temporaryPath =>
        {
            using var writer = new StreamWriter(temporaryPath, false, new UTF8Encoding(false)) { NewLine = "\n" };
            foreach (var record in records)
            {
                writer.WriteLine(JsonSerializer.Serialize(record, SerializerOptions));
            }
        });
    }

    private static void WriteReport(string path, CdslAdapterQualityReport report)
    {
        WriteAtomically(path, temporaryPath =>
        {
            var json = JsonSerializer.Serialize(report, ReportSerializerOptions).ReplaceLineEndings("\n") + "\n";
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

    private sealed class CdslRecordBuilder(string recordId, string headword, string displayForm, int line)
    {
        public string RecordId { get; } = recordId;
        public string Headword { get; } = headword;
        public string DisplayForm { get; } = displayForm;
        public int Line { get; } = line;
        public IList<string> Body { get; } = [];

        public CdslRecord Build()
        {
            return new CdslRecord(RecordId, Headword, DisplayForm, string.Join("\n", Body), Line);
        }
    }
}

public sealed record CdslRecord(string RecordId, string Headword, string DisplayForm, string Body, int Line);

public sealed record CdslRecordResult(CdslRecord? Record, CdslAdapterRejection? Rejection);

public sealed class CdslNormalizedEntry
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
    public string UnicodeNormalization { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
}

public sealed class CdslAdapterQualityReport
{
    public string SchemaVersion { get; set; } = "1.0.0";
    public string AdapterId { get; set; } = string.Empty;
    public string AdapterVersion { get; set; } = string.Empty;
    public string SourceId { get; set; } = string.Empty;
    public string SourceVersion { get; set; } = string.Empty;
    public string InputSha256 { get; set; } = string.Empty;
    public string TransformCommand { get; set; } = string.Empty;
    public int ParsedRecords { get; set; }
    public int EmittedRecords { get; set; }
    public int RejectedRecords { get; set; }
    public SortedDictionary<string, int> UnknownSlp1Symbols { get; set; } = new(StringComparer.Ordinal);
    public SortedDictionary<string, int> RejectionReasons { get; set; } = new(StringComparer.Ordinal);
    public IList<CdslAdapterRejection> Rejections { get; set; } = [];
}

public sealed class CdslAdapterRejection
{
    public string SourceId { get; set; } = string.Empty;
    public int Line { get; set; }
    public string? SourceRecordId { get; set; }
    public string ReasonCode { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public abstract partial class CdslMarkupProfile
{
    public static CdslMarkupProfile Create(string dictionaryCode)
    {
        return dictionaryCode switch
        {
            "mw" => new MwMarkupProfile(),
            "ap" => new ApMarkupProfile(),
            "pw" => new PwMarkupProfile(),
            "pwg" => new PwgMarkupProfile(),
            "shs" => new ShsMarkupProfile(),
            _ => throw new ArgumentOutOfRangeException(nameof(dictionaryCode), dictionaryCode, "Unsupported CDSL dictionary."),
        };
    }

    public virtual string CleanDisplay(string value)
    {
        return CleanCommon(value) ?? string.Empty;
    }

    public abstract string? CleanDefinition(string value);

    protected static string? CleanBraceMarkup(string value)
    {
        value = SanskritMarkupRegex().Replace(value, "$1");
        value = TranslationMarkupRegex().Replace(value, "$1");
        value = value.Replace("{@", string.Empty, StringComparison.Ordinal)
            .Replace("@}", string.Empty, StringComparison.Ordinal);
        return CleanCommon(value);
    }

    protected static string? CleanCommon(string value)
    {
        value = LbodyRegex().Replace(value, string.Empty);
        value = XmlTagRegex().Replace(value, string.Empty);
        value = WebUtility.HtmlDecode(value);
        value = WhitespaceRegex().Replace(value, " ").Trim();
        return string.IsNullOrEmpty(value) ? null : value.Normalize(NormalizationForm.FormC);
    }

    [GeneratedRegex(@"\{#(.*?)#\}", RegexOptions.CultureInvariant)]
    private static partial Regex SanskritMarkupRegex();

    [GeneratedRegex(@"\{%(.*?)%\}", RegexOptions.CultureInvariant)]
    private static partial Regex TranslationMarkupRegex();

    [GeneratedRegex(@"\{\{Lbody=.*?\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex LbodyRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.CultureInvariant)]
    private static partial Regex XmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}

public sealed class MwMarkupProfile : CdslMarkupProfile
{
    public override string? CleanDefinition(string value)
    {
        return CleanCommon(value);
    }
}

public sealed class ApMarkupProfile : CdslMarkupProfile
{
    public override string? CleanDefinition(string value)
    {
        return CleanBraceMarkup(value);
    }
}

public sealed class PwMarkupProfile : CdslMarkupProfile
{
    public override string? CleanDefinition(string value)
    {
        return CleanBraceMarkup(value);
    }
}

public sealed class PwgMarkupProfile : CdslMarkupProfile
{
    public override string? CleanDefinition(string value)
    {
        return CleanBraceMarkup(value);
    }
}

public sealed class ShsMarkupProfile : CdslMarkupProfile
{
    public override string? CleanDefinition(string value)
    {
        return CleanBraceMarkup(value);
    }
}
