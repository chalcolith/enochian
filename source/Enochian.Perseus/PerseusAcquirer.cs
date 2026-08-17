using System.Globalization;
using System.Security.Cryptography;

namespace Enochian.Perseus;

public sealed class PerseusAcquirer(HttpClient httpClient)
{
    public async Task AcquireAsync(
        PerseusManifest manifest,
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var destination = ResolvePath(repositoryRoot, manifest.RawPath);
        if (File.Exists(destination) && string.Equals(HashFile(destination), manifest.Sha256, StringComparison.Ordinal))
        {
            return;
        }

        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        var temporaryPath = destination + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            using var response = await httpClient.GetAsync(
                new Uri(manifest.SourceUrl, UriKind.Absolute),
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            _ = response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                useAsync: true))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            var actual = HashFile(temporaryPath);
            if (!string.Equals(actual, manifest.Sha256, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"{manifest.SourceId}: downloaded SHA-256 {actual} does not match manifest {manifest.Sha256}.");
            }

            File.Move(temporaryPath, destination, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static string ResolvePath(string repositoryRoot, string relativePath)
    {
        return Path.GetFullPath(Path.Combine(
            repositoryRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}