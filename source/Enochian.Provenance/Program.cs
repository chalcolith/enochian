using Enochian.Provenance;
using IpaEncoder = Enochian.Text.Encoder;

try
{
    var repositoryRoot = FindRepositoryRoot();
    return args.FirstOrDefault() switch
    {
        "validate" => ValidateManifests(repositoryRoot, args),
        "attribution" => GenerateAttribution(repositoryRoot, args),
        "ipa-audit" => AuditIpa(repositoryRoot, args),
        _ => ShowUsage(),
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static int ValidateManifests(string repositoryRoot, string[] arguments)
{
    var (validator, paths) = GetManifests(repositoryRoot, arguments);
    var report = validator.Validate(paths);
    foreach (var issue in report.Issues)
    {
        Console.Error.WriteLine(issue);
    }

    if (!report.IsValid)
    {
        return 1;
    }

    Console.WriteLine($"Validated {paths.Count} source manifests.");
    return 0;
}

static int GenerateAttribution(
    string repositoryRoot,
    string[] arguments)
{
    var (validator, paths) = GetManifests(repositoryRoot, arguments);
    var report = validator.GenerateAttribution(paths);
    if (arguments.Length > 2)
    {
        File.WriteAllText(arguments[2], report);
    }
    else
    {
        Console.Write(report);
    }

    return 0;
}

static int AuditIpa(string repositoryRoot, string[] arguments)
{
    var sampleSize = 100;
    if (arguments.Length is < 5 or > 6 ||
        (arguments.Length == 6 &&
            (!int.TryParse(
                arguments[5],
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out sampleSize) || sampleSize < 0)))
    {
        return ShowUsage();
    }

    var flow = new Enochian.Flow.Flow(Path.Combine(repositoryRoot, "samples", "ipatransducer.json"));
    var errors = flow.Errors.Select(error => error.Message).ToArray();
    if (errors.Length != 0)
    {
        throw new InvalidDataException(string.Join(Environment.NewLine, errors));
    }

    var features = flow.FeatureSets.Single(featureSet => featureSet.Id == "Default");
    var encoding = flow.Encodings.Single(candidate =>
        string.Equals(candidate.Id, "IPA", StringComparison.OrdinalIgnoreCase));
    var schemaDirectory = Path.Combine(repositoryRoot, "resources", "lexicons", "schemas");
    var auditor = new IpaArtifactAuditor(
        Path.Combine(schemaDirectory, "ipa-conversion-artifact.schema.json"),
        Path.Combine(schemaDirectory, "ipa-conversion-profile.schema.json"),
        new IpaEncoder(features, encoding));
    var result = auditor.Audit(
        Path.GetFullPath(arguments[1]),
        Path.GetFullPath(arguments[2]),
        sampleSize);
    IpaArtifactAuditor.WriteReviewSheet(Path.GetFullPath(arguments[3]), result.ReviewRows);
    IpaArtifactAuditor.WriteSummary(Path.GetFullPath(arguments[4]), result.Summary);

    foreach (var issue in result.Summary.Issues)
    {
        Console.Error.WriteLine($"line {issue.Line}: {issue.RecordId ?? "unknown"}: {issue.Code}: {issue.Message}");
    }

    Console.WriteLine(
        $"Audited {result.Summary.TotalRecords} records: {result.Summary.AcceptedRecords} accepted, " +
        $"{result.Summary.RejectedRecords} rejected; wrote {result.ReviewRows.Count} blinded review rows.");
    return result.Summary.RejectedRecords == 0 ? 0 : 1;
}

static (ManifestValidator Validator, IReadOnlyList<string> Paths) GetManifests(
    string repositoryRoot,
    string[] arguments)
{
    var manifestDirectory = arguments.Length > 1
        ? Path.GetFullPath(arguments[1])
        : Path.Combine(repositoryRoot, "resources", "lexicons", "manifests");
    var schemaPath = Path.Combine(repositoryRoot, "resources", "lexicons", "schemas",
        "source-manifest.schema.json");
    var validator = new ManifestValidator(repositoryRoot, schemaPath);
    return (validator, ManifestValidator.FindManifests(manifestDirectory));
}

static int ShowUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  Enochian.Provenance validate [manifest-directory]");
    Console.Error.WriteLine("  Enochian.Provenance attribution [manifest-directory] [output-file]");
    Console.Error.WriteLine(
        "  Enochian.Provenance ipa-audit <artifacts-jsonl> <profile-json> <review-jsonl> <summary-json> [sample-size]");
    return 1;
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName
        ?? throw new DirectoryNotFoundException("Unable to locate repository root.");
}
