using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Enochian.Benchmark;

public sealed class StatisticsRunner(string repositoryRoot)
{
    private readonly string repositoryRoot = Path.GetFullPath(repositoryRoot);

    public int Run(string protocolPath)
    {
        var resolvedProtocol = Resolve(protocolPath, repositoryRoot);
        var protocol = StatisticsProtocol.Load(resolvedProtocol);
        var protocolDirectory = Path.GetDirectoryName(resolvedProtocol)!;
        var input = StatisticalInput.Load(
            Resolve(protocol.InputPath, protocolDirectory),
            Resolve(protocol.InputSchemaPath, protocolDirectory));
        var result = StatisticalAnalyzer.Analyze(protocol, input);
        WriteJsonLines(Resolve(protocol.Outputs.CalibratedScores, protocolDirectory), result.CalibratedScores);
        WriteJsonLines(Resolve(protocol.Outputs.Estimates, protocolDirectory), result.Estimates);
        WriteJsonLines(Resolve(protocol.Outputs.Intervals, protocolDirectory), result.Intervals);
        WriteJsonLines(Resolve(protocol.Outputs.Tests, protocolDirectory), result.Tests);
        WriteJsonLines(Resolve(protocol.Outputs.AdjustedPValues, protocolDirectory), result.AdjustedPValues);
        WriteJsonLines(Resolve(protocol.Outputs.Diagnostics, protocolDirectory), result.Diagnostics);
        return 0;
    }

    private static string Resolve(string path, string root) =>
        Path.GetFullPath(Path.Combine(root, path.Replace('/', Path.DirectorySeparatorChar)));

    private static void WriteJsonLines<T>(string path, IEnumerable<T> rows) =>
        WriteAtomically(path, temporary =>
        {
            using var writer = new StreamWriter(temporary, false, new UTF8Encoding(false)) { NewLine = "\n" };
            foreach (var row in rows)
            {
                writer.WriteLine(JsonSerializer.Serialize(row, BenchmarkProtocol.LineSerializerOptions));
            }
        });

    private static void WriteAtomically(string path, Action<string> write)
    {
        _ = Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            write(temporary);
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
