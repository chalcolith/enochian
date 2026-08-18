using Enochian.Bhsa;

try
{
    return args.FirstOrDefault() switch
    {
        "status" => ShowStatus(args.Length > 1 ? Path.GetFullPath(args[1]) : FindRepositoryRoot()),
        "export-normalize" => ExportNormalize(
            args.Length > 1 ? Path.GetFullPath(args[1]) : FindRepositoryRoot(),
            args.Length > 2 ? Path.GetFullPath(args[2]) : "python"),
        "normalize" when args.Length is 3 or 4 => Normalize(
            Path.GetFullPath(args[1]),
            args[2],
            args.Length == 4 ? args[3] : null),
        _ => ShowUsage(),
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static int ShowStatus(string repositoryRoot)
{
    Console.WriteLine(BhsaSnapshot.Inspect(repositoryRoot).Message);
    return 0;
}

static int ExportNormalize(string repositoryRoot, string pythonPath)
{
    var occurrences = BhsaSnapshot.ExportOccurrences(repositoryRoot, pythonPath);
    return Normalize(repositoryRoot, occurrences, null);
}

static int Normalize(string repositoryRoot, string inputArgument, string? outputArgument)
{
    var input = Path.GetFullPath(inputArgument);
    var output = outputArgument == null
        ? Path.Combine(repositoryRoot, ".enoch", "bhsa-generated")
        : Path.GetFullPath(outputArgument);
    var report = new BhsaPipeline(repositoryRoot).Normalize(input, output, sampleSize: 100);
    Console.WriteLine(
        $"Biblical Hebrew: emitted {report.EmittedLexemes} unique lexemes from {report.ConversionRecords} readings; " +
        $"prepared {report.ReviewRecords} blinded review rows; confirmatory eligible: {report.ConfirmatoryEligible}.");
    return report.ConfirmatoryEligible ? 0 : 1;
}

static int ShowUsage()
{
    Console.Error.WriteLine("Usage:");
    Console.Error.WriteLine("  Enochian.Bhsa status [repository-root]");
    Console.Error.WriteLine("  Enochian.Bhsa export-normalize [repository-root] [python-path]");
    Console.Error.WriteLine("  Enochian.Bhsa normalize [repository-root] <occurrences-jsonl> [output-directory]");
    return 1;
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(AppContext.BaseDirectory);
    while (directory != null && !File.Exists(Path.Combine(directory.FullName, "README.md")))
    {
        directory = directory.Parent;
    }

    return directory?.FullName ?? throw new DirectoryNotFoundException("Unable to locate repository root.");
}
