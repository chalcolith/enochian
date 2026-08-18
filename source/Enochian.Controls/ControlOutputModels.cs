namespace Enochian.Controls;

public sealed class ControlNormalizedEntry
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public string EntryId { get; init; } = string.Empty;
    public string SourceRecordId { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Family { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string SourceVersion { get; init; } = string.Empty;
    public string Lemma { get; init; } = string.Empty;
    public string OriginalForm { get; init; } = string.Empty;
    public string Form { get; init; } = string.Empty;
    public string EntryKind { get; init; } = "lemma";
    public string? Transliteration { get; init; }
    public string? Dialect { get; init; }
    public string? PartOfSpeech { get; init; }
    public string? Definition { get; init; }
    public double? Frequency { get; init; }
    public string SourceEncoding { get; init; } = string.Empty;
    public string Ipa { get; init; } = string.Empty;
    public ControlIpaProvenance IpaConversion { get; init; } = new();
    public string UnicodeNormalization { get; init; } = "NFC";
    public string License { get; init; } = string.Empty;
}

public sealed class ControlIpaProvenance
{
    public string SourceForm { get; init; } = string.Empty;
    public string NormalizedForm { get; init; } = string.Empty;
    public string GeneratedIpa { get; init; } = string.Empty;
    public string ProviderId { get; init; } = string.Empty;
    public string ProviderVersion { get; init; } = string.Empty;
    public string ProfileId { get; init; } = string.Empty;
    public string ProfileVersion { get; init; } = string.Empty;
    public string Status { get; init; } = "complete";
}

public sealed class ControlQualityReport
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public string AdapterVersion { get; init; } = string.Empty;
    public string SourceId { get; init; } = string.Empty;
    public string SourceVersion { get; init; } = string.Empty;
    public string InputSha256 { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string ProfileId { get; init; } = string.Empty;
    public string ProfileVersion { get; init; } = string.Empty;
    public string ProviderVersion { get; init; } = string.Empty;
    public string TransformCommand { get; init; } = string.Empty;
    public int SourceRecords { get; init; }
    public int LemmaRecords { get; init; }
    public int GeneratedMorphologyRecords { get; init; }
    public int InflectedFormRecords { get; init; }
    public int EmittedRecords { get; init; }
    public int ExcludedRecords { get; init; }
    public int ReviewRecords { get; init; }
    public double UnknownIpaRate { get; init; }
    public bool ConfirmatoryEligible { get; init; }
    public IReadOnlyList<string> EligibilityBlockers { get; init; } = [];
    public IReadOnlyDictionary<string, int> ExclusionCounts { get; init; } = new SortedDictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, int> UnknownIpaSegments { get; init; } = new SortedDictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlyList<ControlSourceRejection> SourceRejections { get; init; } = [];
}

public sealed class ControlInflectedEntry
{
    public string SchemaVersion { get; init; } = "1.0.0";
    public string EntryId { get; init; } = string.Empty;
    public string SourceRecordId { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public string SourceVersion { get; init; } = string.Empty;
    public string Language { get; init; } = string.Empty;
    public string Lemma { get; init; } = string.Empty;
    public string Form { get; init; } = string.Empty;
    public string EntryKind { get; init; } = "inflected_form";
    public string Script { get; init; } = string.Empty;
    public string? Transliteration { get; init; }
    public IReadOnlyList<string> Features { get; init; } = [];
    public string UnicodeNormalization { get; init; } = "NFC";
    public string License { get; init; } = string.Empty;
}
