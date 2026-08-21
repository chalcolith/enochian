using Enochian.Flow;

try
{
    if (args.Length is < 1 or > 3)
    {
        return ShowUsage();
    }

    var mode = args[0] switch
    {
        "sample" => "sample",
        "statistics" => "statistics",
        "experiment" => "experiment",
        _ => "benchmark",
    };
    var protocolIndex = mode == "benchmark" ? 0 : 1;
    var rootIndex = protocolIndex + 1;
    if (args.Length <= protocolIndex || args.Length > rootIndex + 1)
    {
        return ShowUsage();
    }

    var repositoryRoot = args.Length > rootIndex ? Path.GetFullPath(args[rootIndex]) : FindRepositoryRoot();
    var flow = new Flow(Path.Combine(repositoryRoot, "samples", "ipatransducer.json"));
    var errors = flow.Errors.Select(error => error.Message).ToArray();
    if (errors.Length != 0)
    {
        throw new InvalidDataException(string.Join(Environment.NewLine, errors));
    }

    var features = flow.FeatureSets.Single(featureSet => featureSet.Id == "Default");
    var encoding = flow.Encodings.Single(candidate => candidate.Id == "IPA");
    var protocolPath = Path.GetFullPath(args[protocolIndex]);
    return mode switch
    {
        "sample" => new Enochian.Benchmark.SamplingRunner(repositoryRoot, features, encoding).Run(protocolPath),
        "statistics" => new Enochian.Benchmark.StatisticsRunner(repositoryRoot).Run(protocolPath),
        "experiment" => new Enochian.Benchmark.ExperimentRunner(repositoryRoot, features, encoding).Run(protocolPath),
        _ => new Enochian.Benchmark.BenchmarkRunner(repositoryRoot, features, encoding).Run(protocolPath),
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static int ShowUsage()
{
    Console.Error.WriteLine("Usage: Enochian.Benchmark <protocol-json> [repository-root]");
    Console.Error.WriteLine("       Enochian.Benchmark sample <sampling-protocol-json> [repository-root]");
    Console.Error.WriteLine("       Enochian.Benchmark statistics <statistics-protocol-json> [repository-root]");
    Console.Error.WriteLine("       Enochian.Benchmark experiment <run-protocol-json> [repository-root]");
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
