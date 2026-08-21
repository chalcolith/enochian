using System.Security.Cryptography;
using System.Text;

namespace Enochian.Benchmark;

public static class ExperimentHashing
{
    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string HashValues(IEnumerable<string> values)
    {
        var content = string.Join('\n', values) + "\n";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
    }

    public static HashedArtifact Artifact(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
        return new(relative, HashFile(path), new FileInfo(path).Length);
    }
}
