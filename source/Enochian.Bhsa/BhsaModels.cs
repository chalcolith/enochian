namespace Enochian.Bhsa;

public sealed class BhsaOccurrence
{
    public string SchemaVersion { get; init; } = string.Empty;
    public string CorpusLabel { get; init; } = string.Empty;
    public string SourceRecordId { get; init; } = string.Empty;
    public string LexemeId { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string? Lexeme { get; init; }
    public string? VocalizedForm { get; init; }
    public string? Gloss { get; init; }
    public string? PartOfSpeech { get; init; }
    public string? Phono { get; init; }
    public int? SourceFrequency { get; init; }
    public int? Rank { get; init; }
}

public sealed record BhsaLexeme(
    string LexemeId,
    string Lexeme,
    string VocalizedForm,
    string? Gloss,
    string? PartOfSpeech,
    int Frequency,
    int? Rank,
    IReadOnlyList<BhsaReading> Readings);

public sealed record BhsaReading(string Ipa, int Frequency);

public sealed record BhsaRejection(string SourceRecordId, string Category, string Reason);

public sealed record BhsaSourceResult(
    IReadOnlyList<BhsaLexeme> Lexemes,
    IReadOnlyList<BhsaRejection> Rejections,
    int Occurrences);

public sealed class BhsaNormalizedEntry
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public string EntryId { get; init; } = string.Empty;
    public string SourceRecordId { get; init; } = string.Empty;
    public string Language { get; init; } = "hbo";
    public string Family { get; init; } = "Afro-Asiatic/Semitic";
    public string Source { get; init; } = "bhsa";
    public string SourceVersion { get; init; } = string.Empty;
    public string Lemma { get; init; } = string.Empty;
    public string OriginalForm { get; init; } = string.Empty;
    public string Form { get; init; } = string.Empty;
    public string EntryKind { get; init; } = "lemma";
    public string Dialect { get; init; } = "Biblical Hebrew";
    public string? PartOfSpeech { get; init; }
    public string? Definition { get; init; }
    public double Frequency { get; init; }
    public int? Rank { get; init; }
    public string SourceEncoding { get; init; } = "Hebrew";
    public string Ipa { get; init; } = string.Empty;
    public BhsaIpaProvenance IpaConversion { get; init; } = new();
    public string UnicodeNormalization { get; init; } = "NFC";
    public string License { get; init; } = "CC-BY-NC-4.0";
}

public sealed class BhsaIpaProvenance
{
    public string SourceForm { get; init; } = string.Empty;
    public string NormalizedForm { get; init; } = string.Empty;
    public string GeneratedIpa { get; init; } = string.Empty;
    public string ProviderId { get; init; } = "etcbc-phono";
    public string ProviderVersion { get; init; } = "2.1";
    public string ProfileId { get; init; } = "hbo-etcbc-phono";
    public string ProfileVersion { get; init; } = "1.0.0";
    public string Status { get; init; } = "complete";
}

public sealed class BhsaQualityReport
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public string CorpusLabel { get; init; } = "Biblical Hebrew";
    public string SourceId { get; init; } = "bhsa";
    public string SourceVersion { get; init; } = string.Empty;
    public string AdapterVersion { get; init; } = string.Empty;
    public string ProfileId { get; init; } = "hbo-etcbc-phono";
    public string ProfileVersion { get; init; } = "1.0.0";
    public int OccurrenceRecords { get; init; }
    public int UniqueLexemes { get; init; }
    public int EmittedLexemes { get; init; }
    public int ConversionRecords { get; init; }
    public int RejectedRecords { get; init; }
    public int MultipleReadingLexemes { get; init; }
    public int ReviewRecords { get; init; }
    public bool ConfirmatoryEligible { get; init; }
    public IReadOnlyList<string> EligibilityBlockers { get; init; } = [];
    public IReadOnlyDictionary<string, int> RejectionReasons { get; init; } =
        new SortedDictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlyList<BhsaRejection> Rejections { get; init; } = [];
}
