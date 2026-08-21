using Enochian.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using IpaEncoder = Enochian.Text.Encoder;

namespace Enochian.Benchmark;

public sealed class SamplingRunner(string repositoryRoot, FeatureSet features, Text.Encoding encoding)
{
    private readonly string repositoryRoot = Path.GetFullPath(repositoryRoot);
    private readonly IpaEncoder encoder = new(features, encoding);

    public int Run(string protocolPath)
    {
        var resolvedProtocol = Resolve(protocolPath, repositoryRoot);
        var protocol = SamplingProtocol.Load(resolvedProtocol);
        var protocolDirectory = Path.GetDirectoryName(resolvedProtocol)!;
        var queries = SamplingInputLoader.LoadQueries(Resolve(protocol.QueriesPath, protocolDirectory));
        foreach (var analysis in protocol.Analyses.OrderBy(analysis => analysis.AnalysisId, StringComparer.Ordinal))
        {
            var inputs = analysis.Sources
                .OrderBy(source => source.SourceId, StringComparer.Ordinal)
                .SelectMany(source => SamplingInputLoader.LoadCandidates(
                    Resolve(source.LexiconPath, protocolDirectory),
                    source.Language,
                    encoder))
                .ToArray();
            var candidates = CandidateSetBuilder.Build(
                inputs,
                analysis.IncludedEntryKinds.ToHashSet(StringComparer.Ordinal));
            var sampling = BalancedSampler.Sample(
                analysis.AnalysisId,
                analysis.AnalysisSet,
                candidates,
                analysis.SmallerSampleSizes,
                protocol.Repetitions,
                protocol.Seed,
                protocol.GeneratorVersion,
                analysis.FrequencyBands);
            var candidatesById = candidates.ToDictionary(candidate => candidate.CandidateId, StringComparer.Ordinal);
            var nulls = sampling.Memberships
                .GroupBy(membership => membership.SampleId, StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .SelectMany(group => SequenceNullGenerator.GenerateForSample(
                    analysis.AnalysisId,
                    group.Key,
                    group.First().RequestedSize,
                    group.Select(membership => candidatesById[membership.CandidateId]).DistinctBy(candidate => candidate.CandidateId),
                    queries,
                    protocol.Mapping,
                    group.First().Repetition,
                    protocol.Seed,
                    protocol.GeneratorVersion))
                .ToArray();
            WriteJsonLines(Resolve(analysis.Outputs.Memberships, protocolDirectory), sampling.Memberships);
            WriteJsonLines(Resolve(analysis.Outputs.Nulls, protocolDirectory), nulls);
            WriteJson(Resolve(analysis.Outputs.Report, protocolDirectory), new SamplingReport(
                "1.0.0",
                protocol.SamplingId,
                analysis.AnalysisId,
                analysis.AnalysisSet,
                protocol.Seed,
                protocol.GeneratorVersion,
                sampling.LargestCommonSize,
                sampling.SampleSizes,
                candidates.GroupBy(candidate => candidate.Language)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                sampling.Shortages,
                sampling.Memberships.Count,
                nulls.Length));
        }

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

    private static void WriteJson<T>(string path, T value) =>
        WriteAtomically(path, temporary => File.WriteAllText(
            temporary,
            JsonSerializer.Serialize(value, BenchmarkProtocol.SerializerOptions).ReplaceLineEndings("\n") + "\n",
            new UTF8Encoding(false)));

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
