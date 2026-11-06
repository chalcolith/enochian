using Json.Schema;
using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Enochian.Provenance;

public sealed class PronunciationRecovery(string repositoryRoot)
{
    private const string OutputPrefix = ".enoch/pronunciation-provenance/v1";
    private const string License = "CC-BY-SA-4.0";
    private static readonly JsonSerializerOptions Indented = new(PronunciationAudit.JsonOptions) { WriteIndented = true };
    private static readonly string[] Languages = ["hin", "san", "tur", "hun"];
    private static readonly string[] Bands = ["03-05", "06-09", "10-20"];
    private static readonly string[] ParentKeys =
        ["profile", "profile_sha256", "panel_sha256", "feature_sha256", "encoding_sha256", "source_manifests"];
    private static readonly string[] GeneratorTitles =
    [
        "Template:hi-IPA", "Module:hi-IPA", "Template:sa-IPA", "Module:sa-pronunc",
        "Template:tr-IPA", "Module:tr-IPA", "Template:hu-IPA", "Module:hu-pron",
    ];

    public async Task RunAsync(string planPath, bool acquire, CancellationToken cancellationToken = default)
    {
        var plan = ReadPlan(planPath);
        var planPin = ConfigurationPin(planPath);
        VerifyPin(plan.ParentProtocol, configuration: true);
        var protocol = JsonNode.Parse(File.ReadAllText(Resolve(plan.ParentProtocol.Path)))!.AsObject();
        var inputs = ParentInputs(protocol, plan).ToArray();
        var selection = inputs.ToDictionary(input => input.Language,
            input => Select(ReadRows<PronunciationEvidence>(input.EvidencePath), plan.UnitsPerBand), StringComparer.Ordinal);
        var packagePath = Resolve(OutputPrefix + "/snapshot.json");
        RecoveryPackage package;
        if (File.Exists(packagePath))
        {
            package = Read<RecoveryPackage>(packagePath);
            if (package.Plan != planPin)
            {
                throw new InvalidDataException("Sealed recovery snapshot belongs to another plan.");
            }
        }
        else
        {
            if (!acquire)
            {
                throw new FileNotFoundException("Run pronunciation-provenance-acquire to create the sealed snapshot.", packagePath);
            }

            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "EnochianPronunciationProvenance/1.0 (https://github.com/chalcolith/enochian)");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            var rights = await FetchAsync(client,
                "https://en.wiktionary.org/w/api.php?action=query&meta=siteinfo&siprop=rightsinfo&format=json&formatversion=2",
                ".json", cancellationToken);
            VerifyRights(rights);
            var pages = new List<RecoveryPage>();
            foreach (var title in selection.Values.SelectMany(rows => rows).Select(row => row.Lemma)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            {
                pages.Add(await AcquirePageAsync(client, title, plan.PageCutoff, render: true, cancellationToken));
                Console.WriteLine(FormattableString.Invariant($"Recovered page {pages.Count}: revision {pages[^1].Revision}."));
            }

            var generators = new List<RecoveryPage>();
            foreach (var title in GeneratorTitles)
            {
                generators.Add(await AcquirePageAsync(client, title, plan.PageCutoff, render: false, cancellationToken));
            }

            package = new("1.0.0", planPin, License, rights, [.. pages], [.. generators]);
            WriteSealed(packagePath, package);
        }

        VerifyPackage(package, plan, selection.Values.SelectMany(rows => rows).Select(row => row.Lemma));
        var packagePin = Pin(packagePath);
        var decisions = new List<RecoveryCoverage>();
        foreach (var input in inputs)
        {
            var proofs = BuildProofs(selection[input.Language], package).OrderBy(proof => proof.EntryId, StringComparer.Ordinal).ToArray();
            var proofById = proofs.ToDictionary(proof => proof.EntryId, StringComparer.Ordinal);
            var parentRows = ReadRows<PronunciationEvidence>(input.EvidencePath).ToArray();
            var compatibleIds = ReadRows<PronunciationNormalizedEntry>(input.EntryPath)
                .Select(entry => entry.EntryId).ToHashSet(StringComparer.Ordinal);
            var enriched = parentRows.Select(row => proofById.TryGetValue(row.EntryId, out var proof)
                ? PronunciationOriginProofBuilder.Apply(row, proof) : row).ToArray();
            var stem = OutputPrefix + "/profiles/" + input.Stem;
            var entryPath = Resolve(stem + ".jsonl");
            WriteSealedBytes(entryPath, File.ReadAllBytes(input.EntryPath));
            var evidencePath = Resolve(stem + ".evidence.jsonl");
            WriteRows(evidencePath, enriched);
            var proofsPath = Resolve(stem + ".proofs.jsonl");
            WriteRows(proofsPath, proofs);
            var envelope = JsonNode.Parse(File.ReadAllText(input.ManifestPath))!.AsObject();
            envelope["schema_version"] = "1.1.0";
            envelope["software_sha256"] = PronunciationAudit.Hash(typeof(PronunciationRecovery).Assembly.Location);
            envelope["artifacts"] = JsonSerializer.SerializeToNode(
                new[] { Pin(entryPath), Pin(evidencePath), Pin(proofsPath) }, PronunciationAudit.JsonOptions);
            envelope["provenance"] = JsonSerializer.SerializeToNode(new
            {
                parent = Pin(input.ManifestPath),
                package = packagePin,
                proofs = Pin(proofsPath),
            }, PronunciationAudit.JsonOptions);
            envelope["status"] = "exploratory";
            envelope["blockers"] = new JsonArray("generated_readings_require_independent_validation",
                "retrieval_and_query_mapping_preflight_required");
            var manifestPath = Resolve(stem + ".manifest.json");
            WriteSealed(manifestPath, envelope);
            var source = protocol["sources"]!.AsArray().Single(source => source!["language"]!.GetValue<string>() == input.Language)!;
            source["profile_manifest"] = JsonSerializer.SerializeToNode(ConfigurationPin(manifestPath), PronunciationAudit.JsonOptions);
            decisions.Add(new(input.Language,
                selection[input.Language].Select(row => row.LexicalIdentity).Distinct(StringComparer.Ordinal).Count(),
                proofs.Length, PronunciationPolicyEvaluator.Counts(proofs.Select(proof => proof.Origin)),
                PronunciationPolicyEvaluator.Counts(proofs.Where(proof => proof.Origin == "unknown").Select(proof => proof.Status)),
                Supported(parentRows.Where(row => compatibleIds.Contains(row.EntryId))),
                Supported(enriched.Where(row => compatibleIds.Contains(row.EntryId))), "not_established"));
        }

        protocol["config_id"] = "dictionary-first-provenance-v1";
        protocol["output_directory"] = ".enoch/experiments/provenance-v1";
        protocol["admission_report"] = "reports/pronunciation-provenance/v1/admission.json";
        WriteSealed(Resolve(plan.AmendmentPath), protocol);
        WriteSealed(Resolve(plan.ReportPath), new
        {
            schema_version = "1.0.0",
            plan = planPin,
            snapshot = packagePin,
            license = License,
            pages = package.Pages.Length,
            generator_source_revisions = package.GeneratorSources.Select(page => new { page.Title, page.Revision, page.Api }),
            generator_revision_policy = "Historical root revisions are diagnostics, not the rendered transitive dependency closure.",
            sources = decisions,
        });
        WriteSealedBytes(Path.ChangeExtension(Resolve(plan.ReportPath), ".md"), Encoding.UTF8.GetBytes(
            "# Per-reading pronunciation provenance\n\n" +
            "| Language | Selected units | Literal readings | Generated readings | Unknown readings | Supported units |\n" +
            "| --- | ---: | ---: | ---: | ---: | ---: |\n" +
            string.Join("\n", decisions.Select(row => FormattableString.Invariant(
                $"| {row.Language} | {row.SelectedUnits} | {row.Origins.GetValueOrDefault("dictionary-supplied")} | {row.Origins.GetValueOrDefault("template-generated")} | {row.Origins.GetValueOrDefault("unknown")} | {row.SupportedUnitsAfter} |"))) + "\n\n" +
            "The JSON report records measured, deterministically selected anchor coverage. " +
            "Every selected unit retains all compatible reading alternatives and original memberships.\n\n" +
            "Exact language/etymology/POS-scoped Parsoid transclusions bind individual original IPA strings. " +
            "Generic IPA templates qualify only with matching literal arguments and language codes. " +
            "Other-language, ambiguous, nested, and unresolved template semantics are excluded.\n\n" +
            "Revision HTML can render with later templates. Historical generator root revisions do not establish " +
            "the rendered dependency closure; generated readings have no admitted generator revision. " +
            "Wiktionary is not an independent reference for its own generators.\n\n" +
            "No human labeling, manuscript scoring, threshold changes, or holdout access occurred. " +
            "Run the separate amendment's admission/preflight for the actual remaining M4-01 gates.\n\n" +
            "Source: English Wiktionary contributors, CC-BY-SA-4.0. The sealed snapshot contains exact revision URLs, " +
            "response checksums, and verified site license metadata. Raw revisions are retained locally, not " +
            "published as definitions-free proof exports.\n"));
    }

    public static PronunciationEvidence[] Select(IEnumerable<PronunciationEvidence> rows, int unitsPerBand)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(unitsPerBand);
        var units = rows.Where(row => row.Exclusions.Length == 0 && row.UnknownSegments.Length == 0)
            .GroupBy(row => row.Source + "\u001f" + row.LexicalIdentity, StringComparer.Ordinal)
            .Select(unit => (unit.Key, Readings: unit.OrderBy(row => row.EntryId, StringComparer.Ordinal).ToArray())).ToArray();
        return [.. Bands.SelectMany(band => units
            .Where(unit => unit.Readings[0].LengthBand == band)
            .OrderBy(unit => PronunciationPolicyEvaluator.StableId(unit.Key), StringComparer.Ordinal)
            .ThenBy(unit => unit.Key, StringComparer.Ordinal).Take(unitsPerBand).SelectMany(unit => unit.Readings))
            .OrderBy(row => row.EntryId, StringComparer.Ordinal)];
    }

    public RecoveryPin[] VerifyProfile(JsonElement envelope)
    {
        var provenance = envelope.GetProperty("provenance");
        var parentPin = Deserialize<RecoveryPin>(provenance.GetProperty("parent"));
        var packagePin = Deserialize<RecoveryPin>(provenance.GetProperty("package"));
        var proofsPin = Deserialize<RecoveryPin>(provenance.GetProperty("proofs"));
        VerifyPin(parentPin);
        VerifyPin(packagePin);
        VerifyPin(proofsPin);
        using var parent = JsonDocument.Parse(File.ReadAllText(Resolve(parentPin.Path)));
        foreach (var key in ParentKeys)
        {
            if (JsonSerializer.Serialize(parent.RootElement.GetProperty(key)) != JsonSerializer.Serialize(envelope.GetProperty(key)))
            {
                throw new InvalidDataException("Recovery envelope changed its frozen profile or upstream pins.");
            }
        }

        var package = Read<RecoveryPackage>(Resolve(packagePin.Path));
        VerifyPin(package.Plan, configuration: true);
        var plan = ReadPlan(Resolve(package.Plan.Path));
        VerifyPin(plan.ParentProtocol, configuration: true);
        var baseline = JsonNode.Parse(File.ReadAllText(Resolve(plan.ParentProtocol.Path)))!.AsObject();
        var inputs = ParentInputs(baseline, plan).ToArray();
        var input = inputs.Single(input => input.Language == envelope.GetProperty("profile").GetProperty("language").GetString());
        if (Pin(input.ManifestPath) != parentPin)
        {
            throw new InvalidDataException("Recovery parent is not a pinned required-language profile.");
        }

        var selections = inputs.SelectMany(input => Select(ReadRows<PronunciationEvidence>(input.EvidencePath), plan.UnitsPerBand)).ToArray();
        VerifyPackage(package, plan, selections.Select(row => row.Lemma));
        var selected = selections.Where(row => row.Language == input.Language).ToArray();
        var expected = BuildProofs(selected, package).OrderBy(proof => proof.EntryId, StringComparer.Ordinal).ToArray();
        var supplied = ReadRows<PronunciationOriginProof>(Resolve(proofsPin.Path)).ToArray();
        if (JsonSerializer.Serialize(expected, PronunciationAudit.JsonOptions) !=
            JsonSerializer.Serialize(supplied, PronunciationAudit.JsonOptions))
        {
            throw new InvalidDataException("Pronunciation proof does not reproduce from its exact pinned source.");
        }

        var artifacts = envelope.GetProperty("artifacts").EnumerateArray().Select(Deserialize<RecoveryPin>).ToArray();
        foreach (var pin in artifacts)
        {
            VerifyPin(pin);
        }

        if (!artifacts.Contains(proofsPin) ||
            artifacts.Single(pin => Path.GetFileName(pin.Path) == input.Stem + ".jsonl").Sha256 != PronunciationAudit.Hash(input.EntryPath))
        {
            throw new InvalidDataException("Recovered entries or proof artifact membership changed.");
        }

        var expectedById = expected.ToDictionary(proof => proof.EntryId, StringComparer.Ordinal);
        var expectedEvidence = ReadRows<PronunciationEvidence>(input.EvidencePath).Select(row =>
            expectedById.TryGetValue(row.EntryId, out var proof) ? PronunciationOriginProofBuilder.Apply(row, proof) : row).ToArray();
        var evidencePin = artifacts.Single(pin => Path.GetFileName(pin.Path) == input.Stem + ".evidence.jsonl");
        var actualEvidence = ReadRows<PronunciationEvidence>(Resolve(evidencePin.Path)).ToArray();
        if (JsonSerializer.Serialize(expectedEvidence, PronunciationAudit.JsonOptions) !=
            JsonSerializer.Serialize(actualEvidence, PronunciationAudit.JsonOptions))
        {
            throw new InvalidDataException("Recovered evidence changed readings, alternatives, weights, or unsupported origins.");
        }

        return [.. new[] { parentPin, packagePin, proofsPin, package.Plan, package.RightsInfo, plan.ParentProtocol }
            .Concat(package.Pages.Concat(package.GeneratorSources).SelectMany(page =>
                page.Html is { } html ? new[] { page.Api, html } : [page.Api]))
            .Concat(inputs.Select(input => Pin(input.ManifestPath)))
            .Concat(inputs.SelectMany(input => input.Artifacts))
            .DistinctBy(pin => pin.Path, StringComparer.Ordinal)];
    }

    private IEnumerable<ParentInput> ParentInputs(JsonObject protocol, RecoveryPlan plan)
    {
        foreach (var language in plan.Languages)
        {
            var source = protocol["sources"]!.AsArray().Single(source => source!["language"]!.GetValue<string>() == language)!;
            var pin = source["profile_manifest"]!.Deserialize<RecoveryPin>(PronunciationAudit.JsonOptions)!;
            VerifyPin(pin, configuration: true);
            var manifestPath = Resolve(pin.Path);
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var manifest = document.RootElement;
            var stem = manifest.GetProperty("profile").GetProperty("id").GetString() + "-v" +
                manifest.GetProperty("profile").GetProperty("version").GetString();
            var artifacts = manifest.GetProperty("artifacts").EnumerateArray().Select(Deserialize<RecoveryPin>).ToArray();
            foreach (var artifact in artifacts)
            {
                VerifyPin(artifact);
            }

            var entryPath = Resolve(artifacts.Single(artifact => Path.GetFileName(artifact.Path) == stem + ".jsonl").Path);
            if (ReadRows<PronunciationNormalizedEntry>(entryPath).Any(entry => entry.Definition != null))
            {
                throw new InvalidDataException("Pronunciation recovery inputs must be definitions-free.");
            }

            yield return new(language, stem, manifestPath, entryPath,
                Resolve(artifacts.Single(artifact => Path.GetFileName(artifact.Path) == stem + ".evidence.jsonl").Path), artifacts);
        }
    }

    private PronunciationOriginProof[] BuildProofs(IEnumerable<PronunciationEvidence> rows, RecoveryPackage package)
    {
        var pages = package.Pages.ToDictionary(page => page.Title, StringComparer.Ordinal);
        var html = new Dictionary<string, string?>(StringComparer.Ordinal);
        var proofs = new List<PronunciationOriginProof>();
        foreach (var row in rows)
        {
            var page = pages[row.Lemma];
            if (!html.TryGetValue(row.Lemma, out var content))
            {
                content = page.Html == null ? null : File.ReadAllText(Resolve(page.Html.Path), new UTF8Encoding(false, true));
                html.Add(row.Lemma, content);
            }

            proofs.Add(PronunciationOriginProofBuilder.Bind(row, page, content));
        }

        return [.. proofs];
    }

    private void VerifyPackage(RecoveryPackage package, RecoveryPlan plan, IEnumerable<string> selectedTitles)
    {
        if (package.SchemaVersion != "1.0.0" || package.License != License ||
            package.Pages.Select(page => page.Title).Distinct(StringComparer.Ordinal).Count() != package.Pages.Length ||
            !package.Pages.Select(page => page.Title).ToHashSet(StringComparer.Ordinal).SetEquals(selectedTitles) ||
            !package.GeneratorSources.Select(page => page.Title).ToHashSet(StringComparer.Ordinal).SetEquals(GeneratorTitles))
        {
            throw new InvalidDataException("Incomplete, duplicated, incompatible, or unlicensed recovery snapshot.");
        }

        VerifyRights(package.RightsInfo);
        foreach (var page in package.Pages.Concat(package.GeneratorSources))
        {
            VerifyPin(page.Api);
            if (page.Html != null)
            {
                VerifyPin(page.Html);
            }

            using var response = JsonDocument.Parse(File.ReadAllText(Resolve(page.Api.Path)));
            var pages = response.RootElement.GetProperty("query").GetProperty("pages");
            if (pages.GetArrayLength() != 1 || pages[0].GetProperty("title").GetString() != page.Title)
            {
                throw new InvalidDataException("Revision API response has the wrong page.");
            }

            if (page.Revision == 0)
            {
                if (page.Html != null || page.Timestamp != null ||
                    (!pages[0].TryGetProperty("missing", out _) && pages[0].TryGetProperty("revisions", out var existing) && existing.GetArrayLength() != 0))
                {
                    throw new InvalidDataException("Incorrect missing-page provenance.");
                }
            }
            else
            {
                var revision = pages[0].GetProperty("revisions")[0];
                if (revision.GetProperty("revid").GetInt64() != page.Revision ||
                    revision.GetProperty("timestamp").GetString() != page.Timestamp ||
                    DateTimeOffset.Parse(page.Timestamp!, CultureInfo.InvariantCulture) >
                        DateTimeOffset.Parse(plan.PageCutoff, CultureInfo.InvariantCulture) ||
                    (package.Pages.Contains(page) && page.Html == null))
                {
                    throw new InvalidDataException("Invalid revision pin, cutoff, or absent pronunciation HTML.");
                }

                _ = revision.GetProperty("slots").GetProperty("main").GetProperty("content").GetString();
            }
        }
    }

    private async Task<RecoveryPage> AcquirePageAsync(
        HttpClient client, string title, string cutoff, bool render, CancellationToken cancellationToken)
    {
        var cachePath = Resolve(OutputPrefix + "/cache/" +
            PronunciationPolicyEvaluator.StableId(title + "\u001f" + cutoff) + ".json");
        RecoveryPage page;
        if (File.Exists(cachePath))
        {
            page = Read<RecoveryPage>(cachePath);
            VerifyPin(page.Api);
            if (page.Title != title)
            {
                throw new InvalidDataException("Wrong title in acquisition cache.");
            }
        }
        else
        {
            var uri = "https://en.wiktionary.org/w/api.php?action=query&prop=revisions&rvprop=ids%7Ctimestamp%7Ccontent" +
                "&rvslots=main&rvlimit=1&rvdir=older&format=json&formatversion=2&rvstart=" +
                Uri.EscapeDataString(cutoff) + "&titles=" + Uri.EscapeDataString(title);
            var api = await FetchAsync(client, uri, ".json", cancellationToken);
            using var response = JsonDocument.Parse(File.ReadAllText(Resolve(api.Path)));
            if (response.RootElement.TryGetProperty("error", out var error))
            {
                throw new InvalidDataException("MediaWiki API error: " + error.GetProperty("code").GetString());
            }

            var result = response.RootElement.GetProperty("query").GetProperty("pages")[0];
            if (result.GetProperty("title").GetString() != title)
            {
                throw new InvalidDataException("Unexpected MediaWiki title normalization or redirect.");
            }

            var revision = result.TryGetProperty("revisions", out var revisions) && revisions.GetArrayLength() > 0
                ? revisions[0] : default;
            page = new(title, revision.ValueKind == JsonValueKind.Object ? revision.GetProperty("revid").GetInt64() : 0,
                revision.ValueKind == JsonValueKind.Object ? revision.GetProperty("timestamp").GetString() : null, api, null);
            WriteAtomic(cachePath, Serialize(page));
        }

        if (render && page.Revision != 0 && page.Html == null)
        {
            var html = await FetchAsync(client,
                "https://en.wiktionary.org/api/rest_v1/page/html/" + Uri.EscapeDataString(title) + "/" +
                    page.Revision.ToString(CultureInfo.InvariantCulture), ".html", cancellationToken);
            page = page with { Html = html };
            WriteAtomic(cachePath, Serialize(page));
        }

        if (page.Html != null)
        {
            VerifyPin(page.Html);
        }

        return page;
    }

    private async Task<RecoveryPin> FetchAsync(HttpClient client, string uri, string extension, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        var bytes = await client.GetByteArrayAsync(uri, cancellationToken);
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
        var path = Resolve(OutputPrefix + "/raw/" + hash + extension);
        WriteSealedBytes(path, bytes);
        return Pin(path);
    }

    private void VerifyRights(RecoveryPin pin)
    {
        VerifyPin(pin);
        using var document = JsonDocument.Parse(File.ReadAllText(Resolve(pin.Path)));
        var rights = document.RootElement.GetProperty("query").GetProperty("rightsinfo");
        if (rights.GetProperty("url").GetString() != "https://creativecommons.org/licenses/by-sa/4.0/deed.en" ||
            rights.GetProperty("text").GetString() != "Creative Commons Attribution-Share Alike 4.0")
        {
            throw new InvalidDataException("Wiktionary source license could not be verified.");
        }
    }

    private RecoveryPlan ReadPlan(string path)
    {
        var plan = Read<RecoveryPlan>(path);
        if (plan.SchemaVersion != "1.0.0" || plan.UnitsPerBand != 50 ||
            !plan.Languages.SequenceEqual(Languages) ||
            plan.PageCutoff != "2026-10-01T00:00:00Z" || plan.OutputDirectory != OutputPrefix ||
            plan.ReportPath != "reports/pronunciation-provenance/v1/coverage.json" ||
            plan.AmendmentPath != "experiments/confirmatory.provenance.v1.json")
        {
            throw new InvalidDataException("Unsupported recovery plan; selection and output policy must be explicitly versioned.");
        }

        return plan;
    }

    private string Resolve(string path)
    {
        if (Path.IsPathRooted(path))
        {
            throw new InvalidDataException("Recovery pins must be repository-relative.");
        }

        var resolved = Path.GetFullPath(Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)));
        if (!resolved.StartsWith(Path.GetFullPath(repositoryRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Recovery path escapes the repository.");
        }

        return resolved;
    }

    private RecoveryPin Pin(string path) => new(
        Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/'), PronunciationAudit.Hash(path));

    private RecoveryPin ConfigurationPin(string path) => new(
        Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/'), PronunciationAudit.HashConfiguration(path));

    private void VerifyPin(RecoveryPin pin, bool configuration = false)
    {
        var path = Resolve(pin.Path);
        var hash = configuration ? PronunciationAudit.HashConfiguration(path) : PronunciationAudit.Hash(path);
        if (hash != pin.Sha256)
        {
            throw new InvalidDataException("Recovery artifact hash mismatch: " + pin.Path);
        }
    }

    private static int Supported(IEnumerable<PronunciationEvidence> rows) => rows
        .GroupBy(row => row.Source + "\u001f" + row.LexicalIdentity, StringComparer.Ordinal)
        .Count(group => group.All(row => row.Origin == "dictionary-supplied"));

    private T Read<T>(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path, new UTF8Encoding(false, true)));
        var schema = JsonSchema.FromText(File.ReadAllText(Resolve(
            "resources/lexicons/schemas/pronunciation-recovery.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        if (!schema.Evaluate(document.RootElement).IsValid)
        {
            throw new InvalidDataException("Invalid recovery artifact: " + path);
        }

        return Deserialize<T>(document.RootElement);
    }

    private static T Deserialize<T>(JsonElement value) =>
        JsonSerializer.Deserialize<T>(value, PronunciationAudit.JsonOptions)
        ?? throw new InvalidDataException("Empty recovery artifact.");

    private static IEnumerable<T> ReadRows<T>(string path)
    {
        foreach (var line in File.ReadLines(path, new UTF8Encoding(false, true)))
        {
            yield return JsonSerializer.Deserialize<T>(line, PronunciationAudit.JsonOptions)
                ?? throw new InvalidDataException("Empty recovery row.");
        }
    }

    private static byte[] Serialize<T>(T value) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Indented).ReplaceLineEndings("\n") + "\n");

    private static void WriteRows<T>(string path, IEnumerable<T> rows) => WriteSealedBytes(path,
        Encoding.UTF8.GetBytes(string.Concat(rows.Select(row => JsonSerializer.Serialize(row, PronunciationAudit.JsonOptions) + "\n"))));

    private static void WriteSealed<T>(string path, T value) => WriteSealedBytes(path, Serialize(value));

    private static void WriteSealedBytes(string path, byte[] bytes)
    {
        if (File.Exists(path))
        {
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                throw new InvalidDataException("Refusing to overwrite a different sealed recovery artifact: " + path);
            }

            return;
        }

        WriteAtomic(path, bytes);
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private sealed record ParentInput(
        string Language, string Stem, string ManifestPath, string EntryPath, string EvidencePath, RecoveryPin[] Artifacts);

    private sealed record RecoveryCoverage(
        string Language, int SelectedUnits, int SelectedReadings,
        IReadOnlyDictionary<string, int> Origins, IReadOnlyDictionary<string, int> Exclusions,
        int SupportedUnitsBefore, int SupportedUnitsAfter, string GeneratedValidation);
}
