using Json.Schema;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using IpaEncoder = Enochian.Text.Encoder;

namespace Enochian.Provenance;

public sealed class PronunciationAudit(string repositoryRoot)
{
    public static JsonSerializerOptions JsonOptions { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonSerializerOptions ReportOptions = new(JsonOptions) { WriteIndented = true };

    public async Task RunAsync(string planPath, bool acquire, CancellationToken cancellationToken = default)
    {
        var plan = LoadPlan(planPath);
        var sources = LoadSources(plan);
        if (acquire)
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromHours(2) };
            foreach (var source in sources)
            {
                await AcquireAsync(client, source, cancellationToken);
            }
        }

        foreach (var source in sources)
        {
            Verify(source);
        }

        var output = Resolve(plan.OutputDirectory);
        _ = Directory.CreateDirectory(output);
        var work = Path.Combine(output, "work-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        _ = Directory.CreateDirectory(work);
        try
        {
            SpoolSources(plan, sources, work, cancellationToken);
            WriteOutputs(planPath, plan, sources, work, output);
        }
        finally
        {
            Directory.Delete(work, true);
        }
    }

    private static void SpoolSources(
        PronunciationPlan plan,
        IEnumerable<PronunciationSnapshot> sources,
        string work,
        CancellationToken cancellationToken)
    {
        var writers = new Dictionary<string, StreamWriter>(StringComparer.Ordinal);
        try
        {
            foreach (var language in plan.Languages)
            {
                writers.Add(language.Language, new StreamWriter(
                    Path.Combine(work, language.Language + ".jsonl"), false, new UTF8Encoding(false)));
            }

            foreach (var source in sources)
            {
                Console.WriteLine($"Auditing pinned source {source.Id} ({source.Hash}).");
                using var input = File.OpenRead(source.Path);
                using var gzip = source.Path.EndsWith(".gz", StringComparison.Ordinal)
                    ? new GZipStream(input, CompressionMode.Decompress) : null;
                using var reader = new StreamReader(gzip ?? (Stream)input, new UTF8Encoding(false, true));
                if (source.Parser == "wiktextract-raw")
                {
                    ReadWiktextract(reader, source, plan, writers, cancellationToken);
                }
                else if (source.Parser == "ipa-dict-tsv")
                {
                    var language = plan.Languages.Single(candidate => candidate.Language == source.Language);
                    var convention = source.Language switch
                    {
                        "ara" => "Modern-Standard-Arabic",
                        "fas" => "modern-persian",
                        _ => throw new InvalidDataException(
                            $"{source.Id}: no pinned IPA Dictionary convention for {source.Language}."),
                    };
                    foreach (var reading in PronunciationDictionaryAdapter.ReadIpaDictionary(
                        reader, source.Id, source.Revision, source.Language, convention, source.License))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        writers[language.Language].WriteLine(JsonSerializer.Serialize(
                            new PronunciationLexeme(reading.Source, reading.LexicalIdentity,
                                "unverified_entry_kind", [reading]), JsonOptions));
                    }
                }
                else
                {
                    throw new InvalidDataException($"Unsupported pronunciation parser: {source.Parser}.");
                }
            }
        }
        finally
        {
            foreach (var writer in writers.Values)
            {
                writer.Dispose();
            }
        }
    }

    private void WriteOutputs(
        string planPath,
        PronunciationPlan plan,
        PronunciationSnapshot[] sources,
        string work,
        string output)
    {
        var encoder = CreateEncoder();
        var coverage = new List<PronunciationCoverage>();
        var artifacts = new List<string>();
        foreach (var language in plan.Languages.OrderBy(language => language.Language, StringComparer.Ordinal))
        {
            var builder = new PronunciationCoverageBuilder(language, plan.Policy, encoder);
            using (var reader = new StreamReader(
                Path.Combine(work, language.Language + ".jsonl"), new UTF8Encoding(false, true)))
            {
                while (reader.ReadLine() is { } line)
                {
                    builder.AddLexeme(JsonSerializer.Deserialize<PronunciationLexeme>(line, JsonOptions)
                        ?? throw new InvalidDataException("Empty pronunciation spool row."));
                }
            }

            var lexicon = language.LexiconPath == null ? null : Resolve(language.LexiconPath);
            var references = language.ReferencePath == null ? []
                : ReadReferences(Resolve(language.ReferencePath));
            var (report, evidence, entries, joins) = builder.Build(lexicon, references);
            var prefix = Path.Combine(output, language.Language);
            WriteLines(prefix + ".evidence.jsonl", evidence);
            WriteLines(prefix + ".jsonl", entries);
            WriteLines(prefix + ".joins.jsonl", joins);
            artifacts.AddRange([prefix + ".evidence.jsonl", prefix + ".jsonl", prefix + ".joins.jsonl"]);
            coverage.Add(report);
            Console.WriteLine(FormattableString.Invariant(
                $"{language.Language}: {report.UsableLemmas}/{report.EligibleLexicalUnits} usable source lexical units; {report.SupportedEvidenceLemmas} supported; exploratory."));
            if (report.LexiconStatus == "missing")
            {
                Console.Error.WriteLine($"{language.Language}: configured target lexicon is missing; joins were not evaluated.");
            }
        }

        var reports = Resolve(plan.ReportDirectory);
        _ = Directory.CreateDirectory(reports);
        WriteJson(Path.Combine(reports, "coverage.json"), new
        {
            SchemaVersion = "1.0.0",
            plan.PolicyId,
            PolicySha256 = Hash(planPath),
            plan.Frozen,
            plan.DumpDate,
            plan.ExtractionDate,
            plan.ExtractorRevision,
            plan.ProcessorRevision,
            SoftwareSha256 = Hash(typeof(PronunciationAudit).Assembly.Location),
            FeatureSha256 = Hash(Resolve("resources/encodings/features.json")),
            EncodingSha256 = Hash(Resolve("resources/encodings/ipa.json")),
            plan.Policy,
            Inputs = plan.Languages.OrderBy(language => language.Language, StringComparer.Ordinal).Select(language => new
            {
                language.Language,
                language.LexiconPath,
                LexiconSha256 = language.LexiconPath != null && File.Exists(Resolve(language.LexiconPath))
                    ? Hash(Resolve(language.LexiconPath)) : null,
                language.ReferencePath,
                ReferenceSha256 = language.ReferencePath != null ? Hash(Resolve(language.ReferencePath)) : null,
            }),
            Artifacts = artifacts.Order(StringComparer.Ordinal).Select(path => new
            {
                Path = Path.GetRelativePath(repositoryRoot, path).Replace('\\', '/'),
                Sha256 = Hash(path),
                Bytes = new FileInfo(path).Length,
            }),
            Sources = sources.Select(source => new
            {
                SourceId = source.Id,
                source.Url,
                source.Hash,
                source.Revision,
                source.License,
                source.Citation,
            }),
            Languages = coverage,
        });
        WriteText(Path.Combine(reports, "coverage.md"), RenderMatrix(plan, sources, coverage));
    }

    public static async Task AcquireAsync(
        HttpClient client,
        PronunciationSnapshot source,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(source.Path))
        {
            Verify(source);
            return;
        }

        _ = Directory.CreateDirectory(Path.GetDirectoryName(source.Path)!);
        var temporary = source.Path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            using var response = await client.GetAsync(new Uri(source.Url),
                HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            _ = response.EnsureSuccessStatusCode();
            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = File.Create(temporary))
            {
                await input.CopyToAsync(output, cancellationToken);
            }

            Verify(source with { Path = temporary });
            File.Move(temporary, source.Path);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static void Verify(PronunciationSnapshot source)
    {
        if (!File.Exists(source.Path))
        {
            throw new FileNotFoundException($"{source.Id}: snapshot missing; use pronunciation-acquire explicitly.", source.Path);
        }

        var hash = Hash(source.Path);
        if (hash != source.Hash)
        {
            throw new InvalidDataException(
                $"{source.Id}: SHA-256 {hash} does not match pinned {source.Hash}; do not replace the pin silently.");
        }
    }

    public static void WriteLines<T>(string path, IEnumerable<T> rows)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)) { NewLine = "\n" })
            {
                foreach (var row in rows)
                {
                    writer.WriteLine(JsonSerializer.Serialize(row, JsonOptions));
                }
            }

            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private PronunciationPlan LoadPlan(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
        var schema = JsonSchema.FromText(File.ReadAllText(
            Resolve("resources/lexicons/schemas/pronunciation-audit.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        if (!schema.Evaluate(document.RootElement).IsValid)
        {
            throw new InvalidDataException($"{path}: invalid pronunciation audit protocol.");
        }

        var plan = JsonSerializer.Deserialize<PronunciationPlan>(document.RootElement, JsonOptions)
            ?? throw new InvalidDataException($"{path}: empty pronunciation audit protocol.");
        if (plan.Languages.Select(language => language.Language).Distinct(StringComparer.Ordinal).Count() != plan.Languages.Length ||
            plan.Languages.Select(language => language.DictionaryCode).Distinct(StringComparer.Ordinal).Count() != plan.Languages.Length)
        {
            throw new InvalidDataException("Pronunciation audit languages and dictionary codes must be unique.");
        }

        return plan;
    }

    private PronunciationSnapshot[] LoadSources(PronunciationPlan plan)
    {
        var paths = plan.SourceManifests.Select(Resolve).ToArray();
        var validator = new ManifestValidator(repositoryRoot, Resolve("resources/lexicons/schemas/source-manifest.schema.json"));
        var validation = validator.Validate(paths);
        if (!validation.IsValid)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, validation.Issues));
        }

        return [.. paths.Select(path =>
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            if (root.GetProperty("status").GetString() != "acquired" ||
                root.GetProperty("license_status").GetString() != "verified")
            {
                throw new InvalidDataException($"{path}: pronunciation acquisition requires pinned, licensed acquired metadata.");
            }

            return new PronunciationSnapshot(
                root.GetProperty("source_id").GetString()!,
                Resolve(root.GetProperty("raw_path").GetString()!),
                root.GetProperty("url").GetString()!,
                root.GetProperty("sha256").GetString()!,
                root.GetProperty("revision").GetProperty("value").GetString()!,
                root.GetProperty("language").GetString()!,
                root.GetProperty("parser").GetProperty("id").GetString()!,
                root.GetProperty("license").GetString()!,
                root.GetProperty("citation").GetString()!);
        }).OrderBy(source => source.Id, StringComparer.Ordinal)];
    }

    private static void ReadWiktextract(
        TextReader reader,
        PronunciationSnapshot source,
        PronunciationPlan plan,
        Dictionary<string, StreamWriter> writers,
        CancellationToken cancellationToken)
    {
        var codes = plan.Languages.ToDictionary(language => language.DictionaryCode, StringComparer.Ordinal);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;
            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var code = PronunciationDictionaryAdapter.GetString(root, "lang_code");
                if (codes.TryGetValue(code, out var language))
                {
                    var lexeme = PronunciationDictionaryAdapter.ReadLexeme(
                        root, source.Id, source.Revision, language.Language, source.License);
                    writers[language.Language].WriteLine(JsonSerializer.Serialize(lexeme, JsonOptions));
                }
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                throw new InvalidDataException($"{source.Id}:{lineNumber}: {exception.Message}", exception);
            }
        }
    }

    private PronunciationReference[] ReadReferences(string path)
    {
        var schema = JsonSchema.FromText(File.ReadAllText(Resolve(
            "resources/lexicons/schemas/pronunciation-reference.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        using var reader = new StreamReader(path, new UTF8Encoding(false, true));
        var rows = new List<PronunciationReference>();
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            using var document = JsonDocument.Parse(line);
            if (!schema.Evaluate(document.RootElement).IsValid)
            {
                throw new InvalidDataException($"{path}:{lineNumber}: invalid pronunciation reference.");
            }

            rows.Add(JsonSerializer.Deserialize<PronunciationReference>(document.RootElement, JsonOptions)
                ?? throw new InvalidDataException($"{path}: empty pronunciation reference."));
        }

        return [.. rows];
    }

    private IpaEncoder CreateEncoder()
    {
        var flow = new Enochian.Flow.Flow(Resolve("samples/ipatransducer.json"));
        if (flow.Errors.Any())
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, flow.Errors.Select(error => error.Message)));
        }

        return new IpaEncoder(flow.FeatureSets.Single(features => features.Id == "Default"),
            flow.Encodings.Single(encoding => string.Equals(encoding.Id, "IPA", StringComparison.OrdinalIgnoreCase)));
    }

    private static string RenderMatrix(
        PronunciationPlan plan,
        IEnumerable<PronunciationSnapshot> sources,
        IEnumerable<PronunciationCoverage> coverage)
    {
        var lines = new List<string>
        {
            "# Pronunciation dictionary coverage",
            string.Empty,
            $"Policy: `{plan.PolicyId}`; Wiktionary dump: {plan.DumpDate}; extraction: {plan.ExtractionDate}.",
            string.Empty,
            "Counts are source-specific lexical units, not independent language evidence. IPA coverage is not accuracy.",
            "All profiles remain exploratory pending M2-08/M2-09. No human annotation or Voynich scoring was performed.",
            string.Empty,
            "| Language | Convention | Eligible units | With IPA | Convention matched | Usable | Supported evidence |",
            "| --- | --- | ---: | ---: | ---: | ---: | ---: |",
        };
        lines.AddRange(coverage.Select(row => FormattableString.Invariant(
            $"| {row.Language} | {row.Convention} | {row.EligibleLexicalUnits} | {row.LemmasWithIpa} | {row.ConventionMatchedLemmas} | {row.UsableLemmas} | {row.SupportedEvidenceLemmas} |")));
        lines.AddRange(
        [
            string.Empty,
            "Usable means convention/length/symbol compatible, not historically verified. Multiple readings count once per lexical unit.",
            "Unknown or template-generated provenance is not independently supplied pronunciation evidence.",
            "See coverage.json and per-language evidence/joins for exclusions, length bands, labels, overlaps, and agreement diagnostics.",
            string.Empty,
            "## Pinned sources",
            string.Empty,
        ]);
        foreach (var source in sources)
        {
            lines.Add($"- {source.Id}: revision `{source.Revision}`, SHA-256 `{source.Hash}`, license {source.License}.");
            lines.Add($"  {source.Citation}");
        }

        return string.Join("\n", lines) + "\n";
    }

    private string Resolve(string path) =>
        Path.GetFullPath(Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)));

    private static void WriteJson<T>(string path, T value) =>
        WriteText(path, JsonSerializer.Serialize(value, ReportOptions) + "\n");

    private static void WriteText(string path, string content)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}

public sealed record PronunciationSnapshot(
    string Id,
    string Path,
    string Url,
    string Hash,
    string Revision,
    string Language,
    string Parser,
    string License,
    string Citation);
