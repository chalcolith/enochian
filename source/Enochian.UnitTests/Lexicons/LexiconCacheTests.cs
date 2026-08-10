using Enochian.Flow;
using Enochian.Lexicons;
using Enochian.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;
using TextEncoding = Enochian.Text.Encoding;

namespace Enochian.UnitTests.Lexicons;

[TestClass]
[DoNotParallelize]
public sealed class LexiconCacheTests
{
    [TestMethod]
    public void CacheRoundTripPreservesMetadataAndDuplicateLemmas()
    {
        using var environment = new CacheTestEnvironment();
        var entries = new[]
        {
            CreateEntry(string.Empty, "record", "shared", "second", 2.5),
            CreateEntry(string.Empty, "record", "shared", "first", null),
        };

        var initial = environment.CreateLexicon(environment.CreateSource("dictionary.txt"), entries);
        Assert.AreEqual(2, initial.Entries.Count);
        Assert.AreEqual(1, initial.SourceLoadCount);

        var cached = environment.CreateLexicon(initial.SourcePath!, null);
        var matches = cached.EntriesByLemma["shared"];

        Assert.AreEqual(0, cached.SourceLoadCount);
        Assert.AreEqual(2, matches.Count);
        AssertUtils.SequenceEquals(["source:record", "source:record:2"], matches.Select(entry => entry.EntryId));
        AssertEntry(entries[1], matches[0]);
        AssertEntry(entries[0], matches[1]);
        Assert.AreSame(matches[0], cached.GetEntryByLemma("shared"));
        AssertUtils.NoErrors(cached);
    }

    [TestMethod]
    public void CacheIdentitySeparatesEqualFilenamesInDifferentDirectories()
    {
        using var environment = new CacheTestEnvironment();
        var firstPath = environment.CreateSource(Path.Combine("first", "dictionary.txt"));
        var secondPath = environment.CreateSource(Path.Combine("second", "dictionary.txt"));

        _ = environment.CreateLexicon(firstPath, [CreateEntry("first", "1", "alpha", "alpha", null)]).Entries;
        _ = environment.CreateLexicon(secondPath, [CreateEntry("second", "2", "beta", "beta", null)]).Entries;

        var firstCached = environment.CreateLexicon(firstPath, null);
        var secondCached = environment.CreateLexicon(secondPath, null);

        Assert.AreEqual("alpha", firstCached.Entries.Single().Lemma);
        Assert.AreEqual("beta", secondCached.Entries.Single().Lemma);
        Assert.AreEqual(0, firstCached.SourceLoadCount);
        Assert.AreEqual(0, secondCached.SourceLoadCount);
        Assert.AreEqual(2, environment.CachePaths.Count);
    }

    [TestMethod]
    public void InvalidCacheIsRebuiltAndFailedReplacementPreservesValidFile()
    {
        using var environment = new CacheTestEnvironment();
        var sourcePath = environment.CreateSource("dictionary.txt");
        var expected = CreateEntry("source:eng:record", "record", "word", "word", null);
        _ = environment.CreateLexicon(sourcePath, [expected]).Entries;
        var cachePath = environment.CachePaths.Single();

        File.WriteAllBytes(cachePath, Guid.Empty.ToByteArray());
        File.SetLastWriteTimeUtc(cachePath, DateTime.UtcNow.AddMinutes(1));

        var rebuilt = environment.CreateLexicon(sourcePath, [expected]);
        Assert.AreEqual("word", rebuilt.Entries.Single().Lemma);
        Assert.AreEqual(1, rebuilt.SourceLoadCount);
        AssertUtils.NoErrors(rebuilt);
        var validCache = File.ReadAllBytes(cachePath);

        File.SetLastWriteTimeUtc(sourcePath, DateTime.UtcNow.AddMinutes(1));
        var invalidEntry = CreateEntry("source:eng:invalid", "invalid", "invalid", "invalid", null);
        invalidEntry.Phones = [null!];
        var failedReplacement = environment.CreateLexicon(sourcePath, [invalidEntry]);
        _ = failedReplacement.Entries;

        CollectionAssert.AreEqual(validCache, File.ReadAllBytes(cachePath));
        Assert.IsTrue(failedReplacement.Errors.Any());
        Assert.IsFalse(Directory.EnumerateFiles(CacheTestEnvironment.CacheDirectory, "*.tmp").Any());
    }

    private static LexiconEntry CreateEntry(string entryId, string sourceRecordId, string lemma, string text, double? frequency)
    {
        return new LexiconEntry
        {
            EntryId = entryId,
            Language = "eng",
            Family = "Indo-European",
            SourceId = "source",
            SourceVersion = "1.0.0",
            SourceRecordId = sourceRecordId,
            Text = text,
            Lemma = lemma,
            Form = text + "-form",
            EntryKind = LexiconEntryKind.Lemma,
            Dialect = "test-dialect",
            PartOfSpeech = "noun",
            Frequency = frequency,
            SourceEncoding = "test-encoding",
            Ipa = "test-ipa",
            License = "CC-BY-4.0",
            Encoded = "encoded-" + text,
            Definition = "definition-" + text,
            Phones = [[1.0, -1.0], [0.5]],
        };
    }

    private static void AssertEntry(LexiconEntry expected, LexiconEntry actual)
    {
        Assert.AreEqual(expected.EntryId, actual.EntryId);
        Assert.AreEqual(expected.Language, actual.Language);
        Assert.AreEqual(expected.Family, actual.Family);
        Assert.AreEqual(expected.SourceId, actual.SourceId);
        Assert.AreEqual(expected.SourceVersion, actual.SourceVersion);
        Assert.AreEqual(expected.SourceRecordId, actual.SourceRecordId);
        Assert.AreEqual(expected.Text, actual.Text);
        Assert.AreEqual(expected.Lemma, actual.Lemma);
        Assert.AreEqual(expected.Form, actual.Form);
        Assert.AreEqual(expected.EntryKind, actual.EntryKind);
        Assert.AreEqual(expected.Dialect, actual.Dialect);
        Assert.AreEqual(expected.PartOfSpeech, actual.PartOfSpeech);
        Assert.AreEqual(expected.Frequency, actual.Frequency);
        Assert.AreEqual(expected.SourceEncoding, actual.SourceEncoding);
        Assert.AreEqual(expected.Ipa, actual.Ipa);
        Assert.AreEqual(expected.License, actual.License);
        Assert.AreEqual(expected.Encoded, actual.Encoded);
        Assert.AreEqual(expected.Definition, actual.Definition);
        Assert.AreEqual(expected.Phones.Count, actual.Phones.Count);
        for (var index = 0; index < expected.Phones.Count; index++)
        {
            CollectionAssert.AreEqual(expected.Phones[index], actual.Phones[index]);
        }
    }

    private sealed class CacheTestEnvironment : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "enochian-tests", Guid.NewGuid().ToString("N"));
        private readonly TestResources resources = new();
        private readonly string cachePrefix = "m101-" + Guid.NewGuid().ToString("N");

        public static string CacheDirectory => Path.GetFullPath(Path.Combine(".", Configurable.CacheDir));

        public IReadOnlyList<string> CachePaths => Directory.Exists(CacheDirectory)
            ? [.. Directory.EnumerateFiles(CacheDirectory, cachePrefix + "*.bin")]
            : [];

        public string CreateSource(string relativePath)
        {
            var directory = Path.GetDirectoryName(relativePath);
            var fileName = cachePrefix + "-" + Path.GetFileName(relativePath);
            var path = Path.Combine(root, directory ?? string.Empty, fileName);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "fixture");
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-1));
            return path;
        }

        public TestLexicon CreateLexicon(string sourcePath, IEnumerable<LexiconEntry>? sourceEntries)
        {
            var lexicon = new TestLexicon(new FeatureSet(null), resources, sourceEntries)
            {
                AbsoluteFilePath = Path.Combine(root, "config.json"),
            };
            _ = lexicon.Configure(new JsonObject
            {
                ["id"] = "test-lexicon",
                ["features"] = "test-features",
                ["encoding"] = "test-encoding",
                ["path"] = sourcePath,
            });
            return lexicon;
        }

        public void Dispose()
        {
            foreach (var path in CachePaths)
            {
                File.Delete(path);
            }

            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }

        private sealed class TestResources : IFlowResources
        {
            public IList<FeatureSet> FeatureSets { get; } = [new FeatureSet(null) { Id = "test-features" }];
            public IList<TextEncoding> Encodings { get; } = [new TextEncoding(null) { Id = "test-encoding" }];
            public IList<Lexicon> Lexicons { get; } = [];
        }
    }

    private sealed class TestLexicon(
        IConfigurable parent,
        IFlowResources resources,
        IEnumerable<LexiconEntry>? sourceEntries)
        : Lexicon(parent, resources)
    {
        public override ILogger Log => NullLogger.Instance;

        public int SourceLoadCount { get; private set; }

        protected override void LoadLexicon(string path)
        {
            SourceLoadCount++;
            if (sourceEntries == null)
            {
                throw new InvalidOperationException("The source loader should not run when a valid cache exists.");
            }

            SetEntries(sourceEntries);
        }
    }
}
