using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Enochian.Benchmark;

public static class BenchmarkSampling
{
    public static string GetLengthBand(int phonemeLength) => phonemeLength switch
    {
        >= 3 and <= 5 => "03-05",
        >= 6 and <= 9 => "06-09",
        >= 10 and <= 20 => "10-20",
        _ => "out-of-range",
    };

    public static string GetUnusualCategory(string ipa)
    {
        if (ipa.EnumerateRunes().Any(rune => Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark))
        {
            return "combining-mark";
        }

        if (ipa.Contains('͡', StringComparison.Ordinal))
        {
            return "tie-bar";
        }

        if (ipa.EnumerateRunes().Any(rune => rune.Value is 0x02B0 or 0x02B1 or 0x02B2 or 0x02B7 or 0x02E0 or 0x02E4))
        {
            return "modifier-letter";
        }

        return "ordinary";
    }

    public static IReadOnlyList<BenchmarkEntry> Sample(
        IEnumerable<BenchmarkEntry> entries,
        int perStratum,
        int seed)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(perStratum);
        return
        [
            .. entries
                .GroupBy(entry => (entry.Language, entry.LengthBand, entry.UnusualCategory))
                .OrderBy(group => group.Key.Language, StringComparer.Ordinal)
                .ThenBy(group => group.Key.LengthBand, StringComparer.Ordinal)
                .ThenBy(group => group.Key.UnusualCategory, StringComparer.Ordinal)
                .SelectMany(group => group
                    .OrderBy(entry => StableKey(seed, entry.EntryId), StringComparer.Ordinal)
                    .ThenBy(entry => entry.EntryId, StringComparer.Ordinal)
                    .Take(perStratum)),
        ];
    }

    private static string StableKey(int seed, string identity)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{seed}\u001f{identity}");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
}
