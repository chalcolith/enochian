using HtmlAgilityPack;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Enochian.Provenance;

public sealed partial class GeneratorReplay(string repositoryRoot)
{
    public const string RuntimeImage =
        "mediawiki@sha256:9118f6e0ef4192b87d4bc602818a2812b2f471b5596928f0d1fd8256cde98be1";
    private const string OutputPrefix = ".enoch/pronunciation-replay/v1";
    private static readonly string[] Languages = ["hin", "san", "tur", "hun"];
    private static readonly string[] AdapterPaths = ["tools/wiktionary/replay.php", "tools/wiktionary/settings.php"];
    private readonly Dictionary<string, RecoveryPin[]> verifiedPackages = new(StringComparer.Ordinal);
    private static readonly JsonSerializerOptions Options = new(PronunciationAudit.JsonOptions)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public async Task RunAsync(string planPath, bool acquire, CancellationToken cancellationToken = default)
    {
        var plan = ReadPlan(planPath);
        var planPin = Pin(planPath);
        RecoveryPin[] adapterPins = [.. AdapterPaths.Select(path => Pin(Resolve(path)))];
        var inputs = LoadInputs(plan);
        var requests = CreateRequests(inputs);
        var packagePath = Resolve(OutputPrefix + "/snapshot.json");
        GeneratorReplayPackage package;
        if (File.Exists(packagePath))
        {
            package = Read<GeneratorReplayPackage>(packagePath);
        }
        else
        {
            if (!acquire)
            {
                throw new FileNotFoundException("Run pronunciation-replay-acquire to seal the execution closure.", packagePath);
            }

            var sources = LoadInitialSources(inputs);
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd(
                "EnochianGeneratorReplay/1.0 (https://github.com/chalcolith/enochian)");
            await PrefetchModulesAsync(client, sources, plan.Cutoff,
                inputs.SelectMany(input => input.Package.GeneratorSources).Select(page => page.Title), cancellationToken);
            GeneratorReplayExecution execution;
            for (var round = 0; ; round++)
            {
                if (round >= 100 || sources.Count > 10000)
                {
                    throw new InvalidDataException("Dynamic dependency acquisition exceeded its explicit safety bound; snapshot not sealed.");
                }

                execution = await ExecuteAsync(plan, sources.Values, requests, cancellationToken);
                var missing = execution.Results.SelectMany(result => result.Requested)
                    .Distinct(StringComparer.Ordinal).Where(title => !sources.ContainsKey(title))
                    .Order(StringComparer.Ordinal).ToArray();
                if (missing.Length == 0)
                {
                    break;
                }

                Console.WriteLine(FormattableString.Invariant(
                    $"Replay round {round + 1}: acquiring {missing.Length} execution-time dependencies."));
                foreach (var title in missing)
                {
                    sources.Add(title, await AcquireSourceAsync(client, title, plan.Cutoff, cancellationToken));
                }

                await PrefetchModulesAsync(client, sources, plan.Cutoff,
                    execution.Results.SelectMany(result => result.Requested), cancellationToken);
            }

            var repeat = await ExecuteAsync(plan, sources.Values, requests, cancellationToken);
            Require(Same(execution, repeat), "Offline replay was not byte-stable; snapshot not sealed.");
            var requestPath = Resolve(OutputPrefix + "/requests.json");
            var executionPath = Resolve(OutputPrefix + "/execution.json");
            WriteSealed(requestPath, requests);
            WriteSealed(executionPath, execution);
            package = new("2.0.0", planPin, adapterPins,
                [.. sources.Values.OrderBy(page => page.Title, StringComparer.Ordinal)],
                Pin(requestPath), Pin(executionPath));
        }

        var verified = await VerifyPackageAsync(package, planPath, plan, inputs, requests, cancellationToken);
        WriteSealed(packagePath, package);
        var packagePin = Pin(packagePath);
        var proofs = CreateProofs(inputs, requests, verified, package, packagePin.Sha256);
        Publish(plan, inputs, proofs, packagePin);
        Console.WriteLine(FormattableString.Invariant(
            $"Replayed {requests.Length} transclusions: {proofs.Count(proof => proof.Status == "reproduced")} readings reproduced; {proofs.Count(proof => proof.Status != "reproduced")} readings remain blocked. No pronunciation admission granted."));
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
        var package = Read<GeneratorReplayPackage>(Resolve(packagePin.Path));
        VerifyPin(package.Plan);
        var planPath = Resolve(package.Plan.Path);
        var plan = ReadPlan(planPath);
        var inputs = LoadInputs(plan);
        var input = inputs.Single(input => Pin(input.ManifestPath) == parentPin);
        foreach (var key in new[] { "profile", "profile_sha256", "panel_sha256", "feature_sha256", "encoding_sha256", "source_manifests" })
        {
            Require(Same(input.Envelope.GetProperty(key), envelope.GetProperty(key)),
                "Replay envelope changed a frozen profile or upstream pin.");
        }

        var requests = CreateRequests(inputs);
        var execution = Read<GeneratorReplayExecution>(Resolve(package.Execution.Path));
        if (!verifiedPackages.TryGetValue(packagePin.Sha256, out var tracked))
        {
            execution = VerifyPackageAsync(package, planPath, plan, inputs, requests, CancellationToken.None)
                .GetAwaiter().GetResult();
            tracked = [.. new[] { packagePin, package.Plan, package.Requests, package.Execution }
                .Concat(package.Adapter).Concat(package.Sources.Select(page => page.Api))
                .Concat(inputs.SelectMany(item => item.Tracked)).DistinctBy(pin => pin.Path, StringComparer.Ordinal)];
            verifiedPackages.Add(packagePin.Sha256, tracked);
        }

        foreach (var pin in tracked)
        {
            VerifyPin(pin);
        }

        var proofs = CreateProofs(inputs, requests, execution, package, packagePin.Sha256)
            .Where(proof => proof.Language == input.Language).ToArray();
        Require(Same(proofs, ReadRows<GeneratorReplayProof>(Resolve(proofsPin.Path))),
            "Replay proof changed its reading, frame, revision, closure, ownership, or alternatives.");
        VerifyArtifacts(envelope, input, proofs, proofsPin);
        return [.. tracked.Append(proofsPin)];
    }

    public static GeneratorReplayRequest CreateRequest(
        PronunciationOriginProof proof, PronunciationEvidence[] rows, string html)
    {
        Require(proof.Origin == "template-generated" && proof.Template != null && proof.TransclusionId != null,
            "Only an unambiguously owned generated transclusion can be replayed.");
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var candidates = document.DocumentNode.Descendants().Where(node =>
            node.GetAttributeValue("about", "") == proof.TransclusionId && node.Attributes["data-mw"] != null).ToArray();
        Require(candidates.Length == 1, "Ambiguous replay transclusion metadata.");
        using var metadata = JsonDocument.Parse(HtmlEntity.DeEntitize(candidates[0].GetAttributeValue("data-mw", "")));
        var parts = metadata.RootElement.GetProperty("parts");
        Require(parts.GetArrayLength() == 1, "Compound replay transclusion is not supported.");
        var template = parts[0].GetProperty("template");
        var name = template.GetProperty("target").GetProperty("wt").GetString()!.Trim();
        Require(name == proof.Template || name == "Template:" + proof.Template, "Wrong replay generator template.");
        var parameters = template.GetProperty("params").EnumerateObject()
            .ToDictionary(parameter => parameter.Name, parameter => parameter.Value.GetProperty("wt").GetString()!, StringComparer.Ordinal);
        var builder = new StringBuilder("{{").Append(name);
        var positional = 1;
        while (parameters.Remove(positional.ToString(CultureInfo.InvariantCulture), out var value))
        {
            Require(value == value.Trim(), "Unresolved positional whitespace semantics in original frame.");
            _ = builder.Append('|').Append(positional.ToString(CultureInfo.InvariantCulture)).Append('=').Append(value);
            positional++;
        }

        foreach (var (key, value) in parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            Require(!key.Contains('|') && !key.Contains('=') && key == key.Trim(),
                "Unresolved original frame parameter name.");
            _ = builder.Append('|').Append(key).Append('=').Append(value);
        }

        _ = builder.Append("}}");
        var id = PronunciationPolicyEvaluator.StableId(
            rows[0].Lemma + "\u001f" + proof.PageRevision.ToString(CultureInfo.InvariantCulture) + "\u001f" + proof.TransclusionId);
        return new(id, rows[0].Lemma, proof.PageRevision, proof.Template!, proof.TransclusionId!, builder.ToString(),
            [.. rows.Select(row => row.EntryId).Order(StringComparer.Ordinal)]);
    }

    public static GeneratorReplayProof Bind(
        PronunciationEvidence row, GeneratorReplayRequest? request, GeneratorReplayResult? result,
        IReadOnlyDictionary<string, RecoveryPage> sources, string revision, string[] runtimeDependencies,
        string[] originalReadings)
    {
        var parentHash = PronunciationPolicyEvaluator.StableId(JsonSerializer.Serialize(row, PronunciationAudit.JsonOptions));
        if (request != null)
        {
            Require(request.Title == row.Lemma && sources.TryGetValue(request.Title, out var page) &&
                page.Revision == request.PageRevision, "Replay request changed its original page title or revision.");
        }

        var status = request == null || result == null ? "unresolved_origin_or_frame"
            : result.Id != request.Id || !request.EntryIds.Contains(row.EntryId, StringComparer.Ordinal)
                ? throw new InvalidDataException("Replay result is bound to another request or reading.")
            : result.Unsupported.Length != 0 ? "unresolved_runtime_input"
            : result.Requested.Any(title => !sources.ContainsKey(title)) ? "missing_execution_dependency"
            : result.Errors.Length != 0 && result.Requested.Any(title => sources[title].Revision == 0 &&
                (title.StartsWith("Module:", StringComparison.Ordinal) || title.StartsWith("Template:", StringComparison.Ordinal)))
                ? "missing_generator_dependency"
            : result.Errors.Length != 0 ? "generator_execution_error"
            : !result.Readings.Contains(row.OriginalIpa, StringComparer.Ordinal) ? "original_ipa_mismatch"
            : !originalReadings.Order(StringComparer.Ordinal).SequenceEqual(result.Readings.Order(StringComparer.Ordinal))
                ? "alternative_distribution_mismatch"
            : "reproduced";
        var dependencies = result == null ? [] : result.Requested.Where(sources.ContainsKey).Select(title =>
            "enwiktionary:" + title + "@" + sources[title].Revision.ToString(CultureInfo.InvariantCulture))
            .Concat(runtimeDependencies).Concat(row.Dependencies.Where(dependency => dependency != "unknown-generator-dependencies"))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        return new("2.0.0", row.EntryId, parentHash, row.Language, row.LexicalIdentity, row.Convention, row.OriginalIpa,
            request?.Id, status, status == "reproduced" ? "wiktionary:" + request!.Template : null,
            status == "reproduced" ? "mediawiki-replay-v1:" + revision : null, dependencies);
    }

    public static PronunciationEvidence Apply(PronunciationEvidence row, GeneratorReplayProof proof)
    {
        Require(proof.EntryId == row.EntryId && proof.ParentRecordSha256 ==
            PronunciationPolicyEvaluator.StableId(JsonSerializer.Serialize(row, PronunciationAudit.JsonOptions)),
            "Replay proof is not bound to its exact parent reading and weight.");
        if (proof.Status != "reproduced")
        {
            return row;
        }

        Require(!string.IsNullOrWhiteSpace(proof.Generator) && !string.IsNullOrWhiteSpace(proof.GeneratorRevision) &&
            proof.Dependencies.Length != 0 && proof.Dependencies.All(dependency =>
                !dependency.Contains("unknown", StringComparison.OrdinalIgnoreCase)),
            "Reproduced generator is missing its exact identity, revision, or closure.");
        return row with
        {
            Generator = proof.Generator,
            GeneratorRevision = proof.GeneratorRevision,
            Dependencies = proof.Dependencies,
            EvidenceStatus = "exploratory",
            Independence = "unknown",
        };
    }

    public static string[] LiteralDependencies(string code) =>
        [.. LiteralModulePattern().Matches(code).Select(match => match.Groups["title"].Value.Replace('_', ' ').Trim())
            .Where(title => !title.EndsWith('/'))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

    public static string[] OriginalReadings(GeneratorReplayRequest request, string html)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var readings = document.DocumentNode.Descendants().Where(node =>
            node.GetAttributeValue("class", "").Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("IPA") &&
            node.AncestorsAndSelf().FirstOrDefault(ancestor => ancestor.Attributes["about"] != null)?
                .GetAttributeValue("about", "") == request.TransclusionId)
            .Select(node => HtmlEntity.DeEntitize(node.InnerText).Trim(' ', '\t', '\n', '\r', '\0', '\v')).ToArray();
        Require(readings.Length != 0, "Original transclusion has no owned raw IPA readings.");
        return readings;
    }

    private ReplayInput[] LoadInputs(GeneratorReplayPlan plan)
    {
        VerifyPin(plan.ParentProtocol);
        using var protocol = JsonDocument.Parse(File.ReadAllText(Resolve(plan.ParentProtocol.Path)));
        var inputs = new List<ReplayInput>();
        foreach (var pin in plan.Profiles)
        {
            VerifyPin(pin);
            var path = Resolve(pin.Path);
            using var manifest = JsonDocument.Parse(File.ReadAllText(path));
            var envelope = manifest.RootElement.Clone();
            Require(envelope.GetProperty("schema_version").GetString() == "1.1.0", "Replay requires a frozen M2-10 parent.");
            var language = envelope.GetProperty("profile").GetProperty("language").GetString()!;
            var protocolPin = protocol.RootElement.GetProperty("sources").EnumerateArray().Single(source =>
                source.GetProperty("language").GetString() == language).GetProperty("profile_manifest");
            Require(protocolPin.GetProperty("path").GetString() == pin.Path &&
                protocolPin.GetProperty("sha256").GetString() == PronunciationAudit.HashConfiguration(path),
                "Replay parent is not the frozen provenance amendment's language profile.");
            // M2-10 returns canonical configuration pins; replay freezes verified input bytes.
            var tracked = new PronunciationRecovery(repositoryRoot).VerifyProfile(envelope)
                .Select(pin => Pin(Resolve(pin.Path))).ToArray();
            var artifacts = envelope.GetProperty("artifacts").EnumerateArray().Select(Deserialize<RecoveryPin>).ToArray();
            var evidence = artifacts.Single(artifact => artifact.Path.EndsWith(".evidence.jsonl", StringComparison.Ordinal));
            var entries = artifacts.Single(artifact => artifact.Path.EndsWith(".jsonl", StringComparison.Ordinal) &&
                !artifact.Path.EndsWith(".evidence.jsonl", StringComparison.Ordinal) &&
                !artifact.Path.EndsWith(".proofs.jsonl", StringComparison.Ordinal));
            var proof = Deserialize<RecoveryPin>(envelope.GetProperty("provenance").GetProperty("proofs"));
            var sourcePackage = Deserialize<RecoveryPin>(envelope.GetProperty("provenance").GetProperty("package"));
            inputs.Add(new(language, path, envelope, entries, evidence,
                ReadRows<PronunciationEvidence>(Resolve(evidence.Path)),
                ReadRows<PronunciationOriginProof>(Resolve(proof.Path)),
                Read<RecoveryPackage>(Resolve(sourcePackage.Path)),
                [.. tracked.Concat(artifacts).Append(pin).Append(plan.ParentProtocol)]));
        }

        Require(inputs.Select(input => input.Language).Order(StringComparer.Ordinal)
            .SequenceEqual(Languages.Order(StringComparer.Ordinal)), "Replay must cover exactly the four frozen required anchors.");
        return [.. inputs];
    }

    private GeneratorReplayRequest[] CreateRequests(ReplayInput[] inputs)
    {
        var requests = new List<GeneratorReplayRequest>();
        foreach (var input in inputs)
        {
            var rows = input.Rows.ToDictionary(row => row.EntryId, StringComparer.Ordinal);
            var pages = input.Package.Pages.ToDictionary(page => page.Title, StringComparer.Ordinal);
            foreach (var group in input.Proofs.Where(proof => proof.Origin == "template-generated")
                .GroupBy(proof => (proof.PageRevision, proof.TransclusionId)))
            {
                var first = group.First();
                var page = pages[rows[first.EntryId].Lemma];
                Require(page.Revision == first.PageRevision, "Replay proof belongs to another source-page revision.");
                var html = File.ReadAllText(Resolve(page.Html!.Path), new UTF8Encoding(false, true));
                requests.Add(CreateRequest(first, [.. group.Select(proof => rows[proof.EntryId])], html));
            }
        }

        Require(requests.Select(request => request.Id).Distinct(StringComparer.Ordinal).Count() == requests.Count,
            "Duplicate replay request identity.");
        return [.. requests.OrderBy(request => request.Id, StringComparer.Ordinal)];
    }

    private Dictionary<string, RecoveryPage> LoadInitialSources(ReplayInput[] inputs)
    {
        var pages = inputs.SelectMany(input => input.Package.Pages.Concat(input.Package.GeneratorSources))
            .DistinctBy(page => page.Title, StringComparer.Ordinal).ToDictionary(page => page.Title, StringComparer.Ordinal);
        foreach (var page in pages.Values)
        {
            _ = Source(page);
        }

        return pages;
    }

    private async Task<GeneratorReplayExecution> VerifyPackageAsync(
        GeneratorReplayPackage package, string planPath, GeneratorReplayPlan plan,
        ReplayInput[] inputs, GeneratorReplayRequest[] requests, CancellationToken cancellationToken)
    {
        Require(package.SchemaVersion == "2.0.0" && package.Plan == Pin(planPath),
            "Replay snapshot belongs to another version or plan.");
        foreach (var pin in inputs.SelectMany(input => input.Tracked).DistinctBy(pin => pin.Path, StringComparer.Ordinal))
        {
            VerifyPin(pin);
        }

        Require(package.Adapter.SequenceEqual(AdapterPaths.Select(path => Pin(Resolve(path)))),
            "Replay adapter revision changed.");
        VerifyPin(package.Requests);
        VerifyPin(package.Execution);
        Require(Same(requests, Read<GeneratorReplayRequest[]>(Resolve(package.Requests.Path))),
            "Replay requests changed their exact original frames, titles, ownership, or alternatives.");
        var sources = package.Sources.ToDictionary(page => page.Title, StringComparer.Ordinal);
        foreach (var (title, initial) in LoadInitialSources(inputs))
        {
            Require(sources.TryGetValue(title, out var retained) && retained == initial,
                "Pinned lexical page or root generator revision changed.");
        }

        foreach (var source in package.Sources)
        {
            _ = Source(source);
            Require(source.Revision == 0 || string.CompareOrdinal(source.Timestamp, plan.Cutoff) <= 0,
                "Generator dependency is newer than the frozen cutoff.");
        }

        var expected = Read<GeneratorReplayExecution>(Resolve(package.Execution.Path));
        var actual = await ExecuteAsync(plan, package.Sources, requests, cancellationToken);
        Require(Same(expected, actual), "Pinned offline generator execution no longer reproduces byte-for-byte.");
        Require(actual.Results.Select(result => result.Id).SequenceEqual(requests.Select(request => request.Id)) &&
            actual.Results.All(result => result.Requested.All(sources.ContainsKey)),
            "Replay execution has missing requests or unresolved dynamic dependencies.");
        return actual;
    }

    private GeneratorReplayProof[] CreateProofs(
        ReplayInput[] inputs, GeneratorReplayRequest[] requests, GeneratorReplayExecution execution,
        GeneratorReplayPackage package, string revision)
    {
        var byEntry = requests.SelectMany(request => request.EntryIds.Select(id => (id, request)))
            .ToDictionary(item => item.id, item => item.request, StringComparer.Ordinal);
        var results = execution.Results.ToDictionary(result => result.Id, StringComparer.Ordinal);
        var sources = package.Sources.ToDictionary(page => page.Title, StringComparer.Ordinal);
        var runtime = package.Adapter.Select(pin => "adapter:" + pin.Path + "@" + pin.Sha256)
            .Append("runtime:" + RuntimeImage).ToArray();
        var proofs = new List<GeneratorReplayProof>();
        var originals = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            var pages = input.Package.Pages.ToDictionary(page => page.Title, StringComparer.Ordinal);
            foreach (var row in input.Rows.Where(row => byEntry.ContainsKey(row.EntryId)))
            {
                var request = byEntry[row.EntryId];
                var result = results[request.Id];
                if (!originals.TryGetValue(request.Id, out var expected))
                {
                    expected = OriginalReadings(request, File.ReadAllText(Resolve(pages[request.Title].Html!.Path),
                        new UTF8Encoding(false, true)));
                    originals.Add(request.Id, expected);
                }

                proofs.Add(Bind(row, request, result, sources, revision, runtime, expected));
            }
        }

        return [.. proofs.OrderBy(proof => proof.EntryId, StringComparer.Ordinal)];
    }

    private async Task<RecoveryPage> AcquireSourceAsync(
        HttpClient client, string title, string cutoff, CancellationToken cancellationToken)
    {
        var cache = Resolve(OutputPrefix + "/cache/" + PronunciationPolicyEvaluator.StableId(title) + ".json");
        if (File.Exists(cache))
        {
            var cached = Read<RecoveryPage>(cache);
            Require(cached.Title == title, "Wrong dependency in replay acquisition cache.");
            _ = Source(cached);
            return cached;
        }

        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        var uri = "https://en.wiktionary.org/w/api.php?action=query&prop=info%7Crevisions&rvprop=ids%7Ctimestamp%7Ccontent" +
            "&rvslots=main&rvlimit=1&rvdir=older&format=json&formatversion=2&rvstart=" +
            Uri.EscapeDataString(cutoff) + "&titles=" + Uri.EscapeDataString(title);
        var bytes = await client.GetByteArrayAsync(uri, cancellationToken);
        var rawPath = Resolve(OutputPrefix + "/raw/" +
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)) + ".json");
        WriteSealedBytes(rawPath, bytes);
        using var document = JsonDocument.Parse(bytes);
        Require(!document.RootElement.TryGetProperty("error", out _), "MediaWiki dependency acquisition returned an API error.");
        var pages = document.RootElement.GetProperty("query").GetProperty("pages");
        Require(pages.GetArrayLength() == 1 && pages[0].GetProperty("title").GetString() == title,
            "Unexpected dependency title normalization or redirect.");
        var page = pages[0];
        var present = page.TryGetProperty("revisions", out var revisions) && revisions.GetArrayLength() == 1;
        var result = new RecoveryPage(title, present ? revisions[0].GetProperty("revid").GetInt64() : 0,
            present ? revisions[0].GetProperty("timestamp").GetString() : null, Pin(rawPath), null);
        _ = Source(result);
        WriteSealed(cache, result);
        return result;
    }

    [GeneratedRegex("""(?:"(?<title>Module:[^"\\\r\n%<>#{}\[\]|]+)"|'(?<title>Module:[^'\\\r\n%<>#{}\[\]|]+)'|\[\[(?<title>Module:[^\]\\\r\n%<>#{}|]+)\]\])(?!\s*\.\.)""",
        RegexOptions.CultureInvariant, 1000)]
    private static partial Regex LiteralModulePattern();

    private async Task PrefetchModulesAsync(
        HttpClient client, Dictionary<string, RecoveryPage> sources, string cutoff,
        IEnumerable<string> executedTitles, CancellationToken cancellationToken)
    {
        foreach (var title in executedTitles.Where(title => title.StartsWith("Module:", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = Source(sources[title]);
            if (source.Model != "Scribunto")
            {
                continue;
            }

            foreach (var dependency in LiteralDependencies(source.Content))
            {
                if (!sources.ContainsKey(dependency))
                {
                    Require(sources.Count < 10000, "Literal dependency prefetch exceeded the acquisition safety bound.");
                    sources.Add(dependency, await AcquireSourceAsync(client, dependency, cutoff, cancellationToken));
                    Console.WriteLine("Pinned literal module dependency: " + dependency);
                }
            }
        }
    }

    private GeneratorReplaySource Source(RecoveryPage page)
    {
        VerifyPin(page.Api);
        using var response = JsonDocument.Parse(File.ReadAllText(Resolve(page.Api.Path), new UTF8Encoding(false, true)));
        Require(!response.RootElement.TryGetProperty("error", out _), "Pinned dependency contains an API error.");
        var pages = response.RootElement.GetProperty("query").GetProperty("pages");
        Require(pages.GetArrayLength() == 1 && pages[0].GetProperty("title").GetString() == page.Title,
            "Pinned dependency has the wrong title.");
        var source = pages[0];
        var present = source.TryGetProperty("revisions", out var revisions) && revisions.GetArrayLength() == 1;
        Require(present == (page.Revision > 0), "Pinned dependency presence changed.");
        if (!present)
        {
            Require(page.Timestamp == null, "Absent pinned dependency has a timestamp.");
            return new(page.Title, 0, null, 0, "wikitext", "en", "");
        }

        var revision = revisions[0];
        Require(revision.GetProperty("revid").GetInt64() == page.Revision &&
            revision.GetProperty("timestamp").GetString() == page.Timestamp, "Wrong generator dependency revision or timestamp.");
        var slot = revision.GetProperty("slots").GetProperty("main");
        return new(page.Title, page.Revision, page.Timestamp, source.GetProperty("pageid").GetInt64(),
            slot.GetProperty("contentmodel").GetString()!, source.TryGetProperty("pagelanguage", out var language)
                ? language.GetString()! : "en", slot.GetProperty("content").GetString()!);
    }

    private async Task<GeneratorReplayExecution> ExecuteAsync(
        GeneratorReplayPlan plan, IEnumerable<RecoveryPage> sources, GeneratorReplayRequest[] requests,
        CancellationToken cancellationToken)
    {
        var work = Resolve(OutputPrefix + "/work-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(work);
        var name = "enochian-replay-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllBytes(Path.Combine(work, "input.json"), Serialize(new
            {
                sources = sources.OrderBy(page => page.Title, StringComparer.Ordinal).Select(Source).ToArray(),
                requests,
            }));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(20));
            var output = await RunDockerAsync([
                "run", "--rm", "--pull", "never", "--name", name, "--network", "none",
                "--cap-drop", "ALL", "--security-opt", "no-new-privileges", "--pids-limit", "128", "--memory", "1g", "--cpus", "2",
                "--mount", "type=bind,source=" + work + ",target=/inputs,readonly",
                "--mount", "type=bind,source=" + Resolve("tools/wiktionary") + ",target=/adapter,readonly",
                "--entrypoint", "php", plan.Image, "/adapter/replay.php",
            ], timeout.Token);
            return JsonSerializer.Deserialize<GeneratorReplayExecution>(output, Options)
                ?? throw new InvalidDataException("Empty isolated generator execution.");
        }
        finally
        {
            _ = await RunDockerAsync(["rm", "--force", name], CancellationToken.None, allowMissingContainer: true);
            File.Delete(Path.Combine(work, "input.json"));
            Directory.Delete(work);
        }
    }

    private static async Task<string> RunDockerAsync(
        string[] arguments, CancellationToken cancellationToken, bool allowMissingContainer = false)
    {
        var start = new ProcessStartInfo("docker") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start Docker.");
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }

        var output = await stdout;
        var errors = await stderr;
        if (process.ExitCode != 0 && !(allowMissingContainer &&
            errors.Contains("No such container", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(FormattableString.Invariant(
                $"Isolated MediaWiki replay failed (Docker exit {process.ExitCode}): {errors}{output}"));
        }

        if (!allowMissingContainer && !string.IsNullOrWhiteSpace(errors))
        {
            Console.Error.WriteLine(errors.Trim());
        }

        return output;
    }

    private void Publish(
        GeneratorReplayPlan plan, ReplayInput[] inputs, GeneratorReplayProof[] proofs, RecoveryPin packagePin)
    {
        var decisions = new List<object>();
        var protocol = JsonNode.Parse(File.ReadAllText(Resolve(plan.ParentProtocol.Path)))!.AsObject();
        foreach (var input in inputs)
        {
            var stem = Path.GetFileNameWithoutExtension(input.Entries.Path);
            var prefix = OutputPrefix + "/profiles/" + stem;
            var entryPath = Resolve(prefix + ".jsonl");
            var evidencePath = Resolve(prefix + ".evidence.jsonl");
            var proofPath = Resolve(prefix + ".proofs.jsonl");
            var languageProofs = proofs.Where(proof => proof.Language == input.Language).ToArray();
            var byId = languageProofs.ToDictionary(proof => proof.EntryId, StringComparer.Ordinal);
            WriteSealedBytes(entryPath, File.ReadAllBytes(Resolve(input.Entries.Path)));
            WriteRows(evidencePath, input.Rows.Select(row => byId.TryGetValue(row.EntryId, out var proof) ? Apply(row, proof) : row));
            WriteRows(proofPath, languageProofs);
            var envelope = JsonNode.Parse(input.Envelope.GetRawText())!.AsObject();
            envelope["schema_version"] = "1.2.0";
            envelope["software_sha256"] = PronunciationAudit.Hash(typeof(GeneratorReplay).Assembly.Location);
            envelope["artifacts"] = JsonSerializer.SerializeToNode(
                new[] { Pin(entryPath), Pin(evidencePath), Pin(proofPath) }, Options);
            envelope["provenance"] = JsonSerializer.SerializeToNode(new
            {
                parent = Pin(input.ManifestPath),
                package = packagePin,
                proofs = Pin(proofPath),
            }, Options);
            envelope["blockers"] = new JsonArray("independent_reference_agreement_required",
                "unresolved_readings_remain_exploratory", "retrieval_and_query_mapping_preflight_required");
            var manifestPath = Resolve(prefix + ".manifest.json");
            WriteSealed(manifestPath, envelope);
            var source = protocol["sources"]!.AsArray().Single(source => source!["language"]!.GetValue<string>() == input.Language)!;
            source["profile_manifest"] = JsonSerializer.SerializeToNode(new RecoveryPin(
                Pin(manifestPath).Path, PronunciationAudit.HashConfiguration(manifestPath)), Options);
            decisions.Add(new
            {
                input.Language,
                generated_readings = languageProofs.Length,
                reproduced_readings = languageProofs.Count(proof => proof.Status == "reproduced"),
                statuses = PronunciationPolicyEvaluator.Counts(languageProofs.Select(proof => proof.Status)),
                admission = "independent_reference_agreement_still_required",
            });
        }

        protocol["config_id"] = "dictionary-first-generator-replay-v1";
        protocol["output_directory"] = ".enoch/experiments/generator-replay-v1";
        protocol["admission_report"] = "reports/pronunciation-replay/v1/admission.json";
        WriteSealed(Resolve("experiments/confirmatory.replay.v1.json"), protocol);
        WriteSealed(Resolve("reports/pronunciation-replay/v1/coverage.json"), new
        {
            schema_version = "2.0.0",
            snapshot = packagePin,
            sources = decisions,
            license = "CC-BY-SA-4.0",
            attribution = "English Wiktionary contributors; immutable revisions and source bytes retained in the replay snapshot.",
            runtime_license = "MediaWiki and Scribunto: GPL-2.0-or-later; external adapter only, not core-library code.",
            manuscript_scoring = false,
            pronunciation_admission = false,
        });
    }

    private void VerifyArtifacts(
        JsonElement envelope, ReplayInput input, GeneratorReplayProof[] proofs, RecoveryPin proofsPin)
    {
        var artifacts = envelope.GetProperty("artifacts").EnumerateArray().Select(Deserialize<RecoveryPin>).ToArray();
        Require(artifacts.Length == 3 && artifacts.DistinctBy(pin => pin.Path).Count() == 3 &&
            artifacts.Contains(proofsPin), "Replay artifact membership changed.");
        foreach (var artifact in artifacts)
        {
            VerifyPin(artifact);
        }

        var stem = Path.GetFileNameWithoutExtension(input.Entries.Path);
        Require(artifacts.Single(pin => Path.GetFileName(pin.Path) == stem + ".jsonl").Sha256 == input.Entries.Sha256,
            "Replay entries or original source memberships changed.");
        var byId = proofs.ToDictionary(proof => proof.EntryId, StringComparer.Ordinal);
        var expected = input.Rows.Select(row => byId.TryGetValue(row.EntryId, out var proof) ? Apply(row, proof) : row).ToArray();
        var evidence = artifacts.Single(pin => Path.GetFileName(pin.Path) == stem + ".evidence.jsonl");
        Require(Same(expected, ReadRows<PronunciationEvidence>(Resolve(evidence.Path))),
            "Replay evidence changed original alternatives, weights, source membership, or unsupported origins.");
    }

    private static GeneratorReplayPlan ReadPlan(string path)
    {
        var plan = Read<GeneratorReplayPlan>(path);
        Require(plan.SchemaVersion == "2.0.0" && plan.Cutoff == "2026-10-01T00:00:00Z" &&
            plan.Image == RuntimeImage && plan.OutputDirectory == OutputPrefix && plan.Profiles.Length == 4,
            "Unsupported generator replay plan; runtime and selection changes require an explicit new version.");
        return plan;
    }

    private string Resolve(string path)
    {
        Require(!Path.IsPathRooted(path), "Replay pins must be repository-relative.");
        var resolved = Path.GetFullPath(Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)));
        Require(resolved.StartsWith(Path.GetFullPath(repositoryRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase),
            "Replay path escapes the repository.");
        return resolved;
    }

    private RecoveryPin Pin(string path) =>
        new(Path.GetRelativePath(repositoryRoot, path).Replace(Path.DirectorySeparatorChar, '/'), PronunciationAudit.Hash(path));

    private void VerifyPin(RecoveryPin pin) =>
        Require(PronunciationAudit.Hash(Resolve(pin.Path)) == pin.Sha256, "Replay artifact hash mismatch: " + pin.Path);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private static T Deserialize<T>(JsonElement value) =>
        JsonSerializer.Deserialize<T>(value, Options) ?? throw new InvalidDataException("Empty replay artifact.");

    private static T Read<T>(string path) =>
        JsonSerializer.Deserialize<T>(File.ReadAllText(path, new UTF8Encoding(false, true)), Options)
        ?? throw new InvalidDataException("Empty replay artifact: " + path);

    private static T[] ReadRows<T>(string path) =>
        [.. File.ReadLines(path, new UTF8Encoding(false, true)).Select(line =>
            JsonSerializer.Deserialize<T>(line, Options) ?? throw new InvalidDataException("Empty replay row: " + path))];

    private static bool Same<T>(T left, T right) =>
        JsonSerializer.Serialize(left, Options) == JsonSerializer.Serialize(right, Options);

    private static byte[] Serialize<T>(T value) =>
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Options).ReplaceLineEndings("\n") + "\n");

    private static void WriteSealed<T>(string path, T value) => WriteSealedBytes(path, Serialize(value));

    private static void WriteRows<T>(string path, IEnumerable<T> rows) =>
        WriteSealedBytes(path, Encoding.UTF8.GetBytes(string.Concat(rows.Select(row => JsonSerializer.Serialize(row, Options) + "\n"))));

    private static void WriteSealedBytes(string path, byte[] bytes)
    {
        if (File.Exists(path))
        {
            Require(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), "Refusing to replace sealed replay artifact: " + path);
            return;
        }

        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, bytes);
            File.Move(temporary, path);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private sealed record ReplayInput(
        string Language, string ManifestPath, JsonElement Envelope, RecoveryPin Entries, RecoveryPin Evidence,
        PronunciationEvidence[] Rows, PronunciationOriginProof[] Proofs, RecoveryPackage Package, RecoveryPin[] Tracked);
}
