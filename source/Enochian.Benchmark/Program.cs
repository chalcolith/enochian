using Enochian.Flow;

try
{
    if (args.Length is < 1 or > 2)
    {
        return ShowUsage();
    }

    var repositoryRoot = args.Length == 2 ? Path.GetFullPath(args[1]) : FindRepositoryRoot();
    var flow = new Flow(Path.Combine(repositoryRoot, "samples", "ipatransducer.json"));
    var errors = flow.Errors.Select(error => error.Message).ToArray();
    if (errors.Length != 0)
    {
        throw new InvalidDataException(string.Join(Environment.NewLine, errors));
    }

    var features = flow.FeatureSets.Single(featureSet => featureSet.Id == "Default");
    var encoding = flow.Encodings.Single(candidate => candidate.Id == "IPA");
    return new Enochian.Benchmark.BenchmarkRunner(repositoryRoot, features, encoding).Run(Path.GetFullPath(args[0]));
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static int ShowUsage()
{
    Console.Error.WriteLine("Usage: Enochian.Benchmark <protocol-json> [repository-root]");
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
