using Enochian.Cdsl;
using Enochian.Provenance;
using Json.Schema;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Enochian.Benchmark;

public sealed record M4ContextConfigurationPin(string Path, string Sha256);

public sealed record M4ContextArtifactPin(string Path, string Sha256, long Bytes);

public sealed record M4ContextDictionarySource(
    string SourceId,
    M4ContextConfigurationPin Manifest,
    string Join,
    string? DictionaryLanguageCode);

public sealed record M4ContextAstronomy(
    M4ContextConfigurationPin Protocol,
    M4ContextConfigurationPin Registration,
    M4ContextArtifactPin Catalog,
    M4ContextConfigurationPin PronunciationPanel,
    int MinimumSharedCatalogIds);

public sealed record M4ContextConfiguration(
    [property: JsonPropertyName("$schema")] string Schema,
    string SchemaVersion,
    string AppendixId,
    bool Frozen,
    M4ContextConfigurationPin SourceRunManifest,
    M4ContextConfigurationPin QuantitativeReportManifest,
    M4ContextArtifactPin SamplingArtifact,
    M4ContextDictionarySource[] DictionarySources,
    M4ContextAstronomy Astronomy,
    string OutputDirectory,
    string[] HoldoutLoci,
    string[] Rules);

public sealed record M4ContextRequest(
    string CandidateId,
    string Language,
    string Lemma,
    string EntryId,
    string SourceId,
    string SourceRecordId);

public sealed record M4CandidateContext(
    string CandidateId,
    string Language,
    string Lemma,
    string EntryId,
    string SourceId,
    string SourceRecordId,
    string? PartOfSpeech,
    int ContextOrdinal,
    string ContextKind,
    string? Context,
    string[] Labels,
    string JoinStatus,
    string RunId,
    string SourceArtifact);

public sealed record M4AstronomyLanguage(
    string Language,
    string ProfileId,
    int ProfileRecords,
    int CatalogNames,
    int LinkedNameRecords,
    int LinkedCatalogIds,
    int AmbiguousMatchKeys,
    int MissingCatalogNames,
    string Status,
    string RunId,
    string SourceArtifact);

public sealed record M4AstronomyPair(
    string TargetLanguage,
    string ComparisonLanguage,
    int SharedCatalogIds,
    int BalancedObjects,
    int NullRepetitions,
    string Status,
    string Diagnostic,
    string RunId,
    string SourceArtifact);

public sealed record M4ContextManifest(
    string SchemaVersion,
    string AppendixId,
    string RunId,
    string Status,
    bool SecondaryEvidence,
    bool HumanAnnotation,
    bool AutomatedSemanticScoring,
    bool DefinitionsPresent,
    bool PrimaryArtifactsModified,
    bool HoldoutPresent,
    HashedArtifact[] SourceArtifacts,
    HashedArtifact[] AppendixArtifacts);

public sealed class M4ContextualAppendixGenerator(string repositoryRoot)
{
    private const string RunId = "73939edd1372a103413470388d725eef1764e82e09e732a40b9c4d424fa09fb1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly AutomatedFiles files = new(repositoryRoot);

    public int Run(string configurationPath, string? outputDirectory = null)
    {
        var configuration = LoadConfiguration(configurationPath);
        VerifyConfiguration(configuration);
        var runManifest = ReadJson<M4RunPublicationManifest>(
            files.Resolve(configuration.SourceRunManifest.Path));
        var reportManifest = ReadJson<M4ReportManifest>(
            files.Resolve(configuration.QuantitativeReportManifest.Path));
        if (runManifest.RunId != RunId || runManifest.Status != "completed" ||
            runManifest.DefinitionsPresent || runManifest.HoldoutPresent ||
            reportManifest.RunId != RunId || reportManifest.Status != "completed" ||
            reportManifest.DefinitionsPresent || reportManifest.HoldoutPresent)
        {
            throw new InvalidDataException("M4-03 requires the frozen, definition-free M4-01 and M4-02 publications.");
        }

        var protectedArtifacts = runManifest.PackagedArtifacts
            .Concat(reportManifest.ReportArtifacts)
            .ToDictionary(
                artifact => artifact.Path,
                artifact => PronunciationAudit.Hash(files.Resolve(artifact.Path)),
                StringComparer.Ordinal);
        var sourceSnapshots = new PronunciationAudit(repositoryRoot).LoadSnapshots(
            configuration.DictionarySources.Select(source => source.Manifest.Path));
        foreach (var snapshot in sourceSnapshots)
        {
            PronunciationAudit.Verify(snapshot);
        }

        var requests = LoadContextRequests(files.Resolve(configuration.SamplingArtifact.Path));
        var contexts = BuildContexts(configuration, sourceSnapshots, requests);
        var (astronomyLanguages, astronomyPairs, astronomyInputs) = BuildAstronomy(configuration);
        var outputPath = files.Resolve(outputDirectory ?? configuration.OutputDirectory);
        _ = Directory.CreateDirectory(outputPath);
        WriteContextCsv(Path.Combine(outputPath, "candidate-contexts.csv"), contexts);
        WriteAstronomyLanguageCsv(Path.Combine(outputPath, "astronomy-languages.csv"), astronomyLanguages);
        WriteAstronomyPairCsv(Path.Combine(outputPath, "astronomy-pairs.csv"), astronomyPairs);
        WriteReport(Path.Combine(outputPath, "report.md"), contexts, astronomyLanguages, astronomyPairs);
        foreach (var artifact in protectedArtifacts)
        {
            if (PronunciationAudit.Hash(files.Resolve(artifact.Key)) != artifact.Value)
            {
                throw new InvalidDataException("M4-03 modified a protected primary artifact: " + artifact.Key);
            }
        }

        var sourceArtifacts = new[]
        {
            ExperimentHashing.Artifact(configurationPath, repositoryRoot),
            ExperimentHashing.Artifact(files.Resolve(configuration.SourceRunManifest.Path), repositoryRoot),
            ExperimentHashing.Artifact(files.Resolve(configuration.QuantitativeReportManifest.Path), repositoryRoot),
            ExperimentHashing.Artifact(files.Resolve(configuration.SamplingArtifact.Path), repositoryRoot),
            ExperimentHashing.Artifact(files.Resolve(configuration.Astronomy.Protocol.Path), repositoryRoot),
            ExperimentHashing.Artifact(files.Resolve(configuration.Astronomy.Registration.Path), repositoryRoot),
            ExperimentHashing.Artifact(files.Resolve(configuration.Astronomy.Catalog.Path), repositoryRoot),
            ExperimentHashing.Artifact(files.Resolve(configuration.Astronomy.PronunciationPanel.Path), repositoryRoot),
            ExperimentHashing.Artifact(files.Resolve(
                "source/Enochian.Benchmark/M4ContextualAppendixGenerator.cs"), repositoryRoot),
            ExperimentHashing.Artifact(files.Resolve(
                "source/Enochian.Cdsl/CdslOrigAdapter.cs"), repositoryRoot),
            ExperimentHashing.Artifact(files.Resolve(
                "source/Enochian.Provenance/AstronomicalNameRegistry.cs"), repositoryRoot),
            ExperimentHashing.Artifact(files.Resolve(
                "source/Enochian.Provenance/PronunciationDictionaryAdapter.cs"), repositoryRoot),
        }
        .Concat(configuration.DictionarySources.Select(source =>
            ExperimentHashing.Artifact(files.Resolve(source.Manifest.Path), repositoryRoot)))
        .Concat(sourceSnapshots.Select(source =>
            ExperimentHashing.Artifact(source.Path, repositoryRoot)))
        .Concat(astronomyInputs)
        .DistinctBy(artifact => artifact.Path, StringComparer.Ordinal)
        .OrderBy(artifact => artifact.Path, StringComparer.Ordinal)
        .ToArray();
        var expectedOutputs = new[]
        {
            Path.Combine(outputPath, "astronomy-languages.csv"),
            Path.Combine(outputPath, "astronomy-pairs.csv"),
            Path.Combine(outputPath, "candidate-contexts.csv"),
            Path.Combine(outputPath, "report.md"),
        };
        var unexpectedOutputs = Directory.EnumerateFiles(outputPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetFileName(path) != "manifest.json" &&
                !expectedOutputs.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (unexpectedOutputs.Length != 0)
        {
            throw new InvalidDataException("Unexpected file in M4-03 output directory: " + unexpectedOutputs[0]);
        }

        var appendixArtifacts = expectedOutputs
            .Order(StringComparer.Ordinal)
            .Select(path => ExperimentHashing.Artifact(path, repositoryRoot))
            .ToArray();
        AutomatedFiles.Write(Path.Combine(outputPath, "manifest.json"), new M4ContextManifest(
            "1.0.0",
            configuration.AppendixId,
            RunId,
            "completed",
            true,
            false,
            false,
            true,
            false,
            false,
            sourceArtifacts,
            appendixArtifacts));
        Console.WriteLine($"Rendered M4-03 contextual appendix for run {RunId}.");
        return 0;
    }

    public static M4CandidateContext[] JoinCdsl(
        TextReader reader,
        IReadOnlyList<M4ContextRequest> requests,
        string sourceArtifact)
    {
        var requested = requests.ToLookup(RequestedCdslKey);
        var matches = new Dictionary<(string RecordId, string Lemma), List<(string? PartOfSpeech, string? Text)>>(
            EqualityComparer<(string RecordId, string Lemma)>.Default);
        var profile = CdslMarkupProfile.Create("shs");
        foreach (var result in CdslOrigAdapter.ParseRecords(reader, "cdsl-shs"))
        {
            if (result.Record is not { } record)
            {
                continue;
            }

            var key = (record.RecordId, record.Headword.Normalize(NormalizationForm.FormC));
            if (requested.Contains(key))
            {
                if (!matches.TryGetValue(key, out var values))
                {
                    values = [];
                    matches.Add(key, values);
                }

                values.Add((null, profile.CleanDefinition(record.Body)));
            }
        }

        return BuildContextRows(requests, matches, "definition", sourceArtifact, RequestedCdslKey);
    }

    public static M4CandidateContext[] JoinWiktextract(
        TextReader reader,
        IReadOnlyList<M4ContextRequest> requests,
        string dictionaryLanguageCode,
        string sourceArtifact)
    {
        var identities = requests.Select(request => request.SourceRecordId).ToHashSet(StringComparer.Ordinal);
        var matches = new Dictionary<string, ContextMatches>(
            StringComparer.Ordinal);
        var lineNumber = 0;
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (!line.Contains($"\"lang_code\": \"{dictionaryLanguageCode}\"", StringComparison.Ordinal) &&
                !line.Contains($"\"lang_code\":\"{dictionaryLanguageCode}\"", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (PronunciationDictionaryAdapter.GetString(root, "lang_code") != dictionaryLanguageCode)
                {
                    continue;
                }

                var identity = PronunciationDictionaryAdapter.LexicalIdentity(root);
                if (!identities.Contains(identity))
                {
                    continue;
                }

                if (!matches.TryGetValue(identity, out var match))
                {
                    match = new ContextMatches();
                    matches.Add(identity, match);
                }

                match.EntryCount++;
                match.Contexts.AddRange(ReadWiktextractContexts(root));
            }
            catch (Exception exception) when (exception is JsonException or InvalidDataException)
            {
                throw new InvalidDataException($"kaikki-raw:{lineNumber}: {exception.Message}", exception);
            }
        }

        var rows = new List<M4CandidateContext>();
        foreach (var request in requests.OrderBy(request => request.CandidateId, StringComparer.Ordinal)
                     .ThenBy(request => request.EntryId, StringComparer.Ordinal))
        {
            var found = matches.GetValueOrDefault(request.SourceRecordId);
            var status = found == null ? "missing_source_record"
                : found.EntryCount > 1 ? "ambiguous_source_record"
                : "matched";
            if (found == null)
            {
                rows.Add(ContextRow(request, null, 0, "none", null, [], status, sourceArtifact));
                continue;
            }

            var ordinal = 0;
            foreach (var value in found.Contexts)
            {
                ordinal++;
                rows.Add(ContextRow(
                    request,
                    value.PartOfSpeech,
                    ordinal,
                    value.Text == null ? "labels_only" : "gloss",
                    value.Text,
                    value.Labels,
                    status,
                    sourceArtifact));
            }
        }

        return [.. rows];
    }

    private M4ContextConfiguration LoadConfiguration(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, StrictUtf8));
        var schema = JsonSchema.FromText(File.ReadAllText(files.Resolve(
            "experiments/schemas/m4-contextual-appendix.schema.json"), StrictUtf8),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        if (!schema.Evaluate(document.RootElement).IsValid)
        {
            throw new InvalidDataException($"{path}: invalid M4-03 contextual appendix configuration.");
        }

        return JsonSerializer.Deserialize<M4ContextConfiguration>(
            document.RootElement,
            AutomatedProtocol.JsonOptions)
            ?? throw new InvalidDataException($"{path}: empty M4-03 contextual appendix configuration.");
    }

    private void VerifyConfiguration(M4ContextConfiguration configuration)
    {
        VerifyConfigurationPin(configuration.SourceRunManifest);
        VerifyConfigurationPin(configuration.QuantitativeReportManifest);
        VerifyArtifactPin(configuration.SamplingArtifact);
        VerifyConfigurationPin(configuration.Astronomy.Protocol);
        VerifyConfigurationPin(configuration.Astronomy.Registration);
        VerifyArtifactPin(configuration.Astronomy.Catalog);
        VerifyConfigurationPin(configuration.Astronomy.PronunciationPanel);
        foreach (var source in configuration.DictionarySources)
        {
            VerifyConfigurationPin(source.Manifest);
        }

        if (configuration.HoldoutLoci.Length != 4 ||
            !configuration.HoldoutLoci.SequenceEqual(["f3r", "f3v", "f4r", "f4v"]) ||
            configuration.DictionarySources.Select(source => source.SourceId)
                .Distinct(StringComparer.Ordinal).Count() != configuration.DictionarySources.Length)
        {
            throw new InvalidDataException("M4-03 configuration does not preserve the frozen partition or source inventory.");
        }
    }

    private static M4CandidateContext[] BuildContexts(
        M4ContextConfiguration configuration,
        PronunciationSnapshot[] snapshots,
        M4ContextRequest[] requests)
    {
        var rows = new List<M4CandidateContext>();
        foreach (var source in configuration.DictionarySources.OrderBy(value => value.SourceId, StringComparer.Ordinal))
        {
            var sourceRequests = requests.Where(request => request.SourceId == source.SourceId).ToArray();
            var snapshot = snapshots.Single(value => value.Id == source.SourceId);
            if (source.Join == "cdsl-record-and-headword")
            {
                using var reader = new StreamReader(snapshot.Path, StrictUtf8, true);
                rows.AddRange(JoinCdsl(reader, sourceRequests, source.Manifest.Path));
            }
            else if (source.Join == "wiktextract-lexical-identity" &&
                     !string.IsNullOrEmpty(source.DictionaryLanguageCode))
            {
                using var stream = File.OpenRead(snapshot.Path);
                using var gzip = new GZipStream(stream, CompressionMode.Decompress);
                using var reader = new StreamReader(gzip, StrictUtf8, false, 1_048_576);
                rows.AddRange(JoinWiktextract(
                    reader,
                    sourceRequests,
                    source.DictionaryLanguageCode,
                    source.Manifest.Path));
            }
            else
            {
                throw new InvalidDataException($"Unsupported contextual join: {source.Join}.");
            }
        }

        var unknown = requests.Select(request => request.SourceId)
            .Except(configuration.DictionarySources.Select(source => source.SourceId), StringComparer.Ordinal)
            .ToArray();
        if (unknown.Length != 0)
        {
            throw new InvalidDataException("No contextual source is registered for: " + string.Join(", ", unknown));
        }

        return [.. rows.OrderBy(row => row.Language, StringComparer.Ordinal)
            .ThenBy(row => row.CandidateId, StringComparer.Ordinal)
            .ThenBy(row => row.EntryId, StringComparer.Ordinal)
            .ThenBy(row => row.ContextOrdinal)];
    }

    private (M4AstronomyLanguage[] Languages, M4AstronomyPair[] Pairs, HashedArtifact[] Inputs)
        BuildAstronomy(M4ContextConfiguration configuration)
    {
        var protocol = ReadJson<AstronomicalNameProtocol>(files.Resolve(configuration.Astronomy.Protocol.Path));
        var panel = PronunciationPanel.Load(
            files.Resolve(configuration.Astronomy.PronunciationPanel.Path),
            repositoryRoot);
        if (protocol.PronunciationPanel != configuration.Astronomy.PronunciationPanel.Path ||
            protocol.PronunciationPanelSha256 != configuration.Astronomy.PronunciationPanel.Sha256)
        {
            throw new InvalidDataException("M4-03 astronomy panel does not match the frozen M2-08 registration.");
        }

        using var registration = JsonDocument.Parse(
            File.ReadAllText(files.Resolve(configuration.Astronomy.Registration.Path), StrictUtf8));
        if (registration.RootElement.GetProperty("status").GetString() != "registered_not_evaluated" ||
            registration.RootElement.GetProperty("artifact_sha256").GetString() !=
            configuration.Astronomy.Catalog.Sha256)
        {
            throw new InvalidDataException("M4-03 astronomy registration is not the frozen M2-08 registry.");
        }

        var catalog = File.ReadLines(files.Resolve(configuration.Astronomy.Catalog.Path), StrictUtf8)
            .Select(line => JsonSerializer.Deserialize<AstronomicalName>(
                line,
                AutomatedProtocol.JsonOptions)
                ?? throw new InvalidDataException("Empty astronomical catalog row."))
            .ToArray();
        var catalogByKey = catalog.GroupBy(name => name.MatchKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var languages = new List<M4AstronomyLanguage>();
        var idsByLanguage = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var inputs = new List<HashedArtifact>();
        foreach (var profile in panel.Profiles.OrderBy(value => value.Language, StringComparer.Ordinal))
        {
            var manifestPath = files.Resolve(Path.Combine(
                panel.OutputDirectory,
                profile.ArtifactStem + ".manifest.json"));
            var profilePath = files.Resolve(Path.Combine(
                panel.OutputDirectory,
                profile.ArtifactStem + ".jsonl"));
            VerifyProfileArtifact(manifestPath, profilePath, profile);
            inputs.Add(ExperimentHashing.Artifact(manifestPath, repositoryRoot));
            inputs.Add(ExperimentHashing.Artifact(profilePath, repositoryRoot));
            var entries = File.ReadLines(profilePath, StrictUtf8)
                .Select(line => JsonSerializer.Deserialize<PronunciationNormalizedEntry>(
                    line,
                    PronunciationAudit.JsonOptions)
                    ?? throw new InvalidDataException($"Empty profile row: {profilePath}"))
                .ToArray();
            var matchingKeys = entries.Select(entry => entry.OriginalForm.Trim().Normalize(NormalizationForm.FormC))
                .Where(catalogByKey.ContainsKey)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var linkedNames = matchingKeys.SelectMany(key => catalogByKey[key]).ToArray();
            var ids = linkedNames.Select(name => name.CatalogId).ToHashSet(StringComparer.Ordinal);
            idsByLanguage.Add(profile.Language, ids);
            languages.Add(new M4AstronomyLanguage(
                profile.Language,
                profile.Id,
                entries.Length,
                catalog.Length,
                linkedNames.Length,
                ids.Count,
                matchingKeys.Count(key => catalogByKey[key].Length > 1),
                catalogByKey.Count - matchingKeys.Length,
                ids.Count == 0 ? "no_exact_links" : "linked",
                RunId,
                configuration.Astronomy.Catalog.Path));
        }

        var arabic = idsByLanguage["ara"];
        var pairs = languages.Where(language => language.Language != "ara")
            .OrderBy(language => language.Language, StringComparer.Ordinal)
            .Select(language =>
            {
                var shared = arabic.Intersect(idsByLanguage[language.Language], StringComparer.Ordinal).Count();
                var estimable = shared >= configuration.Astronomy.MinimumSharedCatalogIds;
                return new M4AstronomyPair(
                    "ara",
                    language.Language,
                    shared,
                    estimable ? shared : 0,
                    0,
                    estimable ? "registered_not_scored" : "not_estimable",
                    estimable
                        ? "phonological_astronomy_scoring_not_run_by_contextual_appendix"
                        : "fewer_than_25_shared_catalog_ids",
                    RunId,
                    configuration.Astronomy.Catalog.Path);
            })
            .ToArray();
        return ([.. languages], pairs, [.. inputs]);
    }

    private static M4ContextRequest[] LoadContextRequests(string path)
    {
        var memberships = ReadGzipLines<AutomatedMembership>(path);
        var units = memberships.GroupBy(row => row.Unit.Id, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var signatures = group.Select(row => JsonSerializer.Serialize(
                    row.Unit,
                    AutomatedProtocol.JsonOptions)).Distinct(StringComparer.Ordinal).ToArray();
                if (signatures.Length != 1)
                {
                    throw new InvalidDataException($"Candidate {group.Key} changes across frozen lexicon samples.");
                }

                return group.First().Unit;
            })
            .ToArray();
        return [.. units.SelectMany(unit => unit.Memberships.Select(membership => new M4ContextRequest(
                unit.Id,
                unit.Language,
                unit.Lemma,
                membership.EntryId,
                membership.SourceId,
                membership.SourceRecordId)))
            .Distinct()
            .OrderBy(request => request.Language, StringComparer.Ordinal)
            .ThenBy(request => request.CandidateId, StringComparer.Ordinal)
            .ThenBy(request => request.EntryId, StringComparer.Ordinal)];
    }

    private static M4CandidateContext[] BuildContextRows<TKey>(
        IReadOnlyList<M4ContextRequest> requests,
        IReadOnlyDictionary<TKey, List<(string? PartOfSpeech, string? Text)>> matches,
        string contextKind,
        string sourceArtifact,
        Func<M4ContextRequest, TKey> keySelector)
        where TKey : notnull
    {
        var rows = new List<M4CandidateContext>();
        foreach (var request in requests.OrderBy(value => value.CandidateId, StringComparer.Ordinal)
                     .ThenBy(value => value.EntryId, StringComparer.Ordinal))
        {
            var found = matches.GetValueOrDefault(keySelector(request), []);
            var status = found.Count switch
            {
                0 => "missing_source_record",
                > 1 => "ambiguous_source_record",
                _ => "matched",
            };
            if (found.Count == 0)
            {
                rows.Add(ContextRow(request, null, 0, "none", null, [], status, sourceArtifact));
                continue;
            }

            for (var index = 0; index < found.Count; index++)
            {
                rows.Add(ContextRow(
                    request,
                    found[index].PartOfSpeech,
                    index + 1,
                    string.IsNullOrWhiteSpace(found[index].Text) ? "none" : contextKind,
                    found[index].Text,
                    [],
                    string.IsNullOrWhiteSpace(found[index].Text) && status == "matched" ? "missing_context" : status,
                    sourceArtifact));
            }
        }

        return [.. rows];
    }

    private static IEnumerable<(string? PartOfSpeech, string? Text, string[] Labels)>
        ReadWiktextractContexts(JsonElement root)
    {
        var partOfSpeech = PronunciationDictionaryAdapter.GetString(root, "pos");
        var entryLabels = ReadStrings(root, "categories")
            .Concat(ReadStrings(root, "topics"))
            .Concat(ReadStrings(root, "tags"))
            .Concat(ReadStrings(root, "raw_tags"));
        if (!root.TryGetProperty("senses", out var senses) || senses.ValueKind != JsonValueKind.Array)
        {
            yield return (partOfSpeech, null, [.. entryLabels.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
            yield break;
        }

        var emitted = false;
        foreach (var sense in senses.EnumerateArray())
        {
            var labels = entryLabels
                .Concat(ReadStrings(sense, "categories"))
                .Concat(ReadStrings(sense, "topics"))
                .Concat(ReadStrings(sense, "tags"))
                .Concat(ReadStrings(sense, "raw_tags"))
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray();
            var glosses = ReadStrings(sense, "glosses");
            if (glosses.Length == 0)
            {
                glosses = ReadStrings(sense, "raw_glosses");
            }

            if (glosses.Length == 0)
            {
                emitted = true;
                yield return (partOfSpeech, null, labels);
                continue;
            }

            foreach (var gloss in glosses)
            {
                emitted = true;
                yield return (partOfSpeech, gloss.Normalize(NormalizationForm.FormC), labels);
            }
        }

        if (!emitted)
        {
            yield return (partOfSpeech, null, [.. entryLabels.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]);
        }
    }

    private void VerifyProfileArtifact(
        string manifestPath,
        string profilePath,
        PronunciationProfile profile)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath, StrictUtf8));
        var root = document.RootElement;
        var artifact = root.GetProperty("artifacts").EnumerateArray().Single(value =>
            files.Resolve(value.GetProperty("path").GetString()!) == profilePath);
        if (root.GetProperty("profile_sha256").GetString() != profile.Fingerprint ||
            artifact.GetProperty("sha256").GetString() != PronunciationAudit.Hash(profilePath))
        {
            throw new InvalidDataException($"Modified astronomy pronunciation profile: {profile.Id}.");
        }
    }

    private void VerifyConfigurationPin(M4ContextConfigurationPin pin)
    {
        var path = files.Resolve(pin.Path);
        if (!File.Exists(path) || PronunciationAudit.HashConfiguration(path) != pin.Sha256)
        {
            throw new InvalidDataException("M4-03 configuration pin mismatch: " + pin.Path);
        }
    }

    private void VerifyArtifactPin(M4ContextArtifactPin pin)
    {
        var path = files.Resolve(pin.Path);
        if (!File.Exists(path) || new FileInfo(path).Length != pin.Bytes ||
            PronunciationAudit.Hash(path) != pin.Sha256)
        {
            throw new InvalidDataException("M4-03 artifact pin mismatch: " + pin.Path);
        }
    }

    private static T ReadJson<T>(string path) =>
        JsonSerializer.Deserialize<T>(
            File.ReadAllText(path, StrictUtf8),
            AutomatedProtocol.JsonOptions)
        ?? throw new InvalidDataException($"Empty JSON document: {path}");

    private static T[] ReadGzipLines<T>(string path)
    {
        using var stream = File.OpenRead(path);
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, StrictUtf8);
        var rows = new List<T>();
        while (reader.ReadLine() is { } line)
        {
            rows.Add(JsonSerializer.Deserialize<T>(line, AutomatedProtocol.JsonOptions)
                ?? throw new InvalidDataException($"Empty JSON line in {path}."));
        }

        return [.. rows];
    }

    private static (string RecordId, string Lemma) RequestedCdslKey(M4ContextRequest request)
    {
        var separator = request.SourceRecordId.IndexOf(':', StringComparison.Ordinal);
        return separator < 0
            ? (request.SourceRecordId, request.Lemma)
            : (request.SourceRecordId[..separator], request.SourceRecordId[(separator + 1)..]);
    }

    private static M4CandidateContext ContextRow(
        M4ContextRequest request,
        string? partOfSpeech,
        int ordinal,
        string kind,
        string? context,
        string[] labels,
        string status,
        string sourceArtifact) =>
        new(
            request.CandidateId,
            request.Language,
            request.Lemma,
            request.EntryId,
            request.SourceId,
            request.SourceRecordId,
            partOfSpeech,
            ordinal,
            kind,
            context,
            labels,
            status,
            RunId,
            sourceArtifact);

    private static string[] ReadStrings(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{property} must be an array.");
        }

        return [.. value.EnumerateArray().Select(item => item.GetString()
            ?? throw new InvalidDataException($"{property} cannot contain null."))];
    }

    private static void WriteContextCsv(string path, IEnumerable<M4CandidateContext> rows) =>
        WriteCsv(
            path,
            "candidate_id,language,lemma,entry_id,source_id,source_record_id,part_of_speech,context_ordinal,context_kind,context,labels,join_status,run_id,source_artifact",
            rows.Select(row => string.Join(',',
                Csv(row.CandidateId),
                Csv(row.Language),
                Csv(row.Lemma),
                Csv(row.EntryId),
                Csv(row.SourceId),
                Csv(row.SourceRecordId),
                Csv(row.PartOfSpeech ?? string.Empty),
                row.ContextOrdinal.ToString(CultureInfo.InvariantCulture),
                Csv(row.ContextKind),
                Csv(row.Context ?? string.Empty),
                Csv(string.Join('|', row.Labels)),
                Csv(row.JoinStatus),
                Csv(row.RunId),
                Csv(row.SourceArtifact))));

    private static void WriteAstronomyLanguageCsv(string path, IEnumerable<M4AstronomyLanguage> rows) =>
        WriteCsv(
            path,
            "language,profile_id,profile_records,catalog_names,linked_name_records,linked_catalog_ids,ambiguous_match_keys,missing_catalog_names,status,run_id,source_artifact",
            rows.Select(row => string.Join(',',
                Csv(row.Language),
                Csv(row.ProfileId),
                Integer(row.ProfileRecords),
                Integer(row.CatalogNames),
                Integer(row.LinkedNameRecords),
                Integer(row.LinkedCatalogIds),
                Integer(row.AmbiguousMatchKeys),
                Integer(row.MissingCatalogNames),
                Csv(row.Status),
                Csv(row.RunId),
                Csv(row.SourceArtifact))));

    private static void WriteAstronomyPairCsv(string path, IEnumerable<M4AstronomyPair> rows) =>
        WriteCsv(
            path,
            "target_language,comparison_language,shared_catalog_ids,balanced_objects,null_repetitions,status,diagnostic,run_id,source_artifact",
            rows.Select(row => string.Join(',',
                Csv(row.TargetLanguage),
                Csv(row.ComparisonLanguage),
                Integer(row.SharedCatalogIds),
                Integer(row.BalancedObjects),
                Integer(row.NullRepetitions),
                Csv(row.Status),
                Csv(row.Diagnostic),
                Csv(row.RunId),
                Csv(row.SourceArtifact))));

    private static void WriteReport(
        string path,
        M4CandidateContext[] contexts,
        M4AstronomyLanguage[] languages,
        M4AstronomyPair[] pairs)
    {
        var candidates = contexts.Select(row => row.CandidateId).Distinct(StringComparer.Ordinal).Count();
        var joined = contexts.Where(row => row.JoinStatus == "matched" && row.Context != null)
            .Select(row => row.CandidateId).Distinct(StringComparer.Ordinal).Count();
        string[] contextSection =
        [
            string.Empty,
            $"Source: [candidate-contexts.csv](candidate-contexts.csv), run `{RunId}`.",
            string.Empty,
            "The frozen M4-01 outputs do not retain nearest-candidate identities by query, so these contexts cannot be " +
            "associated with individual Voynich query scores without rerunning or changing the frozen analysis. No such " +
            "association is inferred here.",
            string.Empty,
            "## Separately registered astronomical names",
            string.Empty,
            "| Language | Profile records | Linked catalog IDs | Ambiguous keys | Status |",
            "| --- | ---: | ---: | ---: | --- |",
        ];
        string[] astronomySection =
        [
            string.Empty,
            $"Source: [astronomy-languages.csv](astronomy-languages.csv), run `{RunId}`.",
            string.Empty,
            $"All {pairs.Length} preregistered Arabic-versus-panel-language pairs are reported in " +
            "[astronomy-pairs.csv](astronomy-pairs.csv). No pair reached the frozen minimum of 25 shared catalog IDs, " +
            "so balanced scoring and null generation were not estimable; unmatched objects were not assigned zero scores.",
            string.Empty,
            "Catalog names are descriptive external context. They do not establish Arabic etymology, pronunciation, " +
            "or a claim that any Voynich token denotes a star.",
            string.Empty,
            "## Reproduction",
            string.Empty,
            "From `source/`, with the pinned dictionary and HYG snapshots retained:",
            string.Empty,
            "```powershell",
            "dotnet run --project Enochian.Benchmark -- context-m4 ..\\experiments\\contextual-appendix.m4-03.json",
            "```",
            string.Empty,
            "The appendix is optional. M4-02 and the future holdout workflow do not consume its outputs.",
            string.Empty,
            "## Provenance and permissions",
            string.Empty,
            "Sanskrit definitions are from the pinned CDSL Shabda-Sagara snapshot; Turkish glosses and labels are " +
            "from the pinned English Wiktionary/Kaikki extraction; astronomical names are from pinned HYG 4.1. " +
            "All three sources are recorded as CC BY-SA 4.0 in their checked manifests. The redistributed contextual " +
            "text remains under CC BY-SA 4.0 rather than the repository's software license. Source identifiers are retained " +
            "in the CSV tables and every input/output byte hash is recorded in [manifest.json](manifest.json).",
        ];
        var lines = new[]
        {
            "# M4-03 automated contextual appendix",
            string.Empty,
            $"Run ID: `{RunId}`. This optional appendix was generated only after the M4-02 quantitative report was frozen.",
            string.Empty,
            "## Interpretation boundary",
            string.Empty,
            "This is secondary exploratory metadata, not semantic ground truth. It did not use human or LLM annotation, " +
            "did not score gloss plausibility, did not tune the phonological mapping, and did not read holdout-derived data. " +
            "Definitions and proper-name metadata remain separate from every primary matching and statistical artifact.",
            string.Empty,
            "## Frozen candidate contexts",
            string.Empty,
            $"{candidates} unique sampled lexical candidates were joined by frozen candidate/source identifiers; " +
            $"{joined} have at least one source context. Every available source gloss and label is retained without choosing " +
            "an appealing sense. Repeated lexicon-sample membership does not duplicate candidates.",
            string.Empty,
            "| Language | Candidates | With context | Context rows | Missing/ambiguous rows |",
            "| --- | ---: | ---: | ---: | ---: |",
        }
        .Concat(contexts.GroupBy(row => row.Language, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group =>
                $"| {group.Key} | {group.Select(row => row.CandidateId).Distinct(StringComparer.Ordinal).Count()} | " +
                $"{group.Where(row => row.Context != null).Select(row => row.CandidateId).Distinct(StringComparer.Ordinal).Count()} | " +
                $"{group.Count(row => row.Context != null)} | {group.Count(row => row.JoinStatus != "matched")} |"))
        .Concat(contextSection)
        .Concat(languages.Select(row =>
            $"| {row.Language} | {row.ProfileRecords} | {row.LinkedCatalogIds} | " +
            $"{row.AmbiguousMatchKeys} | {row.Status} |"))
        .Concat(astronomySection);
        WriteText(path, string.Join('\n', lines) + "\n");
    }

    private static void WriteCsv(string path, string header, IEnumerable<string> rows) =>
        WriteText(path, string.Join('\n', new[] { header }.Concat(rows)) + "\n");

    private static void WriteText(string path, string text) =>
        File.WriteAllText(path, text.Replace("\r\n", "\n", StringComparison.Ordinal), new UTF8Encoding(false));

    private static string Csv(string value) =>
        value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"'
            : value;

    private static string Integer(int value) => value.ToString(CultureInfo.InvariantCulture);

    private sealed class ContextMatches
    {
        public int EntryCount { get; set; }

        public List<(string? PartOfSpeech, string? Text, string[] Labels)> Contexts { get; } = [];
    }
}
