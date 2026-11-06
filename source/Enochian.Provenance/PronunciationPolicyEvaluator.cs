using System.Security.Cryptography;
using System.Text;
using IpaEncoder = Enochian.Text.Encoder;

namespace Enochian.Provenance;

public static class PronunciationPolicyEvaluator
{
    public static string NormalizeIpa(string ipa)
    {
        var value = ipa.Trim();
        if (value.Length >= 2 &&
            ((value[0] == '/' && value[^1] == '/') || (value[0] == '[' && value[^1] == ']')))
        {
            value = value[1..^1];
        }

        return value.Replace("ˈ", string.Empty, StringComparison.Ordinal)
            .Replace("ˌ", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormC);
    }

    public static string LengthBand(int length) => length switch
    {
        >= 3 and <= 5 => "03-05",
        >= 6 and <= 9 => "06-09",
        >= 10 and <= 20 => "10-20",
        _ => "outside",
    };

    public static string StableId(string value) =>
        Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    public static PronunciationAgreement EvaluateReferences(
        IEnumerable<PronunciationReference> references,
        PronunciationPolicy policy,
        IpaEncoder encoder)
        => EvaluateReferencesCore(references.Select(row => new AlternativeRow(
            row.LemmaId, [row.GeneratedIpa], [row.ReferenceIpa], row.ReferenceOrigin,
            row.GeneratorDependencies, row.ReferenceDependencies, row.HeldOut, false,
            row.IndependenceEstablished)), policy, encoder, null);

    public static PronunciationAgreement EvaluateReferences(
        IEnumerable<PronunciationAlternativeReference> references,
        PronunciationPolicy policy,
        IpaEncoder encoder)
        => EvaluateReferencesCore(references.Select(row => new AlternativeRow(
            row.LemmaId, row.GeneratedIpa, row.ReferenceIpa, row.ReferenceOrigin,
            row.GeneratorDependencies, row.ReferenceDependencies, row.HeldOut, row.TuningExample,
            row.IndependenceEstablished)), policy, encoder, 0);

    public static PronunciationAgreement EvaluateReferences(
        IEnumerable<PronunciationAlternativeReference> references,
        PronunciationPolicy policy,
        IpaEncoder encoder,
        int selectionSeed)
        => EvaluateReferencesCore(references.Select(row => new AlternativeRow(
            row.LemmaId, row.GeneratedIpa, row.ReferenceIpa, row.ReferenceOrigin,
            row.GeneratorDependencies, row.ReferenceDependencies, row.HeldOut, row.TuningExample,
            row.IndependenceEstablished)), policy, encoder, selectionSeed);

    private static PronunciationAgreement EvaluateReferencesCore(
        IEnumerable<AlternativeRow> references,
        PronunciationPolicy policy,
        IpaEncoder encoder,
        int? selectionSeed)
    {
        var rows = references.ToArray();
        var diagnostics = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var valid = new List<(AlternativeRow Row, string[] Generated, string[] Reference, int Length)>();
        var duplicateIds = rows.GroupBy(row => row.LemmaId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            var generated = row.GeneratedIpa.Select(NormalizeIpa).Order(StringComparer.Ordinal).ToArray();
            var reference = row.ReferenceIpa.Select(NormalizeIpa).Order(StringComparer.Ordinal).ToArray();
            var generatedPhones = Encode(generated, encoder);
            var referencePhones = Encode(reference, encoder);
            var alternativeBands = referencePhones.Select(value => LengthBand(value.Count))
                .Distinct(StringComparer.Ordinal).ToArray();
            var reason = duplicateIds.Contains(row.LemmaId) ? "duplicate_reference_lemma"
                : generated.Length == 0 || reference.Length == 0 ? "missing_alternative_distribution"
                : !row.HeldOut ? "not_held_out"
                : row.TuningExample ? "tuning_leakage"
                : row.GeneratorDependencies.Length == 0 || row.ReferenceDependencies.Length == 0 ||
                    !row.IndependenceEstablished ? "unknown_independence"
                : row.GeneratorDependencies.Intersect(row.ReferenceDependencies, StringComparer.Ordinal).Any()
                    ? "shared_upstream"
                : row.ReferenceOrigin != "dictionary-supplied" ? "unvalidated_reference_origin"
                : generatedPhones.Any(value => value.Unknown || value.Count == 0) ||
                    referencePhones.Any(value => value.Unknown || value.Count == 0) ? "unsupported_ipa"
                : alternativeBands.Length != 1 ? "inconsistent_alternative_length_band"
                : referencePhones.Any(value => value.Count < policy.MinimumPhonemes ||
                    value.Count > policy.MaximumPhonemes) ? "outside_length"
                : null;
            if (reason != null)
            {
                Increment(diagnostics, reason);
            }
            else
            {
                valid.Add((row, generated, reference, referencePhones[0].Count));
            }
        }

        var perBand = (int)System.Math.Ceiling((double)policy.MinimumReferences / policy.RequiredBands.Length);
        var sample = valid.GroupBy(item => LengthBand(item.Length), StringComparer.Ordinal)
            .Where(group => policy.RequiredBands.Contains(group.Key, StringComparer.Ordinal))
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .SelectMany(group => group
                .OrderByDescending(item => item.Reference.Sum(value => value.Count(character => character > 127)))
                .ThenBy(item => StableId(selectionSeed == null ? item.Row.LemmaId :
                    selectionSeed.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                    "\u001f" + item.Row.LemmaId), StringComparer.Ordinal).Take(perBand))
            .ToArray();
        var bands = Counts(sample.Select(item => LengthBand(item.Length)));
        var agreement = sample.Length == 0 ? (double?)null
            : (double)sample.Count(item => item.Generated.SequenceEqual(item.Reference, StringComparer.Ordinal)) / sample.Length;
        var disagreements = sample.Count(item => !item.Generated.SequenceEqual(item.Reference, StringComparer.Ordinal));
        if (disagreements != 0)
        {
            diagnostics["pronunciation_disagreement"] = disagreements;
        }

        var editDistance = sample.Length == 0 ? (double?)null : sample.Average(item =>
            AlternativeEditDistance(item.Generated, item.Reference, encoder));
        var enough = sample.Length >= policy.MinimumReferences &&
            policy.RequiredBands.All(band => bands.GetValueOrDefault(band) >= policy.MinimumPerBand);
        if (sample.Length < policy.MinimumReferences)
        {
            diagnostics["insufficient_reference_sample"] = policy.MinimumReferences - sample.Length;
        }

        foreach (var band in policy.RequiredBands.Where(band => bands.GetValueOrDefault(band) < policy.MinimumPerBand))
        {
            diagnostics["insufficient_band_" + band] = policy.MinimumPerBand - bands.GetValueOrDefault(band);
        }

        if (agreement < policy.MinimumAgreement)
        {
            diagnostics["agreement_below_threshold"] = 1;
        }

        return new PronunciationAgreement(
            enough && agreement >= policy.MinimumAgreement ? "pass" : "exploratory",
            rows.Length, valid.Count, sample.Length, agreement, editDistance, bands, diagnostics);
    }

    private static (int Count, bool Unknown)[] Encode(IEnumerable<string> alternatives, IpaEncoder encoder) =>
        [.. alternatives.Select(ipa =>
        {
            var (_, _, phones) = encoder.GetTextAndPhones(
                ipa.Normalize(NormalizationForm.FormD), out var unknown);
            return (phones.Count, unknown.Count != 0);
        })];

    private static double AlternativeEditDistance(string[] generated, string[] reference, IpaEncoder encoder)
    {
        var common = System.Math.Min(generated.Length, reference.Length);
        var total = generated.Length == reference.Length ? 0.0 :
            System.Math.Abs(generated.Length - reference.Length);
        for (var index = 0; index < common; index++)
        {
            total += PhoneEditDistance(generated[index], reference[index], encoder);
        }

        return total / System.Math.Max(generated.Length, reference.Length);
    }

    public static SortedDictionary<string, int> Counts(IEnumerable<string> values)
    {
        var result = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            Increment(result, value);
        }

        return result;
    }

    public static void Increment(IDictionary<string, int> counts, string key) =>
        counts[key] = counts.TryGetValue(key, out var value) ? value + 1 : 1;

    private static double PhoneEditDistance(string generated, string reference, IpaEncoder encoder)
    {
        var (_, _, left) = encoder.GetTextAndPhones(generated.Normalize(NormalizationForm.FormD));
        var (_, _, right) = encoder.GetTextAndPhones(reference.Normalize(NormalizationForm.FormD));
        var previous = Enumerable.Range(0, right.Count + 1).ToArray();
        for (var i = 1; i <= left.Count; i++)
        {
            var current = new int[right.Count + 1];
            current[0] = i;
            for (var j = 1; j <= right.Count; j++)
            {
                current[j] = System.Math.Min(
                    previous[j] + 1,
                    System.Math.Min(current[j - 1] + 1,
                        previous[j - 1] + (left[i - 1].SequenceEqual(right[j - 1]) ? 0 : 1)));
            }

            previous = current;
        }

        return previous[right.Count];
    }

    private sealed record AlternativeRow(
        string LemmaId, string[] GeneratedIpa, string[] ReferenceIpa, string ReferenceOrigin,
        string[] GeneratorDependencies, string[] ReferenceDependencies, bool HeldOut,
        bool TuningExample, bool IndependenceEstablished);
}
