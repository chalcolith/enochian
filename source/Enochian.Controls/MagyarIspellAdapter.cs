using System.Text;

namespace Enochian.Controls;

public static class MagyarIspellAdapter
{
    private static readonly HashSet<string> IncludedModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "alap", "autizmus", "biologia", "fizika", "foldrajz", "godo", "godo2", "huhyph",
        "idegen", "ifjusagi", "informatika", "kemia", "matematika", "sport", "szoszablya",
        "tamogatok", "zene",
    };
    private static readonly HashSet<string> ExcludedModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "javito",
        "magyar_helysegnevek",
        "magyar_helysegnevek_2007",
        "magyar_szemelynevek",
        "regies",
    };

    public static ControlSourceResult Parse(string rootPath)
    {
        var lemmas = new List<ControlSourceLemma>();
        var rejections = new List<ControlSourceRejection>();
        foreach (var path in Directory.EnumerateFiles(rootPath, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal))
        {
            var relativePath = Path.GetRelativePath(rootPath, path).Replace('\\', '/');
            var components = relativePath.Split('/');
            var module = components[0];
            var fileName = Path.GetFileName(path);
            if (!IncludedModules.Contains(module) && !ExcludedModules.Contains(module))
            {
                continue;
            }

            if (!fileName.EndsWith(".1", StringComparison.Ordinal) &&
                !fileName.EndsWith(".2", StringComparison.Ordinal) &&
                !fileName.EndsWith(".7", StringComparison.Ordinal))
            {
                continue;
            }

            var category = GetFileExclusion(module, fileName);
            var lineNumber = 0;
            foreach (var sourceLine in File.ReadLines(path, new UTF8Encoding(false, true)))
            {
                lineNumber++;
                var line = sourceLine.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                var recordId = $"{relativePath}:{lineNumber}";
                if (category != null)
                {
                    rejections.Add(new(recordId, category, $"Excluded Magyar Ispell source category {relativePath}."));
                    continue;
                }

                var fieldIndex = line.IndexOfAny([' ', '\t', '[']);
                var original = (fieldIndex < 0 ? line : line[..fieldIndex]).Normalize(NormalizationForm.FormC);
                var metadata = fieldIndex < 0
                    ? []
                    : line[fieldIndex..].Trim().Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (original.Length == 0 || !original.Any(char.IsLetter) || original.Contains('/'))
                {
                    rejections.Add(new(recordId, "malformed", "Line does not contain one lexical stem."));
                    continue;
                }

                if (original.EndsWith('.') || fileName.Contains("rovid", StringComparison.OrdinalIgnoreCase))
                {
                    rejections.Add(new(recordId, "abbreviation", "Entry is marked as an abbreviation."));
                    continue;
                }

                var partOfSpeech = GetPartOfSpeech(fileName);
                lemmas.Add(new(recordId, original, original.Replace("-", string.Empty, StringComparison.Ordinal).ToLowerInvariant(), partOfSpeech, metadata));
            }
        }

        return new(
            [.. lemmas.GroupBy(lemma => (lemma.NormalizedForm, lemma.PartOfSpeech))
                .Select(group => group.First())],
            rejections);
    }

    private static string? GetFileExclusion(string module, string fileName)
    {
        if (module.Equals("javito", StringComparison.OrdinalIgnoreCase))
        {
            return "correction";
        }

        if (module.Equals("regies", StringComparison.OrdinalIgnoreCase))
        {
            return "obsolete";
        }

        if (ExcludedModules.Contains(module) || fileName.StartsWith("tulajdonnev", StringComparison.OrdinalIgnoreCase))
        {
            return "proper_name";
        }

        return null;
    }

    private static string? GetPartOfSpeech(string fileName)
    {
        return fileName switch
        {
            _ when fileName.StartsWith("fonev", StringComparison.OrdinalIgnoreCase) => "noun",
            _ when fileName.StartsWith("ige", StringComparison.OrdinalIgnoreCase) => "verb",
            _ when fileName.StartsWith("melleknev", StringComparison.OrdinalIgnoreCase) => "adjective",
            _ => null,
        };
    }
}
