using Enochian.Lexicons;
using System.Text.Json;

namespace Enochian.UnitTests;

[TestClass]
public sealed class SanskritCorpusBuilderTests
{
    [TestMethod]
    public void DeduplicatesOverlapsAndRetainsSourceMembershipsDeterministically()
    {
        var entries = new[]
        {
            CreateEntry("cdsl-mw", "1", "agni", "aɡni", 4),
            CreateEntry("cdsl-ap", "7", "agni", "aɡni", 4),
            CreateEntry("cdsl-pw", "3", "agni", "aɡni", 4),
            CreateEntry("cdsl-pwg", "4", "agni", "aɡni", 4),
            CreateEntry("cdsl-shs", "9", "rAjan", "raːdʒan", 6),
        };
        var builder = new SanskritCorpusBuilder(SanskritCorpusFilters.Primary);

        var forward = builder.Build(entries);
        var reverse = builder.Build(entries.Reverse());

        Assert.AreEqual(JsonSerializer.Serialize(forward), JsonSerializer.Serialize(reverse));
        Assert.AreEqual(2, forward.Entries.Count);
        Assert.AreEqual(4, forward.Entries.Single(entry => entry.Lemma == "agni").Memberships.Count);
        Assert.AreEqual(1, forward.Report.OverlapMatrix["cdsl-ap"]["cdsl-mw"]);
        Assert.AreEqual(5, forward.Report.IncludedCount);
        Assert.AreEqual(2, forward.Report.UnionCount);
    }

    [TestMethod]
    public void AppliesPrimaryEntryKindLengthAndMarkupFilters()
    {
        var entries = new[]
        {
            CreateEntry("cdsl-mw", "1", "agni", "aɡni", 4),
            CreateEntry("cdsl-mw", "2", "short", "a", 1),
            CreateEntry("cdsl-mw", "3", "name", "naːma", 4, LexiconEntryKind.ProperName),
            CreateEntry("cdsl-mw", "4", "markup", "maːrk", 4, definition: "unclosed <i"),
        };

        var result = new SanskritCorpusBuilder(SanskritCorpusFilters.Primary).Build(entries);

        Assert.AreEqual(1, result.Entries.Count);
        Assert.AreEqual(1, result.Report.ExclusionReasons["entry_kind"]);
        Assert.AreEqual(1, result.Report.ExclusionReasons["phoneme_length"]);
        Assert.AreEqual(1, result.Report.ExclusionReasons["malformed_markup"]);
    }

    [TestMethod]
    public void ComparesLegacyAndNormalizedShsWithDeclaredToleranceAndExplanations()
    {
        var legacy = new[]
        {
            CreateEntry("cdsl-shs", "40", "rAjan", "raːdʒan", 6),
            CreateEntry("cdsl-shs", "41", "agni", "aɡni", 4),
        };
        var normalized = new[]
        {
            CreateEntry("cdsl-shs", "40", "rAjan", "raːdʒan", 6),
            CreateEntry("cdsl-shs", "42", "guru", "ɡuru", 4),
        };
        var explanations = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["41"] = "Legacy fixture record omitted by the normalized malformed-record filter.",
        };

        var report = SanskritCorpusBuilder.CompareShs(legacy, normalized, 1, explanations);

        Assert.AreEqual(2, report.Discrepancies.Count);
        Assert.AreEqual("Legacy fixture record omitted by the normalized malformed-record filter.", report.Discrepancies[0].Explanation);
        Assert.AreEqual(0, report.UnexplainedAboveTolerance);
    }

    private static LexiconEntry CreateEntry(
        string sourceId,
        string recordId,
        string lemma,
        string ipa,
        int phonemeLength,
        LexiconEntryKind entryKind = LexiconEntryKind.Lemma,
        string definition = "definition")
    {
        return new LexiconEntry
        {
            EntryId = $"{sourceId}:san:{recordId}",
            SourceId = sourceId,
            SourceRecordId = recordId,
            Lemma = lemma,
            Form = lemma,
            Ipa = ipa,
            EntryKind = entryKind,
            Definition = definition,
            Phones = [.. Enumerable.Range(0, phonemeLength).Select(_ => Array.Empty<double>())],
        };
    }
}
