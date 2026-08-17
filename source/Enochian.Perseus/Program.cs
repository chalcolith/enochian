using Enochian.Perseus;

try
{
    var repositoryRoot = args.Length > 1
        ? Path.GetFullPath(args[1])
        : FindRepositoryRoot();
    var pipeline = new PerseusPipeline(repositoryRoot);
    return args.FirstOrDefault() switch
    {
        "acquire-normalize" => await pipeline.RunAsync(acquire: true),
        "normalize" => await pipeline.RunAsync(acquire: false),
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
    Console.Error.WriteLine("Usage: Enochian.Perseus <acquire-normalize|normalize> [repository-root]");
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
