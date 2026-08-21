using Enochian.Benchmark;

namespace Enochian.UnitTests;

[TestClass]
public sealed class BalancedSamplingTests
{
    [TestMethod]
    public void DeduplicatesAndSamplesExactCommonStrataDeterministically()
    {
        var inputs = CreateInputs();
        var includedKinds = new HashSet<string>(["lemma"], StringComparer.Ordinal);

        var candidates = CandidateSetBuilder.Build(inputs, includedKinds);
        var reversed = CandidateSetBuilder.Build(inputs.Reverse(), includedKinds);

        AssertUtils.SequenceEquals(
            candidates.Select(candidate => candidate.CandidateId),
            reversed.Select(candidate => candidate.CandidateId));
        Assert.IsFalse(candidates.Any(candidate => candidate.EntryKind == "proper_name"));
        Assert.AreEqual(1, candidates.Count(candidate => candidate.Language == "eng" && candidate.Phonology == "p0"));
        var overlap = candidates.Single(candidate => candidate.Language == "eng" && candidate.Phonology == "p1");
        Assert.HasCount(2, overlap.SourceMemberships);

        var first = BalancedSampler.Sample("primary", "primary", candidates, [2], 2, 17, "balanced-nulls-v1");
        var repeated = BalancedSampler.Sample("primary", "primary", reversed, [2], 2, 17, "balanced-nulls-v1");
        var changed = BalancedSampler.Sample("primary", "primary", candidates, [2], 2, 23, "balanced-nulls-v1");

        Assert.AreEqual(4, first.LargestCommonSize);
        AssertUtils.SequenceEquals([2, 4], first.SampleSizes);
        AssertUtils.SequenceEquals(
            first.Memberships.Select(MembershipIdentity),
            repeated.Memberships.Select(MembershipIdentity));
        Assert.IsFalse(first.Memberships.Select(MembershipIdentity).SequenceEqual(
            changed.Memberships.Select(MembershipIdentity), StringComparer.Ordinal));
        Assert.IsTrue(first.Memberships
            .GroupBy(membership => (membership.SampleId, membership.Language))
            .All(group => group.Count() == group.First().RequestedSize));
        Assert.IsTrue(first.Memberships
            .GroupBy(membership => (membership.SampleId, membership.Language, membership.LengthBand))
            .All(group => group.Count() == group.First().RequestedSize / 2));
        Assert.IsTrue(first.Memberships
            .GroupBy(membership => (membership.SampleId, membership.Language))
            .All(group => group.Select(membership => membership.CandidateId).Distinct(StringComparer.Ordinal).Count() == group.Count()));
        Assert.IsTrue(first.Shortages.Any(shortage => shortage.ExcludedByBalance > 0));
    }

    [TestMethod]
    public void RejectsSizesAboveCommonCapacity()
    {
        var candidates = CandidateSetBuilder.Build(
            CreateInputs(),
            new HashSet<string>(["lemma"], StringComparer.Ordinal));

        _ = Assert.ThrowsExactly<InvalidDataException>(() =>
            BalancedSampler.Sample("primary", "primary", candidates, [5], 1, 17, "balanced-nulls-v1"));
    }

    [TestMethod]
    public void BalancesComparableFrequencyBandsAndReportsMissingFrequency()
    {
        var candidates = new[]
        {
            Candidate("eng-low", "eng", 1),
            Candidate("eng-high", "eng", 100),
            Candidate("eng-missing", "eng", null),
            Candidate("tur-low", "tur", 2),
            Candidate("tur-high", "tur", 200),
            Candidate("tur-missing", "tur", null),
        };
        var bands = new[]
        {
            new SamplingFrequencyBand("low", 0, 10),
            new SamplingFrequencyBand("high", 10, null),
        };

        var result = BalancedSampler.Sample("primary", "primary", candidates, [], 1, 7, "balanced-nulls-v1", bands);

        Assert.AreEqual(3, result.LargestCommonSize);
        Assert.IsTrue(result.Memberships
            .GroupBy(membership => membership.Language)
            .All(group => group.Select(membership => membership.FrequencyBand)
                .Order(StringComparer.Ordinal)
                .SequenceEqual(["high", "low", "missing"], StringComparer.Ordinal)));
    }

    private static string MembershipIdentity(SamplingMembership membership) =>
        $"{membership.SampleId}:{membership.Language}:{membership.LengthBand}:{membership.CandidateId}";

    private static SamplingCandidateInput[] CreateInputs()
    {
        var result = new List<SamplingCandidateInput>();
        AddLanguage(result, "eng", shortCount: 3, longCount: 2);
        AddLanguage(result, "tur", shortCount: 2, longCount: 4);
        result.Add(Input("eng-overlap", "eng", "lemma-1", "p1", 3, "lemma", "second-source"));
        result.Add(Input("eng-homophone", "eng", "other-lemma", "p0", 3, "lemma", "first-source"));
        result.Add(Input("excluded", "eng", "name", "name", 3, "proper_name", "first-source"));
        return [.. result];
    }

    private static void AddLanguage(
        List<SamplingCandidateInput> result,
        string language,
        int shortCount,
        int longCount)
    {
        for (var index = 0; index < shortCount; index++)
        {
            result.Add(Input($"{language}-short-{index}", language, $"lemma-{index}", $"p{index}", 3, "lemma", "first-source"));
        }

        for (var index = 0; index < longCount; index++)
        {
            result.Add(Input($"{language}-long-{index}", language, $"long-{index}", $"long-p{index}", 6, "lemma", "first-source"));
        }
    }

    private static SamplingCandidateInput Input(
        string id,
        string language,
        string lemma,
        string phonology,
        int length,
        string entryKind,
        string source) =>
        new(
            id,
            language,
            lemma,
            phonology,
            [.. Enumerable.Range(0, length).Select(index => new[] { (double)index })],
            length,
            entryKind,
            source,
            id);

    private static SamplingCandidate Candidate(string id, string language, double? frequency) =>
        new(
            id,
            language,
            id,
            id,
            [[0], [1], [2]],
            frequency,
            "lemma",
            [new SamplingSourceMembership(id, "source", id)]);
}
