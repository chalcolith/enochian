using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Enochian.Bhsa;

public sealed record BhsaSnapshotStatus(string State, string Message, bool IsReady);

public static class BhsaSnapshot
{
    public const string BhsaSha256 = "8104fae1151c926cfcfd01f7e8a30a09af8c607546f14482990833b624b73168";
    public const string PhonoSha256 = "8b46294e98f54fc5b70c1892159a320da78e889555478b20a43e7bbe8a9310ab";

    public static BhsaSnapshotStatus Inspect(string repositoryRoot)
    {
        var (bhsaArchive, phonoArchive) = GetArchivePaths(repositoryRoot);
        if (!File.Exists(bhsaArchive) || !File.Exists(phonoArchive))
        {
            return new("not-installed",
                "Optional Biblical Hebrew data is not installed; default build and tests remain available.", false);
        }

        if (!string.Equals(Hash(bhsaArchive), BhsaSha256, StringComparison.Ordinal) ||
            !string.Equals(Hash(phonoArchive), PhonoSha256, StringComparison.Ordinal))
        {
            return new("invalid", "Optional Biblical Hebrew archives do not match the pinned BHSA/phono releases.", false);
        }

        return new("ready", "Authorized local BHSA v1.8.1 and phono v2.1 archives are ready.", true);
    }

    public static string ExportOccurrences(string repositoryRoot, string pythonPath)
    {
        var status = Inspect(repositoryRoot);
        if (!status.IsReady)
        {
            throw new InvalidDataException(status.Message);
        }

        var (bhsaArchive, phonoArchive) = GetArchivePaths(repositoryRoot);
        var workingRoot = Path.Combine(repositoryRoot, ".enoch", "bhsa", "work");
        if (Directory.Exists(workingRoot))
        {
            Directory.Delete(workingRoot, true);
        }

        var bhsaRoot = Path.Combine(workingRoot, "bhsa");
        var phonoRoot = Path.Combine(workingRoot, "phono");
        ExtractSafely(bhsaArchive, bhsaRoot);
        ExtractSafely(phonoArchive, phonoRoot);
        var bhsaTf = FindFeatureDirectory(bhsaRoot, "otype.tf");
        var phonoTf = FindFeatureDirectory(phonoRoot, "phono.tf");
        var output = Path.Combine(repositoryRoot, ".enoch", "bhsa", "bhsa-occurrences.jsonl");
        RunExporter(
            pythonPath,
            Path.Combine(repositoryRoot, "tools", "bhsa", "export.py"),
            bhsaTf,
            phonoTf,
            output);
        return output;
    }

    public static void ExtractSafely(string archivePath, string outputPath)
    {
        _ = Directory.CreateDirectory(outputPath);
        var root = Path.GetFullPath(outputPath) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var destination = Path.GetFullPath(Path.Combine(
                outputPath,
                entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(root, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Optional BHSA archive contains an unsafe path.");
            }

            if (entry.FullName.EndsWith('/'))
            {
                _ = Directory.CreateDirectory(destination);
                continue;
            }

            _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }
    }

    private static (string BhsaArchive, string PhonoArchive) GetArchivePaths(string repositoryRoot)
    {
        var root = Path.Combine(Path.GetFullPath(repositoryRoot), ".enoch", "bhsa");
        return (Path.Combine(root, "complete.zip"), Path.Combine(root, "tf-2021.zip"));
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string FindFeatureDirectory(string root, string feature)
    {
        var path = Directory.EnumerateFiles(root, feature, SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        return path == null
            ? throw new InvalidDataException($"Pinned archive does not contain {feature}.")
            : Path.GetDirectoryName(path)!;
    }

    private static void RunExporter(
        string pythonPath,
        string workerPath,
        string bhsaTf,
        string phonoTf,
        string outputPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = $"\"{workerPath}\" \"{bhsaTf}\" \"{phonoTf}\" \"{outputPath}\"",
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
        };
        startInfo.Environment["PYTHONUTF8"] = "1";
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the optional BHSA exporter.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException($"Optional BHSA exporter exited {process.ExitCode}: {error.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(output))
        {
            Console.Write(output);
        }
    }
}
