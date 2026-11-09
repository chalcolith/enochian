using Enochian.Provenance;
using Enochian.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using IpaEncoding = Enochian.Text.Encoding;

namespace Enochian.Benchmark;

public sealed record AutomatedQuery(string QueryId, string Locus, string[] Symbols, int TokenFrequency);

public sealed record AutomatedMembership(string SchemaVersion, string SampleId, int Repetition, string Family, EvidenceUnit Unit);

public sealed record AutomatedScore(
    string SchemaVersion, string SampleId, int Repetition, string QueryId, int TokenFrequency,
    string Language, string Family, string NullKind, int NullRepetition, double Distance);

public sealed record AutomatedCalibration(
    string SchemaVersion, string SampleId, int Repetition, string QueryId, int TokenFrequency,
    string Language, string Family, double RawDistance, ScoreCalibration Calibration);

public sealed record AutomatedTest(
    string Id, string ContrastId, string AnalysisMode, bool Primary, bool Required, string Status,
    int QueryCount, double? Estimate, double? PValue, double? AdjustedPValue,
    BootstrapInterval? Interval, int CorrectionFamilySize, string? Diagnostic);

public sealed record AutomatedStage(string Name, string Identity, HashedArtifact[] Outputs);

public sealed record AutomatedRun(
    string SchemaVersion, string RunnerVersion, string RunId, string Status,
    HashedArtifact[] Inputs, AutomatedStage[] Stages);

public sealed record AutomatedPreflight(
    string SchemaVersion, string ConfigId, bool Ready, string[] Blockers, HashedArtifact[] Inputs);

public sealed class AutomatedExperiment(string repositoryRoot, FeatureSet features, IpaEncoding encoding)
{
    public const string RunnerVersion = "automated-experiment-v2";

    private readonly AutomatedFiles files = new(repositoryRoot);

    public static Enochian.Flow.Flow CreateFlow(string protocolPath, string repositoryRoot)
    {
        var protocol = AutomatedProtocol.Load(protocolPath, repositoryRoot);
        var inputs = new AutomatedFiles(repositoryRoot);
        var flow = new Enochian.Flow.Flow(inputs.Pin(protocol.Phonology.Flow));
        if (flow.Errors.Any())
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, flow.Errors.Select(error => error.Message)));
        }

        return flow;
    }

    public int Run(string protocolPath, string mode = "experiment")
    {
        var admission = new AutomatedAdmission(repositoryRoot, features, encoding).Evaluate(protocolPath);
        var protocol = admission.Protocol;
        if (mode == "admit")
        {
            return admission.Report.ReadyForEvaluation ? 0 : 1;
        }

        var blockers = admission.Report.Blockers.ToList();
        AutomatedQuery[] queries = [];
        SortedDictionary<string, double[]> mapping = new(StringComparer.Ordinal);
        try
        {
            (queries, mapping) = LoadQueries(protocol);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or JsonException or KeyNotFoundException)
        {
            Console.Error.WriteLine("V2 preflight: " + exception.Message);
            blockers.Add("queries_or_mapping:" + exception.Message);
        }

        ValidateAstronomy(protocol);
        TrackSoftware();
        var inputs = admission.Inputs.Concat(files.Inputs).GroupBy(input => input.Path, StringComparer.Ordinal)
            .Select(group => group.First()).OrderBy(input => input.Path, StringComparer.Ordinal).ToArray();
        var output = files.Resolve(protocol.OutputDirectory);
        if (admission.ProtectedPaths.Concat(files.ProtectedPaths).Any(path => IsInside(path, output)))
        {
            throw new InvalidDataException("V2 execution outputs must not contain input artifacts or the admission report.");
        }

        var preflight = new AutomatedPreflight("2.0.0", protocol.ConfigId, blockers.Count == 0,
            [.. blockers.Order(StringComparer.Ordinal)], inputs);
        var preflightPath = protocol.PreflightReport == null
            ? Path.Combine(output, "preflight.json")
            : files.Resolve(protocol.PreflightReport);
        if (admission.ProtectedPaths.Concat(files.ProtectedPaths)
            .Contains(preflightPath, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Preflight report must not overwrite a pinned input or admission report.");
        }

        AutomatedFiles.Write(preflightPath, preflight);
        if (!preflight.Ready)
        {
            Console.Error.WriteLine("V2 evaluation blocked: " + string.Join(", ", preflight.Blockers));
            return 1;
        }

        if (mode == "preflight")
        {
            return 0;
        }

        var identity = ExperimentHashing.HashValues(new[] { RunnerVersion, "2.0.0", Environment.Version.ToString(),
            RuntimeInformation.RuntimeIdentifier, RuntimeInformation.ProcessArchitecture.ToString() }
            .Concat(inputs.Select(input => input.Path + ":" + input.Sha256)));
        var manifestPath = Path.Combine(output, "run-manifest.json");
        if (File.Exists(manifestPath))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (document.RootElement.GetProperty("schema_version").GetString() != "2.0.0" ||
                document.RootElement.GetProperty("runner_version").GetString() != RunnerVersion)
            {
                throw new InvalidDataException("Incompatible checkpoint version; select a fresh v2 output directory.");
            }
        }

        AutomatedRun? previous = File.Exists(manifestPath)
            ? JsonSerializer.Deserialize<AutomatedRun>(File.ReadAllText(manifestPath), AutomatedProtocol.JsonOptions) : null;
        var stages = new List<AutomatedStage>();
        var memberships = Stage("sampling", () => Sample(admission)).ToArray();
        var scores = Stage("matching", () => Match(protocol, memberships, queries, mapping));
        var calibrated = Stage("calibration", () => Calibrate(protocol, scores)).ToArray();
        var tests = Stage("statistics", () => Analyze(protocol, calibrated)).ToArray();
        var failed = tests.Any(test => test.Required && test.Status != "estimated");
        AutomatedFiles.Write(Path.Combine(output, "report-inputs.json"), new
        {
            SchemaVersion = "2.0.0",
            RunId = identity,
            protocol.Phase,
            Runtime = new
            {
                Dotnet = Environment.Version.ToString(),
                RuntimeInformation.RuntimeIdentifier,
                ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString()
            },
            HumanAnnotationRequired = false,
            PronunciationAdmission = protocol.AdmissionReport,
            PrimaryMetric = "biphone-calibrated-empirical-percentile-lower-is-better",
            ReplicationUnit = "query-type-not-language-or-family",
            Multiplicity = "holm-across-all-registered-contrasts-and-both-analysis-modes",
            protocol.Astronomy,
            AstronomyExecution = "registered_not_evaluated_no_multilingual_catalog_links",
            Artifacts = stages.SelectMany(stage => stage.Outputs).ToArray(),
        });
        stages.Add(new("report-inputs", identity,
            [ExperimentHashing.Artifact(Path.Combine(output, "report-inputs.json"), repositoryRoot)]));
        AutomatedFiles.Write(manifestPath, new AutomatedRun("2.0.0", RunnerVersion, identity,
            failed ? "blocked_statistics" : "completed", inputs, [.. stages]));
        if (failed)
        {
            Console.Error.WriteLine("Required contrasts could not be estimated; inspect statistics.jsonl.");
        }

        return failed ? 1 : 0;

        IEnumerable<T> Stage<T>(string name, Func<IEnumerable<T>> produce)
        {
            var path = Path.Combine(output, name + ".jsonl");
            var cached = previous?.RunnerVersion == RunnerVersion && previous.SchemaVersion == "2.0.0" &&
                previous.RunId == identity
                ? previous.Stages.SingleOrDefault(stage => stage.Name == name && stage.Identity == identity) : null;
            var reusable = cached != null && cached.Outputs.Length == 1 && cached.Outputs[0].Path ==
                Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/') &&
                File.Exists(path) && ExperimentHashing.HashFile(path) == cached.Outputs[0].Sha256;
            if (!reusable)
            {
                AutomatedFiles.WriteLines(path, produce());
            }

            stages.Add(new(name, identity, [ExperimentHashing.Artifact(path, repositoryRoot)]));
            AutomatedFiles.Write(manifestPath, new AutomatedRun("2.0.0", RunnerVersion, identity, "in_progress", inputs, [.. stages]));
            return ReadRows<T>(path);
        }

        static IEnumerable<T> ReadRows<T>(string path)
        {
            foreach (var line in File.ReadLines(path))
            {
                yield return JsonSerializer.Deserialize<T>(line, AutomatedProtocol.JsonOptions)
                    ?? throw new InvalidDataException("Invalid checkpoint record.");
            }
        }
    }

    private (AutomatedQuery[] Queries, SortedDictionary<string, double[]> Mapping) LoadQueries(AutomatedProtocol protocol)
    {
        if (protocol.Queries == null || protocol.Mapping == null)
        {
            throw new InvalidDataException("Pinned queries with explicit loci and a single frozen mapping are required.");
        }

        if (protocol.Phase == "confirmatory" && protocol.MappingApproval == null)
        {
            throw new InvalidDataException("Confirmatory mappings require a pinned approved preregistration decision.");
        }

        var queryPath = files.Pin(protocol.Queries, configuration: false);
        var queries = File.ReadLines(queryPath).Select(line =>
            JsonSerializer.Deserialize<AutomatedQuery>(line, AutomatedProtocol.JsonOptions)
                ?? throw new InvalidDataException("Empty query.")).ToArray();
        var mapping = JsonSerializer.Deserialize<SortedDictionary<string, double[]>>(
            File.ReadAllText(files.Pin(protocol.Mapping)), AutomatedProtocol.JsonOptions)
            ?? throw new InvalidDataException("Empty mapping.");
        if (protocol.MappingApproval != null)
        {
            ValidateMappingApproval(protocol.MappingApproval, protocol.Mapping);
        }

        if (mapping.Count == 0 || mapping.Any(item => string.IsNullOrWhiteSpace(item.Key) ||
            item.Value == null || item.Value.Length != features.NumDimensions || item.Value.Any(value =>
                !double.IsFinite(value) || (value != features.MinusValue && value != features.UnsetValue && value != features.PlusValue))) ||
            queries.Length < 2 || queries.Select(query => query.QueryId).Distinct(StringComparer.Ordinal).Count() != queries.Length ||
            queries.Select(query => JsonSerializer.Serialize(query.Symbols)).Distinct(StringComparer.Ordinal).Count() != queries.Length ||
            !protocol.EvaluationLoci.All(locus => queries.Any(query => query.Locus == locus)) ||
            queries.Any(query => string.IsNullOrWhiteSpace(query.QueryId) || !protocol.EvaluationLoci.Contains(query.Locus, StringComparer.Ordinal) ||
                protocol.HoldoutLoci.Contains(query.Locus, StringComparer.Ordinal) || query.TokenFrequency <= 0 ||
                query.Symbols.Length == 0 || query.Symbols.Any(symbol => !mapping.ContainsKey(symbol))))
        {
            throw new InvalidDataException("Invalid mapping, query inventory/frequencies, or evaluation/holdout leakage.");
        }

        return ([.. queries.OrderBy(query => query.QueryId, StringComparer.Ordinal)], mapping);
    }

    private void ValidateMappingApproval(PinnedInput approval, PinnedInput mapping)
    {
        using var document = files.Read(approval);
        var root = document.RootElement;
        if (root.GetProperty("status").GetString() != "approved" ||
            !root.GetProperty("ready_for_confirmatory").GetBoolean() ||
            root.GetProperty("blockers").GetArrayLength() != 0)
        {
            throw new InvalidDataException("Pinned mapping preregistration is not approved.");
        }

        var proposalId = root.GetProperty("proposal_candidate_id").GetString();
        var decision = root.GetProperty("decision");
        var candidate = root.GetProperty("candidates").EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == proposalId);
        if (decision.GetProperty("status").GetString() != "approved" ||
            decision.GetProperty("candidate_id").GetString() != proposalId ||
            decision.GetProperty("mapping_sha256").GetString() != mapping.Sha256 ||
            candidate.GetProperty("materialized_mapping").GetString() != mapping.Path ||
            candidate.GetProperty("mapping_sha256").GetString() != mapping.Sha256)
        {
            throw new InvalidDataException("Pinned mapping does not match the approved proposal and checksum.");
        }
    }

    private static IEnumerable<AutomatedMembership> Sample(AutomatedAdmissionResult admission)
    {
        var protocol = admission.Protocol;
        for (var repetition = 1; repetition <= protocol.Repetitions; repetition++)
        {
            var sampleId = string.Create(CultureInfo.InvariantCulture, $"{protocol.ConfigId}.rep-{repetition:D4}");
            foreach (var source in protocol.Sources.Where(source => admission.Units.ContainsKey(source.Language))
                .OrderBy(source => source.Language, StringComparer.Ordinal))
            {
                foreach (var band in protocol.LengthQuotas)
                {
                    foreach (var unit in admission.Units[source.Language].Where(unit => unit.LengthBand == band.Key)
                        .OrderBy(unit => BalancedSampler.StableKey(protocol.SamplingSeed, sampleId, unit.Id), StringComparer.Ordinal)
                        .ThenBy(unit => unit.Id, StringComparer.Ordinal).Take(band.Value))
                    {
                        yield return new("2.0.0", sampleId, repetition, source.Family, unit);
                    }
                }
            }
        }
    }

    private static IEnumerable<AutomatedScore> Match(
        AutomatedProtocol protocol, AutomatedMembership[] memberships, AutomatedQuery[] queries,
        SortedDictionary<string, double[]> mapping)
    {
        var legacyQueries = queries.Select(query =>
            new SamplingQuery(query.QueryId, string.Empty, query.Symbols, query.TokenFrequency)).ToArray();
        var queryById = queries.ToDictionary(query => query.QueryId, StringComparer.Ordinal);
        foreach (var sample in memberships.GroupBy(row => row.SampleId).OrderBy(group => group.Key, StringComparer.Ordinal))
        {
            var repetition = sample.First().Repetition;
            var languages = sample.GroupBy(row => row.Unit.Language).OrderBy(group => group.Key, StringComparer.Ordinal).ToArray();
            foreach (var language in languages)
            {
                foreach (var query in queries)
                {
                    yield return Score(language, query.QueryId, query.TokenFrequency, "observed", 0,
                        [.. query.Symbols.Select(symbol => mapping[symbol])]);
                }
            }

            for (var nullRepetition = 1; nullRepetition <= protocol.NullRepetitions; nullRepetition++)
            {
                var candidates = sample.Select(row =>
                {
                    var key = BalancedSampler.StableKey(protocol.NullSeed, row.SampleId,
                        nullRepetition.ToString(CultureInfo.InvariantCulture), row.Unit.Id);
                    var draw = (Convert.ToUInt64(key[..13], 16) + 0.5) / 4503599627370496.0;
                    var cumulative = 0.0;
                    var selected = row.Unit.Readings.Last();
                    foreach (var reading in row.Unit.Readings)
                    {
                        cumulative += reading.Weight;
                        if (draw < cumulative)
                        {
                            selected = reading;
                            break;
                        }
                    }

                    return new SamplingCandidate(row.Unit.Id, row.Unit.Language, row.Unit.Lemma,
                        selected.Ipa, selected.Phones, null, "lemma", row.Unit.Memberships);
                }).ToArray();
                var nulls = SequenceNullGenerator.GenerateForSample(protocol.ConfigId, sample.Key, 100,
                    candidates, legacyQueries, mapping, repetition, protocol.NullSeed,
                    RunnerVersion + "-weighted-reading-draw", nullRepetition);
                foreach (var row in nulls.Where(row => row.AnalysisMode == "type-primary"))
                {
                    foreach (var language in languages.Where(language => row.Language == "all" || row.Language == language.Key))
                    {
                        yield return Score(language, row.QueryId,
                            queryById[row.QueryId].TokenFrequency,
                            row.NullKind, nullRepetition, row.Phones);
                    }
                }
            }

            AutomatedScore Score(
                IGrouping<string, AutomatedMembership> language, string queryId, int frequency,
                string nullKind, int nullRepetition, IReadOnlyList<double[]> phones) =>
                new("2.0.0", sample.Key, repetition, queryId, frequency, language.Key, language.First().Family,
                    nullKind, nullRepetition, language.Min(row => AutomatedRetrieval.Distance(phones, row.Unit)));
        }
    }

    private static IEnumerable<AutomatedCalibration> Calibrate(AutomatedProtocol protocol, IEnumerable<AutomatedScore> scores)
    {
        var cells = new Dictionary<(string Query, string Language), CalibrationCell>();
        string? sampleId = null;
        foreach (var row in scores)
        {
            if (sampleId != null && sampleId != row.SampleId)
            {
                foreach (var result in Flush()) { yield return result; }
                cells.Clear();
            }

            sampleId = row.SampleId;
            if (row.NullKind != "observed" && row.NullKind != protocol.CalibrationNull)
            {
                continue;
            }

            var key = (row.QueryId, row.Language);
            if (!cells.TryGetValue(key, out var cell))
            {
                cell = new();
                cells.Add(key, cell);
            }

            if (row.NullKind == "observed")
            {
                if (cell.Observed != null)
                {
                    throw new InvalidDataException("Duplicate observed calibration cell.");
                }

                cell.Observed = row;
            }
            else
            {
                cell.NullDistances.Add(row.Distance);
            }
        }

        foreach (var result in Flush()) { yield return result; }

        IEnumerable<AutomatedCalibration> Flush()
        {
            foreach (var cell in cells.OrderBy(pair => pair.Key.Query, StringComparer.Ordinal)
                .ThenBy(pair => pair.Key.Language, StringComparer.Ordinal).Select(pair => pair.Value))
            {
                var row = cell.Observed ?? throw new InvalidDataException("Missing observed calibration cell.");
                if (cell.NullDistances.Count != protocol.NullRepetitions)
                {
                    throw new InvalidDataException("Missing or duplicated calibration null repetitions.");
                }

                yield return new("2.0.0", row.SampleId, row.Repetition, row.QueryId, row.TokenFrequency,
                    row.Language, row.Family, row.Distance, StatisticalMethods.Calibrate(row.Distance, cell.NullDistances));
            }
        }
    }

    private static AutomatedTest[] Analyze(AutomatedProtocol protocol, AutomatedCalibration[] scores)
    {
        var calibrated = scores.Select(row => new
        {
            Row = row,
            Value = row.Calibration.EmpiricalPercentile
                ?? throw new InvalidDataException("Missing calibration nulls."),
        }).ToArray();
        var tests = new List<AutomatedTest>();
        foreach (var contrast in protocol.Contrasts)
        {
            var targets = AutomatedProtocol.Languages(contrast.TargetGroups, protocol.Groups);
            var controls = AutomatedProtocol.Languages(contrast.ControlGroups, protocol.Groups);
            foreach (var mode in new[] { "type-primary", "token-weighted" })
            {
                var id = contrast.Id + ":" + mode;
                var samplePairs = new SortedDictionary<string, IReadOnlyList<PairedValue>>(StringComparer.Ordinal);
                foreach (var sample in calibrated.GroupBy(value => value.Row.SampleId))
                {
                    var pairs = new List<PairedValue>();
                    foreach (var query in sample.GroupBy(value => value.Row.QueryId))
                    {
                        var target = query.Where(value => targets.Contains(value.Row.Language)).ToArray();
                        var control = query.Where(value => controls.Contains(value.Row.Language)).ToArray();
                        if (target.Length != 0 && control.Length != 0)
                        {
                            pairs.Add(new(query.Key, StatisticalMethods.Median(target.Select(value => value.Value)),
                                StatisticalMethods.Median(control.Select(value => value.Value)),
                                mode == "type-primary" ? 1 : query.First().Row.TokenFrequency));
                        }
                    }

                    samplePairs.Add(sample.Key, pairs);
                }

                var collapsed = samplePairs.Values.SelectMany(value => value).GroupBy(pair => pair.QueryId)
                    .Select(group => new PairedValue(group.Key, StatisticalMethods.Median(group.Select(pair => pair.Target)),
                        StatisticalMethods.Median(group.Select(pair => pair.Control)), group.First().Weight)).ToArray();
                if (collapsed.Length < 2 || samplePairs.Count < 2)
                {
                    tests.Add(new(id, contrast.Id, mode, contrast.Primary && mode == "type-primary", contrast.Required,
                        "not_estimable", collapsed.Length, null, null, null, null, protocol.Contrasts.Length * 2,
                        "insufficient_admitted_groups_or_query_pairs"));
                    continue;
                }

                var result = StatisticalMethods.PairedPermutation(collapsed, "less", protocol.PermutationCount, protocol.PermutationSeed);
                var interval = StatisticalMethods.HierarchicalBootstrapMedianDifference(samplePairs, protocol.ConfidenceLevel,
                    protocol.BootstrapCount, protocol.PermutationSeed);
                tests.Add(new(id, contrast.Id, mode, contrast.Primary && mode == "type-primary", contrast.Required,
                    "estimated", collapsed.Length, result.Estimate, result.PValue, null, interval,
                    protocol.Contrasts.Length * 2, null));
            }
        }

        var adjusted = StatisticalMethods.HolmAdjust(tests.Where(test => test.PValue.HasValue)
            .Select(test => (test.Id, test.PValue!.Value)), protocol.Contrasts.Length * 2)
            .ToDictionary(value => value.ContrastId, StringComparer.Ordinal);
        return [.. tests.OrderBy(test => test.Id, StringComparer.Ordinal).Select(test =>
            test with { AdjustedPValue = adjusted.TryGetValue(test.Id, out var value) ? value.AdjustedPValueValue : null })];
    }

    private void TrackSoftware()
    {
        foreach (var path in Directory.EnumerateFiles(Path.GetDirectoryName(typeof(AutomatedExperiment).Assembly.Location)!, "*.dll")
            .Order(StringComparer.Ordinal))
        {
            files.TrackSoftware(path);
        }
    }

    private void ValidateAstronomy(AutomatedProtocol protocol)
    {
        var panel = PronunciationPanel.Load(files.Resolve(protocol.Panel.Path), repositoryRoot);
        using var registration = files.Read(protocol.Astronomy.Registration);
        using var policy = files.Read(protocol.Astronomy.Policy);
        var manifestPath = files.Pin(protocol.Astronomy.Manifest);
        var validation = new ManifestValidator(repositoryRoot,
            files.Resolve("resources/lexicons/schemas/source-manifest.schema.json")).Validate([manifestPath]);
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!validation.IsValid || manifest.RootElement.GetProperty("license_status").GetString() != "verified" ||
            manifest.RootElement.GetProperty("status").GetString() != "acquired" ||
            manifest.RootElement.GetProperty("usage_policy").GetString() != "unrestricted" ||
            registration.RootElement.GetProperty("protocol_sha256").GetString() != protocol.Astronomy.Policy.Sha256 ||
            registration.RootElement.GetProperty("source").GetProperty("hash").GetString() != manifest.RootElement.GetProperty("sha256").GetString() ||
            registration.RootElement.GetProperty("source").GetProperty("id").GetString() != manifest.RootElement.GetProperty("source_id").GetString() ||
            registration.RootElement.GetProperty("source").GetProperty("url").GetString() != manifest.RootElement.GetProperty("url").GetString() ||
            registration.RootElement.GetProperty("source").GetProperty("revision").GetString() != manifest.RootElement.GetProperty("revision").GetProperty("value").GetString() ||
            registration.RootElement.GetProperty("source").GetProperty("license").GetString() != manifest.RootElement.GetProperty("license").GetString() ||
            policy.RootElement.GetProperty("manifest").GetString() != protocol.Astronomy.Manifest.Path ||
            policy.RootElement.GetProperty("report_path").GetString() != protocol.Astronomy.Registration.Path ||
            (protocol.Phase == "confirmatory" &&
                !panel.AcceptedPanelHashes.Contains(
                    registration.RootElement.GetProperty("pronunciation_panel_sha256").GetString(),
                    StringComparer.Ordinal)))
        {
            throw new InvalidDataException("Unlicensed or inconsistent frozen astronomical registration.");
        }
    }

    private static bool IsInside(string path, string directory) =>
        path.Equals(directory, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private sealed class CalibrationCell
    {
        public AutomatedScore? Observed { get; set; }
        public List<double> NullDistances { get; } = [];
    }
}
