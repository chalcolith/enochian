using Enochian.Controls;

try
{
    var command = args.FirstOrDefault() ?? "acquire-normalize";
    var repositoryRoot = args.Length > 1 ? Path.GetFullPath(args[1]) : FindRepositoryRoot();
    var pythonPath = args.Length > 2
        ? Path.GetFullPath(args[2])
        : Environment.GetEnvironmentVariable("ENOCHIAN_EPITRAN_PYTHON") ?? "python";
    var pipeline = new ControlPipeline(repositoryRoot);
    return command switch
    {
        "acquire-normalize" => await pipeline.RunAsync(true, pythonPath),
        "normalize" => await pipeline.RunAsync(false, pythonPath),
        _ => ShowUsage(),
    };
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}

static int ShowUsage()
{
    Console.Error.WriteLine("Usage: Enochian.Controls <acquire-normalize|normalize> [repository-root] [python-path]");
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
