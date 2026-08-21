using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Enochian.Benchmark;

public static class CandidateSetBuilder
{
    public static IReadOnlyList<SamplingCandidate> Build(
        IEnumerable<SamplingCandidateInput> inputs,
        IReadOnlySet<string> includedEntryKinds)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(includedEntryKinds);
        var eligible = inputs
            .Where(input => includedEntryKinds.Contains(input.EntryKind))
            .OrderBy(input => input.Language, StringComparer.Ordinal)
            .ThenBy(input => Normalize(input.Lemma), StringComparer.Ordinal)
            .ThenBy(input => input.EntryKind, StringComparer.Ordinal)
            .ThenBy(input => input.Phonology, StringComparer.Ordinal)
            .ThenBy(input => input.EntryId, StringComparer.Ordinal)
            .ToArray();

        var uniqueLemmas = eligible
            .GroupBy(input => (input.Language, input.EntryKind, Lemma: Normalize(input.Lemma)))
            .Select(group => group.First())
            .ToArray();

        return
        [
            .. uniqueLemmas
                .GroupBy(input => (input.Language, input.EntryKind, input.Phonology))
                .OrderBy(group => group.Key.Language, StringComparer.Ordinal)
                .ThenBy(group => group.Key.EntryKind, StringComparer.Ordinal)
                .ThenBy(group => group.Key.Phonology, StringComparer.Ordinal)
                .Select(group => CreateCandidate(group, eligible)),
        ];
    }

    private static SamplingCandidate CreateCandidate(
        IGrouping<(string Language, string EntryKind, string Phonology), SamplingCandidateInput> group,
        IReadOnlyList<SamplingCandidateInput> eligible)
    {
        var representative = group
            .OrderBy(input => Normalize(input.Lemma), StringComparer.Ordinal)
            .ThenBy(input => input.EntryId, StringComparer.Ordinal)
            .First();
        var representedLemmas = group.Select(input => Normalize(input.Lemma)).ToHashSet(StringComparer.Ordinal);
        var memberships = eligible
            .Where(input => input.Language == group.Key.Language
                && input.EntryKind == group.Key.EntryKind
                && input.Phonology == group.Key.Phonology
                && representedLemmas.Contains(Normalize(input.Lemma)))
            .Select(input => new SamplingSourceMembership(input.EntryId, input.SourceId, input.SourceRecordId))
            .Distinct()
            .OrderBy(membership => membership.SourceId, StringComparer.Ordinal)
            .ThenBy(membership => membership.SourceRecordId, StringComparer.Ordinal)
            .ThenBy(membership => membership.EntryId, StringComparer.Ordinal)
            .ToArray();
        return new(
            CreateCandidateId(group.Key.Language, group.Key.EntryKind, group.Key.Phonology, representedLemmas),
            group.Key.Language,
            Normalize(representative.Lemma),
            group.Key.Phonology,
            [.. representative.Phones.Select(phone => phone.ToArray())],
            representative.Frequency,
            group.Key.EntryKind,
            memberships);
    }

    private static string CreateCandidateId(
        string language,
        string entryKind,
        string phonology,
        IEnumerable<string> lemmas)
    {
        var identity = string.Join('\u001f', new[] { language, entryKind, phonology }.Concat(lemmas.Order(StringComparer.Ordinal)));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return string.Create(CultureInfo.InvariantCulture, $"{language}:{hash[..24]}");
    }

    private static string Normalize(string value) => value.Normalize(NormalizationForm.FormC).ToLowerInvariant();
}
