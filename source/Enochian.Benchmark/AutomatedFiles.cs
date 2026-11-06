using Enochian.Provenance;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Enochian.Benchmark;

internal sealed class AutomatedFiles(string repositoryRoot)
{
    private readonly SortedDictionary<string, HashedArtifact> inputs = new(StringComparer.Ordinal);
    private readonly HashSet<string> protectedPaths = new(StringComparer.OrdinalIgnoreCase);

    public string Resolve(string path)
    {
        if (Path.IsPathRooted(path))
        {
            throw new InvalidDataException("V2 paths must be repository-relative.");
        }

        var resolved = Path.GetFullPath(Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)));
        var relative = Path.GetRelativePath(repositoryRoot, resolved);
        if (relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException("V2 paths must remain inside the repository.");
        }

        return resolved;
    }

    public HashedArtifact[] Inputs => [.. inputs.Values];
    public string[] ProtectedPaths => [.. protectedPaths];
    public void Protect(string path) => _ = protectedPaths.Add(Path.GetFullPath(path));

    public string Pin(PinnedInput pin, bool configuration = true)
    {
        var path = Resolve(pin.Path);
        Protect(path);
        var hash = configuration ? PronunciationAudit.HashConfiguration(path) : PronunciationAudit.Hash(path);
        if (hash != pin.Sha256)
        {
            throw new InvalidDataException("pinned_input_hash_mismatch:" + pin.Path);
        }

        Track(path);
        return path;
    }

    public void Track(string absolutePath)
    {
        Protect(absolutePath);
        var artifact = ExperimentHashing.Artifact(absolutePath, repositoryRoot);
        inputs[artifact.Path] = artifact;
    }

    public void TrackSoftware(string path)
    {
        Protect(path);
        var name = "@runtime/" + Path.GetFileName(path);
        inputs[name] = new(name, ExperimentHashing.HashFile(path), new FileInfo(path).Length);
    }

    public JsonDocument Read(PinnedInput pin, bool configuration = true) => JsonDocument.Parse(File.ReadAllText(Pin(pin, configuration)));

    public static void Write<T>(string path, T value) => WriteLines(path, [value], indented: true);

    public static void WriteLines<T>(string path, IEnumerable<T> values, bool indented = false)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            var options = new JsonSerializerOptions(AutomatedProtocol.JsonOptions) { WriteIndented = indented };
            using (var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)) { NewLine = "\n" })
            {
                foreach (var value in values)
                {
                    writer.WriteLine(JsonSerializer.Serialize(value, options).ReplaceLineEndings("\n"));
                }
            }

            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
