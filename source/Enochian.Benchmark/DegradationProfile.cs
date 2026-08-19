using Enochian.Text;

namespace Enochian.Benchmark;

public sealed record DegradationProfile(
    string Id,
    string Version,
    bool LanguageNeutral,
    IReadOnlyList<DegradationOperation> Operations);

public sealed record DegradationOperation(
    string Kind,
    int? Every = null,
    int Offset = 0,
    IReadOnlyList<string>? Features = null);

public static class BenchmarkDegrader
{
    public static IReadOnlyList<double[]> Apply(
        IReadOnlyList<double[]> phones,
        DegradationProfile profile,
        FeatureSet featureSet)
    {
        if (!profile.LanguageNeutral)
        {
            throw new InvalidDataException("Primary benchmark degradation profiles must be language-neutral.");
        }

        IReadOnlyList<double[]> current = [.. phones.Select(phone => phone.ToArray())];
        foreach (var operation in profile.Operations)
        {
            current = operation.Kind switch
            {
                "deletion" => Delete(current, operation),
                "feature_merger" => Merge(current, operation, featureSet),
                "feature_masking" => Mask(current, operation, featureSet),
                _ => throw new InvalidDataException($"Unknown degradation operation '{operation.Kind}'."),
            };
        }

        return current;
    }

    private static IReadOnlyList<double[]> Delete(
        IReadOnlyList<double[]> phones,
        DegradationOperation operation)
    {
        var every = operation.Every ?? throw new InvalidDataException("Deletion requires 'every'.");
        if (every < 2 || operation.Offset < 0 || operation.Offset >= every)
        {
            throw new InvalidDataException("Deletion requires every >= 2 and 0 <= offset < every.");
        }

        return
        [
            .. phones.Where((_, index) => index % every != operation.Offset),
        ];
    }

    private static IReadOnlyList<double[]> Merge(
        IReadOnlyList<double[]> phones,
        DegradationOperation operation,
        FeatureSet featureSet)
    {
        var indices = GetFeatureIndices(operation, featureSet);
        if (indices.Length < 2)
        {
            throw new InvalidDataException("Feature merger requires at least two features.");
        }

        return
        [
            .. phones.Select(phone =>
            {
                var transformed = phone.ToArray();
                transformed[indices[0]] = indices.Average(index => phone[index]);
                foreach (var index in indices.Skip(1))
                {
                    transformed[index] = featureSet.UnsetValue;
                }

                return transformed;
            }),
        ];
    }

    private static IReadOnlyList<double[]> Mask(
        IReadOnlyList<double[]> phones,
        DegradationOperation operation,
        FeatureSet featureSet)
    {
        var indices = GetFeatureIndices(operation, featureSet);
        return
        [
            .. phones.Select(phone =>
            {
                var transformed = phone.ToArray();
                foreach (var index in indices)
                {
                    transformed[index] = featureSet.UnsetValue;
                }

                return transformed;
            }),
        ];
    }

    private static int[] GetFeatureIndices(DegradationOperation operation, FeatureSet featureSet)
    {
        var requested = operation.Features ?? throw new InvalidDataException($"{operation.Kind} requires features.");
        var names = featureSet.FeatureList
            .SelectMany((aliases, index) => aliases.Split(',').Select(alias => (Alias: alias.Trim(), Index: index)))
            .ToDictionary(pair => pair.Alias, pair => pair.Index, StringComparer.OrdinalIgnoreCase);
        return
        [
            .. requested.Select(feature => names.TryGetValue(feature, out var index)
                ? index
                : throw new InvalidDataException($"Unknown degradation feature '{feature}'.")),
        ];
    }
}
