namespace Enochian.Cdsl;

public sealed class CdslPipeline
{
    public const string AdapterVersion = "1.0.0";
    public const string TransformCommand = "dotnet run --project source/Enochian.Cdsl -- acquire-normalize";

    private readonly string repositoryRoot;
    private readonly IReadOnlyList<CdslManifest> manifests;
    private readonly CdslOrigAdapter adapter;

    public CdslPipeline(string repositoryRoot)
    {
        this.repositoryRoot = Path.GetFullPath(repositoryRoot);
        manifests = CdslManifest.LoadAll(Path.Combine(this.repositoryRoot, "resources", "lexicons", "manifests"));

        var flowPath = Path.Combine(this.repositoryRoot, "resources", "lexicons", "cdsl-normalization.flow.json");
        var flow = new Enochian.Flow.Flow(flowPath);
        var errors = flow.Errors.Select(error => error.Message).Where(message => !string.IsNullOrWhiteSpace(message)).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        var features = flow.FeatureSets.Single(featureSet => string.Equals(featureSet.Id, "Default", StringComparison.Ordinal));
        var slp1 = flow.Encodings.Single(encoding => string.Equals(encoding.Id, "SLP1", StringComparison.Ordinal));
        adapter = new CdslOrigAdapter(features, slp1);
    }

    public async Task<int> RunAsync(bool acquire, CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient();
        var acquirer = new CdslAcquirer(httpClient);

        foreach (var manifest in manifests)
        {
            if (acquire)
            {
                Console.WriteLine($"Acquiring {manifest.SourceId} at {manifest.Revision}...");
                await acquirer.AcquireAsync(manifest, repositoryRoot, cancellationToken);
            }

            var rawPath = ResolvePath(manifest.RawPath);
            if (!File.Exists(rawPath))
            {
                throw new FileNotFoundException(
                    $"{manifest.SourceId}: raw source is absent; run acquire-normalize.",
                    rawPath);
            }

            var actualHash = CdslAcquirer.HashFile(rawPath);
            if (!string.Equals(actualHash, manifest.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{manifest.SourceId}: local SHA-256 {actualHash} does not match manifest {manifest.Sha256}.");
            }

            var outputPath = ResolvePath(manifest.GeneratedArtifactPath);
            var reportPath = Path.ChangeExtension(outputPath, ".quality.json");
            Console.WriteLine($"Normalizing {manifest.SourceId}...");
            var report = adapter.Normalize(manifest, rawPath, outputPath, reportPath, TransformCommand);
            Console.WriteLine(
                $"  wrote {report.EmittedRecords} records; rejected {report.RejectedRecords}; unknown SLP1 symbols {report.UnknownSlp1Symbols.Count}");

            var unreviewedUnknowns = report.Rejections
                .Where(rejection => string.Equals(rejection.ReasonCode, "unknown_slp1", StringComparison.Ordinal))
                .Where(rejection => !IsReviewedUnknown(rejection))
                .ToArray();
            if (unreviewedUnknowns.Length != 0)
            {
                throw new InvalidDataException(
                    $"{manifest.SourceId}: {unreviewedUnknowns.Length} unknown SLP1 rejection(s) require review.");
            }
        }

        return 0;
    }

    private static bool IsReviewedUnknown(CdslAdapterRejection rejection)
    {
        return string.Equals(rejection.SourceId, "cdsl-ap", StringComparison.Ordinal)
            && string.Equals(rejection.SourceRecordId, "6082.002", StringComparison.Ordinal)
            && string.Equals(rejection.Reason, "Unknown SLP1 symbol(s): V", StringComparison.Ordinal);
    }

    private string ResolvePath(string relativePath)
    {
        return Path.GetFullPath(Path.Combine(
            repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
