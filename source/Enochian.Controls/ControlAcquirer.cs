using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;

namespace Enochian.Controls;

public sealed class ControlAcquirer(HttpClient client)
{
    public async Task<string> AcquireAsync(
        ControlManifest manifest,
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var destination = Resolve(repositoryRoot, manifest.RawPath);
        if (File.Exists(destination) && string.Equals(Hash(destination), manifest.Sha256, StringComparison.Ordinal))
        {
            return destination;
        }

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        var temporary = destination + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            using var response = await client.GetAsync(new Uri(manifest.Url), HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            _ = response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = File.Create(temporary))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            var actual = Hash(temporary);
            if (!string.Equals(actual, manifest.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{manifest.SourceId}: SHA-256 {actual} does not match {manifest.Sha256}.");
            }

            File.Move(temporary, destination, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return destination;
    }

    public static string ExtractMagyar(string archivePath, string outputPath)
    {
        if (Directory.Exists(outputPath))
        {
            Directory.Delete(outputPath, true);
        }

        _ = Directory.CreateDirectory(outputPath);
        var root = Path.GetFullPath(outputPath) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var separator = entry.FullName.IndexOf('/');
            if (separator < 0 || entry.FullName.EndsWith('/'))
            {
                continue;
            }

            var relative = entry.FullName[(separator + 1)..].Replace('/', Path.DirectorySeparatorChar);
            var destination = Path.GetFullPath(Path.Combine(outputPath, relative));
            if (!destination.StartsWith(root, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Magyar Ispell archive contains an unsafe path.");
            }

            _ = Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
        }

        return Path.Combine(outputPath, "szotar");
    }

    public static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string Resolve(string root, string path) =>
        Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));
}
