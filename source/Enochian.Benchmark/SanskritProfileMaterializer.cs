using Enochian.Provenance;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Enochian.Benchmark;

public sealed class SanskritProfileMaterializer(string repositoryRoot)
{
    private const string SourceId = "cdsl-shs";
    private const string SourceRevision = "b7297b97cf9f7112277ea98f7969291eb1d5f495";
    private const string ProfileStem = "san-dictionary-v2.0.0";
    private const string GeneratorId = "enochian-slp1-to-ipa-v1";
    private static readonly string[] RequiredBands = ["03-05", "06-09", "10-20"];
    private static readonly JsonSerializerOptions SourceJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true,
    };

    public int Run()
    {
        var panelPath = Path.Combine(
            repositoryRoot, "resources", "lexicons", "pronunciation-panel.m3-07.json");
        var panel = PronunciationPanel.Load(panelPath, repositoryRoot);
        var profile = panel.Profiles.Single(value => value.Id == "san-dictionary");
        var flow = new Enochian.Flow.Flow(
            Path.Combine(repositoryRoot, "samples", "ipatransducer.json"));
        var errors = flow.Errors.Select(error => error.Message).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        var features = flow.FeatureSets.Single(value => value.Id == "Default");
        var encoding = flow.Encodings.Single(value => value.Id == "IPA");
        var encoder = new Enochian.Text.Encoder(features, encoding);
        var sourcePath = Path.Combine(repositoryRoot, ".enoch", "cdsl-generated", "cdsl-shs.jsonl");
        var candidates = File.ReadLines(sourcePath, new UTF8Encoding(false, true))
            .Select(line => JsonSerializer.Deserialize<CdslProfileSource>(
                line, SourceJsonOptions)
                ?? throw new InvalidDataException("Empty normalized SHS record."))
            .Select(value => CreateCandidate(value, encoder))
            .Where(value => value != null)
            .Cast<SanskritCandidate>()
            .GroupBy(value => value.Fingerprint, StringComparer.Ordinal)
            .Select(group => group.OrderBy(
                value => EntryId(value.Source.SourceRecordId), StringComparer.Ordinal).First())
            .GroupBy(value => value.LengthBand, StringComparer.Ordinal)
            .SelectMany(group => group.OrderBy(
                value => EntryId(value.Source.SourceRecordId), StringComparer.Ordinal).Take(40))
            .OrderBy(value => value.LengthBand, StringComparer.Ordinal)
            .ThenBy(value => EntryId(value.Source.SourceRecordId), StringComparer.Ordinal)
            .ToArray();
        var bands = candidates.GroupBy(value => value.LengthBand, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        if (candidates.Length < 100 ||
            RequiredBands.Any(
                band => !bands.TryGetValue(band, out var count) || count < 25))
        {
            throw new InvalidDataException(
                "Pinned SHS data cannot supply the frozen balanced pronunciation capacity.");
        }

        var outputDirectory = Path.Combine(repositoryRoot, ".enoch", "pronunciation-profiles", "m3-07");
        _ = Directory.CreateDirectory(outputDirectory);
        var entriesPath = Path.Combine(outputDirectory, ProfileStem + ".jsonl");
        var evidencePath = Path.Combine(outputDirectory, ProfileStem + ".evidence.jsonl");
        var mappingPath = Path.Combine(repositoryRoot, "resources", "encodings", "slp1.json");
        var generatorRevision = PronunciationAudit.HashConfiguration(mappingPath);
        WriteJsonLines(entriesPath, candidates.Select(value => CreateEntry(value, profile)));
        WriteJsonLines(
            evidencePath,
            candidates.Select(value => CreateEvidence(value, profile, generatorRevision)));

        var manifestPath = Path.Combine(outputDirectory, ProfileStem + ".manifest.json");
        var manifest = new
        {
            schema_version = "1.0.0",
            profile,
            profile_sha256 = profile.Fingerprint,
            panel_sha256 = PronunciationAudit.HashConfiguration(panelPath),
            software_sha256 = PronunciationAudit.HashConfiguration(Path.Combine(
                repositoryRoot, "source", "Enochian.Benchmark", "SanskritProfileMaterializer.cs")),
            feature_sha256 = PronunciationAudit.Hash(features.AbsoluteFilePath),
            encoding_sha256 = PronunciationAudit.Hash(encoding.AbsoluteFilePath),
            source_manifests = new[]
            {
                new
                {
                    path = "resources/lexicons/manifests/cdsl-shs.manifest.json",
                    sha256 = PronunciationAudit.Hash(Path.Combine(
                        repositoryRoot, "resources", "lexicons", "manifests", "cdsl-shs.manifest.json")),
                },
            },
            artifacts = new[]
            {
                Pin(".enoch/pronunciation-profiles/m3-07/" + ProfileStem + ".jsonl", entriesPath),
                Pin(".enoch/pronunciation-profiles/m3-07/" + ProfileStem + ".evidence.jsonl", evidencePath),
            },
            status = "candidate",
            blockers = Array.Empty<string>(),
        };
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, ManifestJsonOptions)
                .ReplaceLineEndings("\n") + "\n",
            new UTF8Encoding(false));
        Console.WriteLine(
            $"Wrote {candidates.Length} definitions-free SHS records " +
            $"({string.Join(", ", bands.OrderBy(value => value.Key).Select(
                value => $"{value.Key}={value.Value}"))}).");
        Console.WriteLine($"Manifest SHA-256: {PronunciationAudit.HashConfiguration(manifestPath)}");
        return 0;
    }

    private static SanskritCandidate? CreateCandidate(
        CdslProfileSource source,
        Enochian.Text.Encoder encoder)
    {
        if (source.Source != SourceId || source.SourceVersion != SourceRevision ||
            source.Language != "san" || source.SourceEncoding != "SLP1" ||
            string.IsNullOrWhiteSpace(source.Ipa) || string.IsNullOrWhiteSpace(source.Form))
        {
            return null;
        }

        var normalizedIpa = PronunciationPolicyEvaluator.NormalizeIpa(source.Ipa);
        var (_, _, phones) = encoder.GetTextAndPhones(
            normalizedIpa.Normalize(NormalizationForm.FormD), out var unknown);
        if (unknown.Count != 0 || phones.Count is < 3 or > 20)
        {
            return null;
        }

        return new(source, normalizedIpa, BenchmarkSampling.GetLengthBand(phones.Count), phones.Count,
            PronunciationPolicyEvaluator.WeightedDistributionKey(
                [((IReadOnlyList<double[]>)phones, 1.0)]));
    }

    private static PronunciationNormalizedEntry CreateEntry(
        SanskritCandidate candidate,
        PronunciationProfile profile)
    {
        var id = EntryId(candidate.Source.SourceRecordId);
        return new()
        {
            EntryId = SourceId + ":san:" + id,
            SourceRecordId = id,
            Language = "san",
            Family = "Indo-European/Indo-Iranian/Indo-Aryan",
            Source = SourceId,
            SourceVersion = SourceRevision,
            Lemma = candidate.Source.Lemma,
            OriginalForm = candidate.Source.OriginalForm,
            Form = candidate.Source.Form,
            EntryKind = "lemma",
            Dialect = profile.ArtifactStem,
            PartOfSpeech = "dictionary-headword",
            Definition = null,
            Frequency = null,
            SourceEncoding = "IPA",
            Ipa = candidate.NormalizedIpa,
            UnicodeNormalization = "NFC",
            License = "CC-BY-SA-4.0",
        };
    }

    private static PronunciationEvidence CreateEvidence(
        SanskritCandidate candidate,
        PronunciationProfile profile,
        string generatorRevision)
    {
        var id = EntryId(candidate.Source.SourceRecordId);
        return new()
        {
            EntryId = SourceId + ":san:" + id,
            Source = SourceId,
            Snapshot = SourceRevision,
            SourceEntry = "https://github.com/sanskrit-lexicon/csl-orig/blob/" + SourceRevision +
                "/v02/shs/shs.txt#record-" + Uri.EscapeDataString(candidate.Source.SourceRecordId),
            Language = "san",
            Lemma = candidate.Source.Lemma,
            PartOfSpeech = "dictionary-headword",
            LexicalIdentity = candidate.Source.SourceRecordId + ":" + candidate.Source.OriginalForm,
            Convention = profile.Convention,
            Labels = [],
            OriginalIpa = candidate.Source.Ipa,
            NormalizedIpa = candidate.NormalizedIpa,
            Origin = "converter-generated",
            Generator = GeneratorId,
            GeneratorRevision = generatorRevision,
            Dependencies = [SourceId, "resources/encodings/slp1.json"],
            AlternativeCount = 1,
            AlternativeWeight = 1,
            PhonemeLength = candidate.PhonemeLength,
            LengthBand = candidate.LengthBand,
            UnknownSegments = [],
            Exclusions = [],
            EvidenceStatus = "supported",
        };
    }

    private static string EntryId(string sourceRecordId)
    {
        var bytes = Encoding.UTF8.GetBytes(SourceId + "\0" + sourceRecordId);
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static object Pin(string path, string absolutePath) =>
        new { path, sha256 = PronunciationAudit.Hash(absolutePath) };

    private static void WriteJsonLines<T>(string path, IEnumerable<T> values)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
        foreach (var value in values)
        {
            writer.WriteLine(JsonSerializer.Serialize(value, PronunciationAudit.JsonOptions));
        }
    }

    private sealed record SanskritCandidate(
        CdslProfileSource Source,
        string NormalizedIpa,
        string LengthBand,
        int PhonemeLength,
        string Fingerprint);

    private sealed record CdslProfileSource
    {
        public required string SourceRecordId { get; init; }
        public required string Language { get; init; }
        public required string Source { get; init; }
        public required string SourceVersion { get; init; }
        public required string Lemma { get; init; }
        public required string OriginalForm { get; init; }
        public required string Form { get; init; }
        public required string SourceEncoding { get; init; }
        public required string Ipa { get; init; }
    }
}
