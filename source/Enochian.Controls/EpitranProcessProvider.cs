using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Enochian.Controls;

public interface IControlIpaProvider
{
    void Convert(
        string profileId,
        string sourceId,
        string language,
        IEnumerable<ControlSourceLemma> lemmas,
        string outputPath);
}

public sealed class EpitranProcessProvider(string pythonPath, string workerPath) : IControlIpaProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public void Convert(
        string profileId,
        string sourceId,
        string language,
        IEnumerable<ControlSourceLemma> lemmas,
        string outputPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = pythonPath,
            Arguments = $"\"{workerPath}\" {profileId}",
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
        };
        startInfo.Environment["PYTHONUTF8"] = "1";
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Unable to start the Epitran worker.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        IOException? writeError = null;
        try
        {
            foreach (var lemma in lemmas)
            {
                var request = new EpitranRequest
                {
                    SchemaVersion = "1.0.0",
                    RecordId = lemma.RecordId,
                    Source = sourceId,
                    Language = language,
                    SourceForm = lemma.NormalizedForm,
                };
                process.StandardInput.WriteLine(JsonSerializer.Serialize(request, SerializerOptions));
            }

            process.StandardInput.Close();
        }
        catch (IOException exception)
        {
            writeError = exception;
        }
        process.WaitForExit();
        var output = outputTask.GetAwaiter().GetResult();
        var error = errorTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
        {
            throw new InvalidDataException($"Epitran worker exited {process.ExitCode}: {error.Trim()}");
        }

        if (writeError != null)
        {
            throw new InvalidDataException("Epitran worker closed its input unexpectedly.", writeError);
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        var temporary = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, output.ReplaceLineEndings("\n"), new UTF8Encoding(false));
            File.Move(temporary, outputPath, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private sealed class EpitranRequest
    {
        public string SchemaVersion { get; init; } = string.Empty;
        public string RecordId { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public string Language { get; init; } = string.Empty;
        public string SourceForm { get; init; } = string.Empty;
    }
}
