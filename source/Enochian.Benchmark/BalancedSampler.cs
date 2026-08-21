using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Enochian.Benchmark;

public static class BalancedSampler
{
    public static BalancedSamplingResult Sample(
        string analysisId,
        string analysisSet,
        IEnumerable<SamplingCandidate> candidates,
        IEnumerable<int> smallerSampleSizes,
        int repetitions,
        int seed,
        string generatorVersion,
        IReadOnlyList<SamplingFrequencyBand>? frequencyBands = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(analysisId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(repetitions);
        ArgumentOutOfRangeException.ThrowIfNegative(seed);
        var source = candidates.OrderBy(candidate => candidate.CandidateId, StringComparer.Ordinal).ToArray();
        var languages = source.Select(candidate => candidate.Language).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (languages.Length == 0)
        {
            throw new InvalidDataException("Balanced sampling requires at least one candidate language.");
        }

        var grouped = source.ToLookup(candidate => GetStratum(candidate, frequencyBands));
        var strata = grouped.Select(group => group.Key).Distinct().OrderBy(key => key.LengthBand, StringComparer.Ordinal)
            .ThenBy(key => key.FrequencyBand, StringComparer.Ordinal).ToArray();
        var capacities = strata.ToDictionary(
            stratum => stratum,
            stratum => languages.Min(language => grouped[stratum].Count(candidate => candidate.Language == language)));
        var largestCommonSize = capacities.Values.Sum();
        if (largestCommonSize == 0)
        {
            throw new InvalidDataException("Candidate languages have no common non-empty sampling strata.");
        }

        var sizes = smallerSampleSizes
            .Append(largestCommonSize)
            .Distinct()
            .Order()
            .ToArray();
        if (sizes.Any(size => size <= 0 || size > largestCommonSize))
        {
            throw new InvalidDataException($"Sample sizes must be between 1 and the largest common size {largestCommonSize}.");
        }

        var memberships = new List<SamplingMembership>();
        for (var repetition = 1; repetition <= repetitions; repetition++)
        {
            foreach (var size in sizes)
            {
                var quotas = AllocateQuotas(size, capacities);
                var sampleId = string.Create(
                    CultureInfo.InvariantCulture,
                    $"{analysisId}.size-{size:D8}.rep-{repetition:D4}");
                foreach (var language in languages)
                {
                    foreach (var stratum in strata)
                    {
                        var selected = grouped[stratum]
                            .Where(candidate => candidate.Language == language)
                            .OrderBy(candidate => StableKey(seed, sampleId, candidate.CandidateId), StringComparer.Ordinal)
                            .ThenBy(candidate => candidate.CandidateId, StringComparer.Ordinal)
                            .Take(quotas[stratum]);
                        memberships.AddRange(selected.Select(candidate => new SamplingMembership(
                            "1.0.0",
                            analysisId,
                            analysisSet,
                            sampleId,
                            repetition,
                            size,
                            seed,
                            generatorVersion,
                            language,
                            stratum.LengthBand,
                            stratum.FrequencyBand,
                            candidate.CandidateId,
                            candidate.Lemma,
                            candidate.Phonology,
                            candidate.EntryKind,
                            candidate.SourceMemberships)));
                    }
                }
            }
        }

        var shortages = languages.SelectMany(language => strata.Select(stratum =>
        {
            var available = grouped[stratum].Count(candidate => candidate.Language == language);
            return new SamplingShortage(
                language,
                stratum.LengthBand,
                stratum.FrequencyBand,
                available,
                capacities[stratum],
                available - capacities[stratum]);
        })).OrderBy(shortage => shortage.Language, StringComparer.Ordinal)
            .ThenBy(shortage => shortage.LengthBand, StringComparer.Ordinal)
            .ThenBy(shortage => shortage.FrequencyBand, StringComparer.Ordinal)
            .ToArray();
        return new(largestCommonSize, sizes, memberships, shortages);
    }

    private static Dictionary<SamplingStratum, int> AllocateQuotas(
        int size,
        IReadOnlyDictionary<SamplingStratum, int> capacities)
    {
        var total = capacities.Values.Sum();
        var quotas = capacities.ToDictionary(pair => pair.Key, pair => size * pair.Value / total);
        var remaining = size - quotas.Values.Sum();
        foreach (var stratum in capacities
            .OrderByDescending(pair => size * pair.Value % total)
            .ThenBy(pair => pair.Key.LengthBand, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.FrequencyBand, StringComparer.Ordinal)
            .Select(pair => pair.Key)
            .Take(remaining))
        {
            quotas[stratum]++;
        }

        return quotas;
    }

    private static SamplingStratum GetStratum(
        SamplingCandidate candidate,
        IReadOnlyList<SamplingFrequencyBand>? frequencyBands)
    {
        var lengthBand = BenchmarkSampling.GetLengthBand(candidate.Phones.Count);
        if (frequencyBands == null || frequencyBands.Count == 0)
        {
            return new(lengthBand, "all");
        }

        var frequencyBand = candidate.Frequency == null
            ? "missing"
            : frequencyBands.FirstOrDefault(band =>
                (band.Minimum == null || candidate.Frequency >= band.Minimum) &&
                (band.Maximum == null || candidate.Frequency < band.Maximum))?.Id ?? "out-of-range";
        return new(lengthBand, frequencyBand);
    }

    internal static string StableKey(int seed, params string[] identities)
    {
        var text = string.Join('\u001f', new[] { seed.ToString(CultureInfo.InvariantCulture) }.Concat(identities));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private sealed record SamplingStratum(string LengthBand, string FrequencyBand);
}
