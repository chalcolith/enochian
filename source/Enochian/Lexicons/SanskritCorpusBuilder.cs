using System.Text;
using System.Text.Json;

namespace Enochian.Lexicons;

public sealed class SanskritCorpusBuilder(SanskritCorpusFilters filters)
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public SanskritCorpusResult Build(IEnumerable<LexiconEntry> sourceEntries)
    {
        var entries = sourceEntries
            .OrderBy(entry => entry.SourceId, StringComparer.Ordinal)
            .ThenBy(entry => entry.EntryId, StringComparer.Ordinal)
            .ToArray();
        var exclusions = entries
            .Select(entry => (Entry: entry, Reason: GetExclusionReason(entry)))
            .Where(result => result.Reason != null)
            .ToArray();
        var included = entries.Except(exclusions.Select(result => result.Entry)).ToArray();
        var unionEntries = included
            .GroupBy(entry => new SanskritCorpusKey(entry.Ipa!, entry.Lemma), SanskritCorpusKeyComparer.Instance)
            .Select(group => new SanskritUnionEntry(
                group.Key.Ipa,
                group.Key.Lemma,
                [.. group.Select(entry => new SanskritSourceMembership(
                        entry.SourceId,
                        entry.SourceRecordId,
                        entry.EntryId))
                    .OrderBy(membership => membership.SourceId, StringComparer.Ordinal)
                    .ThenBy(membership => membership.SourceRecordId, StringComparer.Ordinal)
                    .ThenBy(membership => membership.EntryId, StringComparer.Ordinal)]))
            .OrderBy(entry => entry.Ipa, StringComparer.Ordinal)
            .ThenBy(entry => entry.Lemma, StringComparer.Ordinal)
            .ToArray();
        var sourceIds = entries.Select(entry => entry.SourceId).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        return new SanskritCorpusResult(
            unionEntries,
            new SanskritCorpusReport(
                sourceIds.ToDictionary(
                    sourceId => sourceId,
                    sourceId => entries.Count(entry => string.Equals(entry.SourceId, sourceId, StringComparison.Ordinal)),
                    StringComparer.Ordinal),
                BuildOverlapMatrix(sourceIds, unionEntries),
                entries.Length,
                included.Length,
                unionEntries.Length,
                exclusions
                    .GroupBy(result => result.Reason!, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal)));
    }

    public static void Write(string path, SanskritCorpusReport report)
    {
        WriteJson(path, report);
    }

    public static ShsComparisonReport CompareShs(
        IEnumerable<LexiconEntry> legacyEntries,
        IEnumerable<LexiconEntry> normalizedEntries,
        int tolerance,
        IReadOnlyDictionary<string, string> explanations)
    {
        var legacyIds = legacyEntries.Select(entry => entry.SourceRecordId).ToHashSet(StringComparer.Ordinal);
        var normalizedIds = normalizedEntries.Select(entry => entry.SourceRecordId).ToHashSet(StringComparer.Ordinal);
        var discrepancies = legacyIds
            .Except(normalizedIds, StringComparer.Ordinal)
            .Select(sourceRecordId => new ShsDiscrepancy(sourceRecordId, "legacy_only", explanations.GetValueOrDefault(sourceRecordId)))
            .Concat(normalizedIds
                .Except(legacyIds, StringComparer.Ordinal)
                .Select(sourceRecordId => new ShsDiscrepancy(sourceRecordId, "normalized_only", explanations.GetValueOrDefault(sourceRecordId))))
            .OrderBy(discrepancy => discrepancy.SourceRecordId, StringComparer.Ordinal)
            .ThenBy(discrepancy => discrepancy.Side, StringComparer.Ordinal)
            .ToArray();
        var unexplained = discrepancies.Count(discrepancy => string.IsNullOrWhiteSpace(discrepancy.Explanation));

        return new ShsComparisonReport(
            legacyIds.Count,
            normalizedIds.Count,
            tolerance,
            discrepancies,
            System.Math.Max(0, unexplained - tolerance));
    }

    public static void Write(string path, ShsComparisonReport report)
    {
        WriteJson(path, report);
    }

    private string? GetExclusionReason(LexiconEntry entry)
    {
        if (!filters.EntryKinds.Contains(entry.EntryKind))
        {
            return "entry_kind";
        }

        if (entry.Phones.Count < filters.MinimumPhonemeLength || entry.Phones.Count > filters.MaximumPhonemeLength)
        {
            return "phoneme_length";
        }

        if (filters.ExcludeMalformedMarkup
            && (entry.Lemma.Contains('<', StringComparison.Ordinal)
                || entry.Lemma.Contains('>', StringComparison.Ordinal)
                || entry.Form.Contains('<', StringComparison.Ordinal)
                || entry.Form.Contains('>', StringComparison.Ordinal)
                || entry.Definition.Contains('<', StringComparison.Ordinal)
                || entry.Definition.Contains('>', StringComparison.Ordinal)))
        {
            return "malformed_markup";
        }

        return string.IsNullOrWhiteSpace(entry.Ipa) ? "missing_phonology" : null;
    }

    private static Dictionary<string, IReadOnlyDictionary<string, int>> BuildOverlapMatrix(
        IReadOnlyList<string> sourceIds,
        IEnumerable<SanskritUnionEntry> unionEntries)
    {
        var sourceSets = unionEntries.Select(entry => entry.Memberships
            .Select(membership => membership.SourceId)
            .ToHashSet(StringComparer.Ordinal))
            .ToArray();

        return sourceIds.ToDictionary(
            left => left,
            left => (IReadOnlyDictionary<string, int>)sourceIds.ToDictionary(
                right => right,
                right => sourceSets.Count(sources => sources.Contains(left) && sources.Contains(right)),
                StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    private static void WriteJson<T>(string path, T value)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(value, SerializerOptions).ReplaceLineEndings("\n") + "\n";
        File.WriteAllText(path, json, new UTF8Encoding(false));
    }

    private sealed class SanskritCorpusKeyComparer : IEqualityComparer<SanskritCorpusKey>
    {
        public static SanskritCorpusKeyComparer Instance { get; } = new();

        public bool Equals(SanskritCorpusKey? left, SanskritCorpusKey? right)
        {
            return left != null
                && right != null
                && string.Equals(left.Ipa, right.Ipa, StringComparison.Ordinal)
                && string.Equals(left.Lemma, right.Lemma, StringComparison.Ordinal);
        }

        public int GetHashCode(SanskritCorpusKey value)
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.Ipa),
                StringComparer.Ordinal.GetHashCode(value.Lemma));
        }
    }
}

public sealed record SanskritCorpusFilters(
    IReadOnlySet<LexiconEntryKind> EntryKinds,
    int MinimumPhonemeLength,
    int MaximumPhonemeLength,
    bool ExcludeMalformedMarkup)
{
    public static SanskritCorpusFilters Primary { get; } = new(
        new HashSet<LexiconEntryKind> { LexiconEntryKind.Lemma },
        2,
        24,
        true);
}

public sealed record SanskritCorpusResult(
    IReadOnlyList<SanskritUnionEntry> Entries,
    SanskritCorpusReport Report);

public sealed record SanskritUnionEntry(
    string Ipa,
    string Lemma,
    IReadOnlyList<SanskritSourceMembership> Memberships);

public sealed record SanskritSourceMembership(
    string SourceId,
    string SourceRecordId,
    string EntryId);

public sealed record SanskritCorpusReport(
    IReadOnlyDictionary<string, int> PerSourceCounts,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, int>> OverlapMatrix,
    int InputCount,
    int IncludedCount,
    int UnionCount,
    IReadOnlyDictionary<string, int> ExclusionReasons);

public sealed record ShsComparisonReport(
    int LegacyCount,
    int NormalizedCount,
    int Tolerance,
    IReadOnlyList<ShsDiscrepancy> Discrepancies,
    int UnexplainedAboveTolerance);

public sealed record ShsDiscrepancy(
    string SourceRecordId,
    string Side,
    string? Explanation);

internal sealed record SanskritCorpusKey(string Ipa, string Lemma);
