using Enochian.Benchmark;
using System.Globalization;

namespace Enochian.UnitTests;

[TestClass]
public sealed class SequenceNullGeneratorTests
{
    [TestMethod]
    public void GeneratesDeterministicLabelledNullsWithDeclaredInvariants()
    {
        var candidates = CreateCandidates();
        var query = new SamplingQuery("query-1", "abc", ["a", "b", "c"], 7);
        var mapping = new Dictionary<string, double[]>(StringComparer.Ordinal)
        {
            ["a"] = [9],
            ["b"] = [8],
            ["c"] = [7],
        };

        var first = SequenceNullGenerator.Generate("primary", candidates, [query], mapping, 2, 31, "balanced-nulls-v1");
        var repeated = SequenceNullGenerator.Generate("primary", candidates.Reverse(), [query], mapping, 2, 31, "balanced-nulls-v1");
        var changed = SequenceNullGenerator.Generate("primary", candidates, [query], mapping, 2, 37, "balanced-nulls-v1");

        Assert.HasCount(16, first);
        Assert.IsTrue(first.All(row => row.IsNull && row.NullId.StartsWith("null.", StringComparison.Ordinal)));
        Assert.IsTrue(first.All(row => row.QueryLength == 3 && row.Phones.Count == 3));
        Assert.IsTrue(first.All(row => row.AnalysisMode switch
        {
            "type-primary" => row.Weight == 1,
            "token-weighted" => row.Weight == 7,
            _ => false,
        }));
        AssertUtils.SequenceEquals(first.Select(Identity), repeated.Select(Identity));
        Assert.IsFalse(first.Select(Identity).SequenceEqual(changed.Select(Identity), StringComparer.Ordinal));

        var observedPhones = candidates.SelectMany(candidate => candidate.Phones).Select(Key).ToHashSet(StringComparer.Ordinal);
        Assert.IsTrue(first.Where(row => row.NullKind == "unigram-pseudoword")
            .SelectMany(row => row.Phones)
            .All(phone => observedPhones.Contains(Key(phone))));

        var observedBiphones = candidates.SelectMany(candidate => candidate.Phones.Zip(candidate.Phones.Skip(1), PairKey))
            .ToHashSet(StringComparer.Ordinal);
        Assert.IsTrue(first.Where(row => row.NullKind == "biphone-pseudoword")
            .All(row => row.Phones.Zip(row.Phones.Skip(1), PairKey).All(observedBiphones.Contains)));

        var mappingInventory = mapping.Values.Select(Key).Order(StringComparer.Ordinal).ToArray();
        Assert.IsTrue(first.Where(row => row.NullKind == "mapping-assignment-shuffle")
            .All(row => row.Phones.Select(Key).Order(StringComparer.Ordinal).SequenceEqual(mappingInventory, StringComparer.Ordinal)));
        Assert.IsTrue(first.Where(row => row.NullKind == "within-query-shuffle")
            .All(row => row.Phones.Select(Key).Order(StringComparer.Ordinal).SequenceEqual(mappingInventory, StringComparer.Ordinal)));
    }

    private static SamplingCandidate[] CreateCandidates() =>
    [
        Candidate("one", [[0], [1], [2], [0]]),
        Candidate("two", [[1], [2], [0], [1]]),
    ];

    private static SamplingCandidate Candidate(string id, IReadOnlyList<double[]> phones) =>
        new(
            id,
            "eng",
            id,
            id,
            phones,
            null,
            "lemma",
            [new SamplingSourceMembership(id, "source", id)]);

    private static string Identity(SequenceNullRecord row) =>
        $"{row.NullId}:{row.AnalysisMode}:{row.Seed}:{string.Join(';', row.Phones.Select(Key))}";

    private static string PairKey(double[] left, double[] right) => $"{Key(left)}>{Key(right)}";

    private static string Key(double[] phone) =>
        string.Join(',', phone.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
}
