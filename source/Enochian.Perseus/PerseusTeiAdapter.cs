using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Enochian.Perseus;

public sealed class PerseusTeiAdapter
{
    public static IReadOnlyList<PerseusLemma> Parse(string path)
    {
        var lemmas = new List<PerseusLemma>();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Parse,
            XmlResolver = null,
        };
        using var reader = XmlReader.Create(path, settings);
        while (reader.Read())
        {
            if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "entryFree")
            {
                continue;
            }

            using var subtree = reader.ReadSubtree();
            var entry = XElement.Load(subtree, LoadOptions.PreserveWhitespace);
            var recordId = GetAttribute(entry, "id");
            var orth = entry.DescendantsAndSelf()
                .FirstOrDefault(element => element.Name.LocalName == "orth" &&
                    string.Equals(GetAttribute(element, "lang"), "la", StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(recordId) || orth == null)
            {
                continue;
            }

            var originalForm = CollapseWhitespace(orth.Value);
            var normalizedForm = NormalizeForm(originalForm);
            var partOfSpeech = entry.Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "pos") is { } pos
                    ? CollapseWhitespace(pos.Value)
                    : null;
            var definition = string.Join(" ", entry.Descendants()
                .Where(element => element.Name.LocalName is "sense" or "def")
                .Where(element => !element.Ancestors().Any(ancestor =>
                    ancestor.Name.LocalName is "sense" or "def"))
                .Select(element => CollapseWhitespace(string.Join(" ",
                    element.DescendantNodesAndSelf().OfType<XText>().Select(text => text.Value)))))
                .Normalize(NormalizationForm.FormC);
            lemmas.Add(new PerseusLemma(
                recordId,
                originalForm,
                normalizedForm,
                partOfSpeech,
                string.IsNullOrWhiteSpace(definition) ? null : definition));
        }

        return [.. lemmas
            .GroupBy(lemma => (lemma.NormalizedForm, lemma.PartOfSpeech), LemmaKeyComparer.Instance)
            .Select(group => group.OrderBy(lemma => lemma.RecordId, StringComparer.Ordinal).First())
            .OrderBy(lemma => lemma.RecordId, StringComparer.Ordinal)];
    }

    private static string NormalizeForm(string value)
    {
        var result = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormC).ToLowerInvariant())
        {
            _ = character switch
            {
                '-' or '·' or '^' or '_' or '†' or '\'' or '!' or '?' or '\u0361' => result,
                'ў' => result.Append('ŭ'),
                'æ' => result.Append("ae"),
                'œ' => result.Append("oe"),
                'á' or 'à' or 'ä' => result.Append('a'),
                'é' or 'è' or 'ë' => result.Append('e'),
                'í' or 'ì' or 'ï' => result.Append('i'),
                'ó' or 'ò' or 'ö' => result.Append('o'),
                'ú' or 'ù' or 'ü' => result.Append('u'),
                'ÿ' => result.Append('y'),
                _ when char.IsWhiteSpace(character) => result,
                _ => result.Append(character),
            };
        }

        return result.ToString();
    }

    private static string CollapseWhitespace(string value)
    {
        return string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Normalize(NormalizationForm.FormC);
    }

    private static string? GetAttribute(XElement element, string localName)
    {
        return element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == localName)?.Value;
    }

    private sealed class LemmaKeyComparer : IEqualityComparer<(string NormalizedForm, string? PartOfSpeech)>
    {
        public static LemmaKeyComparer Instance { get; } = new();

        public bool Equals(
            (string NormalizedForm, string? PartOfSpeech) left,
            (string NormalizedForm, string? PartOfSpeech) right)
        {
            return string.Equals(left.NormalizedForm, right.NormalizedForm, StringComparison.Ordinal)
                && string.Equals(left.PartOfSpeech, right.PartOfSpeech, StringComparison.Ordinal);
        }

        public int GetHashCode((string NormalizedForm, string? PartOfSpeech) value)
        {
            return HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(value.NormalizedForm),
                value.PartOfSpeech == null ? 0 : StringComparer.Ordinal.GetHashCode(value.PartOfSpeech));
        }
    }
}

public sealed record PerseusLemma(
    string RecordId,
    string OriginalForm,
    string NormalizedForm,
    string? PartOfSpeech,
    string? Definition);

public sealed class ClassicalLatinConverter
{
    private static readonly Dictionary<Rune, string> Vowels = new()
    {
        [new Rune('a')] = "a",
        [new Rune('e')] = "e",
        [new Rune('i')] = "i",
        [new Rune('o')] = "o",
        [new Rune('u')] = "u",
        [new Rune('y')] = "y",
        [new Rune('ā')] = "aː",
        [new Rune('ē')] = "eː",
        [new Rune('ī')] = "iː",
        [new Rune('ō')] = "oː",
        [new Rune('ū')] = "uː",
        [new Rune('ȳ')] = "yː",
        [new Rune('ă')] = "a",
        [new Rune('ĕ')] = "e",
        [new Rune('ĭ')] = "i",
        [new Rune('ŏ')] = "o",
        [new Rune('ŭ')] = "u",
    };
    private static readonly HashSet<Rune> ExplicitLength =
    [
        new Rune('ā'), new Rune('ē'), new Rune('ī'), new Rune('ō'), new Rune('ū'), new Rune('ȳ'),
        new Rune('ă'), new Rune('ĕ'), new Rune('ĭ'), new Rune('ŏ'), new Rune('ŭ'),
    ];

    public static LatinConversion Convert(string source)
    {
        var form = source.Normalize(NormalizationForm.FormC).ToLowerInvariant();
        var runes = form.EnumerateRunes().ToArray();
        var ipa = new StringBuilder();
        var assumedShortVowels = 0;
        var unknown = new SortedSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < runes.Length; index++)
        {
            var current = runes[index];
            var next = index + 1 < runes.Length ? runes[index + 1] : default;
            if (TryDigraph(current, next, out var digraph))
            {
                _ = ipa.Append(digraph);
                index++;
                continue;
            }

            if (Vowels.TryGetValue(current, out var vowel))
            {
                if (current.Value == 'i' && IsConsonantal(index, runes))
                {
                    _ = ipa.Append('j');
                }
                else if (current.Value == 'u' && IsConsonantal(index, runes))
                {
                    _ = ipa.Append('w');
                }
                else
                {
                    if (!ExplicitLength.Contains(current) && current.Value is not 'y')
                    {
                        assumedShortVowels++;
                    }

                    _ = ipa.Append(vowel);
                }

                continue;
            }

            var consonant = current.Value switch
            {
                'b' => "b",
                'c' => "k",
                'd' => "d",
                'f' => "f",
                'g' => "g",
                'h' => "h",
                'j' => "j",
                'k' => "k",
                'l' => "l",
                'm' => "m",
                'n' => "n",
                'p' => "p",
                'r' => "r",
                's' => "s",
                't' => "t",
                'v' => "w",
                'x' => "ks",
                'z' => "z",
                _ => null,
            };
            if (consonant == null)
            {
                _ = unknown.Add(current.ToString());
            }
            else
            {
                _ = ipa.Append(consonant);
            }
        }

        return new LatinConversion(
            source,
            ipa.ToString(),
            assumedShortVowels,
            [.. unknown]);
    }

    private static bool TryDigraph(Rune current, Rune next, out string ipa)
    {
        ipa = (current.Value, next.Value) switch
        {
            ('a', 'e') => "ae",
            ('a', 'u') => "au",
            ('c', 'h') => "kʰ",
            ('o', 'e') => "oe",
            ('p', 'h') => "pʰ",
            ('q', 'u') => "kw",
            ('t', 'h') => "tʰ",
            _ => string.Empty,
        };
        return ipa.Length != 0;
    }

    private static bool IsConsonantal(int index, Rune[] runes)
    {
        if (index + 1 >= runes.Length || !Vowels.ContainsKey(runes[index + 1]))
        {
            return false;
        }

        return index == 0 || Vowels.ContainsKey(runes[index - 1]);
    }
}

public sealed record LatinConversion(
    string SourceForm,
    string Ipa,
    int AssumedShortVowels,
    IReadOnlyList<string> UnknownGraphemes)
{
    public bool IsComplete => UnknownGraphemes.Count == 0 && Ipa.Length != 0;
}
