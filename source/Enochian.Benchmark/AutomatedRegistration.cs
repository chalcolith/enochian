using Enochian.Provenance;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using IpaEncoder = Enochian.Text.Encoder;

namespace Enochian.Benchmark;

public static class AutomatedRegistration
{
    private static readonly string[] SyntheticLanguages = ["tar", "ctl"];
    private static readonly string[] SyntheticFilters = ["synthetic"];

    public static void Freeze(string repositoryRoot)
    {
        var files = new AutomatedFiles(repositoryRoot);
        if (File.Exists(files.Resolve("experiments/confirmatory.v2.json")))
        {
            throw new InvalidDataException("V2 is already registered; amendments require a new protocol file/version.");
        }

        var panel = PronunciationPanel.Load(files.Resolve("resources/lexicons/pronunciation-panel.v1.json"), repositoryRoot);
        var plan = JsonSerializer.Deserialize<PronunciationPlan>(
            File.ReadAllText(files.Resolve(panel.DictionaryProtocol)), PronunciationAudit.JsonOptions)!;
        var families = plan.Languages.ToDictionary(language => language.Language, language => language.Family, StringComparer.Ordinal);
        var sources = panel.Profiles.Select(profile => new AutomatedSource
        {
            ProfileId = profile.Id,
            Language = profile.Language,
            Family = families[profile.Language],
            Required = profile.Language is "hin" or "san" or "tur" or "hun",
            ConfirmatoryEligible = profile.Language != "rom",
            ProfileManifest = Pin(repositoryRoot, panel.OutputDirectory + "/" + profile.ArtifactStem + ".manifest.json"),
        }).ToArray();
        var protocol = Template(repositoryRoot, "dictionary-first-confirmatory-v2", "confirmatory",
            Pin(repositoryRoot, "resources/lexicons/pronunciation-panel.v1.json"),
            new(Pin(repositoryRoot, "samples/ipatransducer.json"), Pin(repositoryRoot, "resources/encodings/features.json"),
                Pin(repositoryRoot, "resources/encodings/ipa.json")), sources) with
        {
            Groups = new(StringComparer.Ordinal)
            {
                ["indo-aryan"] = ["hin", "san", "asm", "ben", "bho", "guj", "mar", "pan", "rom"],
                ["unrelated-controls"] = ["tur", "hun"],
                ["latin-greek"] = ["lat", "grc"],
                ["iranian-bridge"] = ["fas", "pus"],
                ["arabic"] = ["ara"],
                ["hebrew-descriptive-only"] = ["hbo"],
            },
            Contrasts =
            [
                new("indo-aryan-versus-unrelated-controls", true, true, ["indo-aryan"], ["unrelated-controls"]),
                new("indo-aryan-versus-latin-greek", false, false, ["indo-aryan"], ["latin-greek"]),
                new("iranian-bridge-versus-unrelated-controls", false, false, ["iranian-bridge"], ["unrelated-controls"]),
                new("arabic-versus-indo-aryan", false, false, ["arabic"], ["indo-aryan"]),
            ],
            Repetitions = 20,
            NullRepetitions = 100,
            PermutationCount = 10000,
            BootstrapCount = 2000,
            EvaluationLoci = ["f1r", "f1v", "f2r", "f2v"],
            HoldoutLoci = ["f3r", "f3v", "f4r", "f4v"],
            OutputDirectory = ".enoch/experiments/automated-v2",
            AdmissionReport = "reports/automated-admission/v2/coverage.json",
        };
        AutomatedFiles.Write(files.Resolve("experiments/confirmatory.v2.json"), protocol);
    }

    public static string CreateSynthetic(string repositoryRoot, string directory)
    {
        directory = directory.Replace('\\', '/').TrimEnd('/');
        var files = new AutomatedFiles(repositoryRoot);
        _ = Directory.CreateDirectory(files.Resolve(directory));
        var ipaPath = directory + "/synthetic-ipa.json";
        var alphabet = new[] { "p", "t", "s", "k", "m", "n" };
        var dimensions = new[] { "Labial", "Coronal", "Continuant", "Dorsal", "Nasal", "Lateral" };
        AutomatedFiles.Write(files.Resolve(ipaPath), new
        {
            Id = "IPA",
            Description = "Artificial integration-test encoding; not linguistic pronunciation data.",
            Patterns = alphabet.Select((symbol, index) => new { Input = symbol, Features = new[] { "+" + dimensions[index] } }).ToArray(),
        });
        // Flow resource keys use the core configuration convention, not the v2 snake_case contract.
        var flow = new JsonObject
        {
            ["id"] = "Synthetic v2 fixture",
            ["features"] = new JsonArray(new JsonObject { ["path"] = Relative("resources/encodings/features.json") }),
            ["encodings"] = new JsonArray(new JsonObject { ["features"] = "Default", ["path"] = "synthetic-ipa.json" }),
            ["steps"] = new JsonArray(),
        };
        var flowPath = directory + "/flow.json";
        AutomatedFiles.Write(files.Resolve(flowPath), flow);
        var configured = new Enochian.Flow.Flow(files.Resolve(flowPath));
        if (configured.Errors.Any())
        {
            throw new InvalidDataException(string.Join(", ", configured.Errors.Select(error => error.Message)));
        }

        var features = configured.FeatureSets.Single(candidate => candidate.Id == "Default");
        var encoding = configured.Encodings.Single(candidate => candidate.Id == "IPA");
        var encoder = new IpaEncoder(features, encoding);
        var profiles = SyntheticLanguages.Select(language => new PronunciationProfile
        {
            Id = language + "-synthetic",
            Language = language,
            Version = "1.0.0",
            Convention = "artificial-test-only",
            RequiredLabels = ["Synthetic"],
            RejectedLabels = ["Wrong"],
            Fallback = "none",
            Rules = new(StringComparer.Ordinal) { ["scope"] = "Artificial executable fixture; never research evidence." },
        }).ToArray();
        var panel = new PronunciationPanel
        {
            SchemaVersion = "1.0.0",
            PolicyId = "synthetic-v2-panel",
            Frozen = true,
            DictionaryProtocol = "resources/lexicons/pronunciation-audit.protocol.json",
            DictionaryProtocolSha256 = Pin(repositoryRoot, "resources/lexicons/pronunciation-audit.protocol.json").Sha256,
            OutputDirectory = directory,
            ReportDirectory = directory + "/reports",
            FallbackSourceManifests = [],
            Profiles = profiles,
        };
        var panelPath = directory + "/panel.json";
        AutomatedFiles.Write(files.Resolve(panelPath), panel);
        var sequences = (from first in alphabet
                         from second in alphabet
                         from third in alphabet
                         where first != second && second != third
                         select first + second + third).Take(100).ToArray();
        foreach (var profile in profiles)
        {
            var language = new PronunciationLanguage
            {
                Language = profile.Language,
                DictionaryCode = profile.Language,
                Family = "synthetic-" + profile.Language,
                Convention = profile.Convention,
                RequiredLabels = profile.RequiredLabels,
                RejectedLabels = profile.RejectedLabels,
            };
            var builder = new PronunciationCoverageBuilder(language, new(), encoder, profile);
            for (var index = 0; index < sequences.Length; index++)
            {
                var sequence = sequences[profile.Language == "tar" ? index : sequences.Length - index - 1];
                var repetitions = index < 34 ? 1 : index < 67 ? 2 : 4;
                var pronunciation = string.Concat(sequence.SelectMany(symbol => Enumerable.Repeat(symbol, repetitions)));
                var identity = "lexeme-" + index.ToString("D3", CultureInfo.InvariantCulture);
                builder.AddReading(new(profile.Id, "synthetic-v1", "https://example.com/" + profile.Id + "/" + identity,
                    profile.Language, identity, "noun", identity, pronunciation, ["Synthetic"],
                    "dictionary-supplied", null, [profile.Id], "MIT"));
            }

            var (_, evidence, entries, _) = builder.Build();
            var lexiconPath = directory + "/" + profile.ArtifactStem + ".jsonl";
            var evidencePath = directory + "/" + profile.ArtifactStem + ".evidence.jsonl";
            AutomatedFiles.WriteLines(files.Resolve(lexiconPath), entries);
            AutomatedFiles.WriteLines(files.Resolve(evidencePath), evidence);
            var manifestPath = directory + "/" + profile.Id + ".source.json";
            AutomatedFiles.Write(files.Resolve(manifestPath), new
            {
                SchemaVersion = "1.0.0",
                SourceId = profile.Id,
                Status = "acquired",
                Owner = "Enochian synthetic fixture",
                profile.Language,
                language.Family,
                Url = "https://example.com/" + profile.Id,
                Revision = new { Kind = "release", Value = "synthetic-v1" },
                RetrievalDate = "2026-11-05",
                Sha256 = PronunciationAudit.Hash(files.Resolve(lexiconPath)),
                License = "MIT",
                LicenseStatus = "verified",
                DistributionPolicy = "vendored",
                UsagePolicy = "unrestricted",
                Optional = false,
                DefaultBundle = false,
                Citation = "Artificial integration fixture; not a natural-language pronunciation dictionary.",
                Parser = new { Id = "synthetic-fixture", Version = "1.0.0" },
                RawPath = lexiconPath,
                GeneratedArtifactPath = lexiconPath,
                Filters = new { Include = SyntheticFilters, Exclude = Array.Empty<string>() },
            });
            AutomatedFiles.Write(files.Resolve(directory + "/" + profile.ArtifactStem + ".manifest.json"), new
            {
                SchemaVersion = "1.0.0",
                Profile = profile,
                ProfileSha256 = profile.Fingerprint,
                PanelSha256 = Pin(repositoryRoot, panelPath).Sha256,
                SoftwareSha256 = ExperimentHashing.HashValues(["synthetic-fixture-v2"]),
                FeatureSha256 = PronunciationAudit.Hash(features.AbsoluteFilePath),
                EncodingSha256 = PronunciationAudit.Hash(encoding.AbsoluteFilePath),
                SourceManifests = new[] { new PinnedInput(manifestPath, PronunciationAudit.Hash(files.Resolve(manifestPath))) },
                Artifacts = new[] { lexiconPath, evidencePath }.Select(path => new PinnedInput(path, PronunciationAudit.Hash(files.Resolve(path)))).ToArray(),
                Status = "synthetic",
                Blockers = Array.Empty<string>(),
            });
        }

        var sources = profiles.Select(profile => new AutomatedSource
        {
            ProfileId = profile.Id,
            Language = profile.Language,
            Family = "synthetic-" + profile.Language,
            Required = true,
            ConfirmatoryEligible = true,
            ProfileManifest = Pin(repositoryRoot, directory + "/" + profile.ArtifactStem + ".manifest.json"),
        }).ToArray();
        var queriesPath = directory + "/queries.jsonl";
        AutomatedFiles.WriteLines(files.Resolve(queriesPath), sequences.Take(4).Select((sequence, index) =>
            new AutomatedQuery("query-" + index.ToString(CultureInfo.InvariantCulture), "synthetic-evaluation",
                [.. sequence.Select(symbol => symbol.ToString())], index + 1)));
        var mappingPath = directory + "/mapping.json";
        AutomatedFiles.Write(files.Resolve(mappingPath), alphabet.ToDictionary(symbol => symbol,
            symbol => encoder.GetTextAndPhones(symbol).Item3.Single(), StringComparer.Ordinal));
        var protocol = Template(repositoryRoot, "synthetic-automated-v2", "synthetic", Pin(repositoryRoot, panelPath),
            new(Pin(repositoryRoot, flowPath), Pin(repositoryRoot, "resources/encodings/features.json"), Pin(repositoryRoot, ipaPath)), sources) with
        {
            Groups = new(StringComparer.Ordinal) { ["target"] = ["tar"], ["control"] = ["ctl"] },
            Contrasts = [new("synthetic-target-versus-control", true, true, ["target"], ["control"])],
            EvaluationLoci = ["synthetic-evaluation"],
            HoldoutLoci = ["synthetic-holdout"],
            Queries = new(queriesPath, PronunciationAudit.Hash(files.Resolve(queriesPath))),
            Mapping = Pin(repositoryRoot, mappingPath),
            OutputDirectory = directory + "/outputs",
            AdmissionReport = directory + "/admission.json",
        };
        var protocolPath = directory + "/run.json";
        AutomatedFiles.Write(files.Resolve(protocolPath), protocol);
        return files.Resolve(protocolPath);

        string Relative(string path) => Path.GetRelativePath(files.Resolve(directory), files.Resolve(path))
            .Replace(Path.DirectorySeparatorChar, '/');
    }

    private static AutomatedProtocol Template(
        string root, string id, string phase, PinnedInput panel, AutomatedPhonology phonology, AutomatedSource[] sources)
    {
        var retrievalPath = "experiments/retrieval-benchmark.protocol.json";
        var retrieval = BenchmarkProtocol.Load(Path.Combine(root, retrievalPath.Replace('/', Path.DirectorySeparatorChar)));
        return new()
        {
            Schema = "https://chalcolith.github.io/enochian/schemas/automated-experiment-2.json",
            SchemaVersion = "2.0.0",
            ConfigId = id,
            Phase = phase,
            Frozen = true,
            Phonology = phonology,
            Panel = panel,
            RetrievalPolicy = Pin(root, retrievalPath),
            Degradations = [.. retrieval.DegradationProfiles.Select(path =>
                Pin(root, Path.GetRelativePath(root, Path.GetFullPath(Path.Combine(root, "experiments", path.Replace('/', Path.DirectorySeparatorChar))))
                    .Replace(Path.DirectorySeparatorChar, '/')))],
            Sources = sources,
            Groups = new(StringComparer.Ordinal),
            Contrasts = [],
            LengthQuotas = new(StringComparer.Ordinal) { ["03-05"] = 34, ["06-09"] = 33, ["10-20"] = 33 },
            SamplingSeed = 15485863,
            NullSeed = 15485867,
            PermutationSeed = 32452843,
            Repetitions = 2,
            NullRepetitions = 8,
            PermutationCount = 32,
            BootstrapCount = 32,
            ConfidenceLevel = 0.95,
            Alpha = 0.05,
            CalibrationNull = "biphone-pseudoword",
            EvaluationLoci = [],
            HoldoutLoci = [],
            Queries = null,
            Mapping = null,
            Astronomy = new()
            {
                Registration = Pin(root, "reports/pronunciation-profiles/v1/astronomy-registration.json"),
                Policy = Pin(root, "experiments/astronomical-names.v1.json"),
                Manifest = Pin(root, "resources/lexicons/manifests/hyg-4.1.manifest.json"),
                Status = "exploratory_not_part_of_primary",
                MinimumSharedObjects = 25,
                Balancing = "paired-common-catalog-ids-equal-object-weights-stable-hash-selection",
                Contexts = "HYG-constellation-identifiers-only-no-inferred-manuscript-context",
                Nulls = ["catalog-label-permutation-within-language-and-length-band", "within-query-shuffle"],
                Multiplicity = "separate-holm-family-arabic-versus-each-other-panel-language-exploratory-only",
            },
            OutputDirectory = string.Empty,
            AdmissionReport = string.Empty,
        };
    }

    private static PinnedInput Pin(string root, string path) =>
        new(path, PronunciationAudit.HashConfiguration(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar))));
}
