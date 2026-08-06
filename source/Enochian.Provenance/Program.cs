using Enochian.Provenance;

try
{
    var repositoryRoot = FindRepositoryRoot();
    var manifestDirectory = args.Length > 1
        ? Path.GetFullPath(args[1])
        : Path.Combine(repositoryRoot, "resources", "lexicons", "manifests");
    var schemaPath = Path.Combine(repositoryRoot, "resources", "lexicons", "schemas",
        "source-manifest.schema.json");
    var validator = new ManifestValidator(repositoryRoot, schemaPath);
    var paths = ManifestValidator.FindManifests(manifestDirectory);

    return args.FirstOrDefault() switch
    {
        "validate" => Validate(validator, paths),
        "attribution" => GenerateAttribution(validator, paths, args),
        _ => ShowUsage(),
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static int Validate(ManifestValidator validator, IReadOnlyList<string> paths)
{
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
    ManifestValidator validator,
    IReadOnlyList<string> paths,
    string[] arguments)
{
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

static int ShowUsage()
{
    Console.Error.WriteLine("Usage: Enochian.Provenance <validate|attribution> [manifest-directory] [output-file]");
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
