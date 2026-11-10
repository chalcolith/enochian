using Enochian.Flow;

try
{
    if (args.Length is 3 or 4 && args[0] == "publish-m4")
    {
        var publicationRoot = args.Length == 4 ? Path.GetFullPath(args[3]) : FindRepositoryRoot();
        return new Enochian.Benchmark.M4RunPublication(publicationRoot).Run(
            Path.GetFullPath(args[1]), args[2]);
    }

    if (args.Length is 1 or 2 && args[0] == "sanskrit-profile")
    {
        var sanskritRoot = args.Length == 2 ? Path.GetFullPath(args[1]) : FindRepositoryRoot();
        return new Enochian.Benchmark.SanskritProfileMaterializer(sanskritRoot).Run();
    }

    if (args.Length is 2 or 3 && args[0] == "mapping-preregister")
    {
        var mappingRoot = args.Length == 3 ? Path.GetFullPath(args[2]) : FindRepositoryRoot();
        var mappingFlow = new Flow(Path.Combine(mappingRoot, "samples", "ipatransducer.json"));
        var mappingErrors = mappingFlow.Errors.Select(error => error.Message).ToArray();
        if (mappingErrors.Length != 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, mappingErrors));
        }

        var mappingFeatures = mappingFlow.FeatureSets.Single(featureSet => featureSet.Id == "Default");
        var mappingEncoding = mappingFlow.Encodings.Single(candidate => candidate.Id == "IPA");
        return new Enochian.Benchmark.MappingPreregistration(mappingRoot, mappingFeatures, mappingEncoding)
            .Run(Path.GetFullPath(args[1]));
    }

    if (args.Length == 2 && args[0] == "register-v2")
    {
        Enochian.Benchmark.AutomatedRegistration.Freeze(Path.GetFullPath(args[1]));
        return 0;
    }

    if (args.Length == 3 && args[0] == "synthetic-v2")
    {
        _ = Enochian.Benchmark.AutomatedRegistration.CreateSynthetic(Path.GetFullPath(args[2]), args[1]);
        return 0;
    }

    if (args.Length is < 1 or > 3)
    {
        return ShowUsage();
    }

    var mode = args[0] switch
    {
        "sample" => "sample",
        "statistics" => "statistics",
        "experiment" => "experiment",
        "admit" => "admit",
        "preflight" => "preflight",
        _ => "benchmark",
    };
    var protocolIndex = mode == "benchmark" ? 0 : 1;
    var rootIndex = protocolIndex + 1;
    if (args.Length <= protocolIndex || args.Length > rootIndex + 1)
    {
        return ShowUsage();
    }

    var repositoryRoot = args.Length > rootIndex ? Path.GetFullPath(args[rootIndex]) : FindRepositoryRoot();
    var protocolPath = Path.GetFullPath(args[protocolIndex]);
    var revised = Enochian.Benchmark.AutomatedProtocol.IsRevised(protocolPath);
    var flow = revised ? Enochian.Benchmark.AutomatedExperiment.CreateFlow(protocolPath, repositoryRoot)
        : new Flow(Path.Combine(repositoryRoot, "samples", "ipatransducer.json"));
    var errors = flow.Errors.Select(error => error.Message).ToArray();
    if (errors.Length != 0)
    {
        throw new InvalidDataException(string.Join(Environment.NewLine, errors));
    }

    var features = flow.FeatureSets.Single(featureSet => featureSet.Id == "Default");
    var encoding = flow.Encodings.Single(candidate => candidate.Id == "IPA");
    if (revised)
    {
        if (mode is "sample" or "statistics")
        {
            throw new InvalidDataException("V2 uses the combined experiment protocol; use admit, preflight or experiment.");
        }

        return new Enochian.Benchmark.AutomatedExperiment(repositoryRoot, features, encoding).Run(
            protocolPath, mode == "benchmark" ? "admit" : mode);
    }

    if (mode is "admit" or "preflight")
    {
        throw new InvalidDataException("Automated admission/preflight requires a v2 protocol; v1 review rules are unchanged.");
    }

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
    Console.Error.WriteLine("Usage: Enochian.Benchmark publish-m4 <protocol-json> <runner-commit> [repository-root]");
    Console.Error.WriteLine("Usage: Enochian.Benchmark sanskrit-profile [repository-root]");
    Console.Error.WriteLine("Usage: Enochian.Benchmark <protocol-json> [repository-root]");
    Console.Error.WriteLine("       Enochian.Benchmark sample <sampling-protocol-json> [repository-root]");
    Console.Error.WriteLine("       Enochian.Benchmark statistics <statistics-protocol-json> [repository-root]");
    Console.Error.WriteLine("       Enochian.Benchmark experiment <run-protocol-json> [repository-root]");
    Console.Error.WriteLine("       Enochian.Benchmark admit|preflight <automated-v2-protocol-json> [repository-root]");
    Console.Error.WriteLine("       Enochian.Benchmark mapping-preregister <preregistration-json> [repository-root]");
    Console.Error.WriteLine("       Enochian.Benchmark synthetic-v2 <fixture-directory> <repository-root>");
    Console.Error.WriteLine("       Enochian.Benchmark register-v2 <repository-root>");
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
