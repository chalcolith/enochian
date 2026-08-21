using Enochian.Flow.Steps;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Enochian.UnitTests;

[TestClass]
public sealed class ScoredMatchExporterTests
{
    [TestMethod]
    public void WritesStableBlindedJsonCsvAndMetadata()
    {
        using var fixture = new ExportFixture();
        var records = new[]
        {
            Record("record-b", "query", "candidate-b", "plain", "form", 0.3, 2),
            Record("record-a", "query", "candidate-a", "café, \"quoted\"\nline", null, 0.30000000000000004, 1),
        };
        var definitions = new[]
        {
            new ScoredMatchDefinition("candidate-a", "secret definition"),
        };

        ScoredMatchExporter.Write(
            fixture.Options,
            records,
            definitions,
            "fixture-config",
            fixture.ConfigurationPath,
            fixture.SoftwarePath);
        var firstJson = File.ReadAllBytes(fixture.Options.Jsonl);
        var firstCsv = File.ReadAllBytes(fixture.Options.Csv);
        var firstMetadata = File.ReadAllBytes(fixture.Options.Metadata);
        var firstDefinitions = File.ReadAllBytes(fixture.Options.Definitions!);

        ScoredMatchExporter.Write(
            fixture.Options,
            records.Reverse(),
            definitions,
            "fixture-config",
            fixture.ConfigurationPath,
            fixture.SoftwarePath);

        CollectionAssert.AreEqual(firstJson, File.ReadAllBytes(fixture.Options.Jsonl));
        CollectionAssert.AreEqual(firstCsv, File.ReadAllBytes(fixture.Options.Csv));
        CollectionAssert.AreEqual(firstMetadata, File.ReadAllBytes(fixture.Options.Metadata));
        CollectionAssert.AreEqual(firstDefinitions, File.ReadAllBytes(fixture.Options.Definitions!));

        var json = Encoding.UTF8.GetString(firstJson);
        var csv = Encoding.UTF8.GetString(firstCsv);
        var definitionJson = Encoding.UTF8.GetString(firstDefinitions);
        Assert.DoesNotContain("secret definition", json, StringComparison.Ordinal);
        Assert.DoesNotContain("secret definition", csv, StringComparison.Ordinal);
        Assert.Contains("secret definition", definitionJson, StringComparison.Ordinal);
        Assert.Contains("0.30000000000000004", csv, StringComparison.Ordinal);
        Assert.Contains("\"café, \"\"quoted\"\"\nline\",,", csv, StringComparison.Ordinal);
        Assert.IsTrue(csv.Contains("\r\n", StringComparison.Ordinal));

        var lines = json.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.HasCount(2, lines);
        using var firstRecord = JsonDocument.Parse(lines[0]);
        Assert.AreEqual("record-a", firstRecord.RootElement.GetProperty("record_id").GetString());
        Assert.AreEqual(JsonValueKind.Null, firstRecord.RootElement.GetProperty("candidate_form").ValueKind);
        Assert.IsFalse(firstRecord.RootElement.TryGetProperty("definition", out _));

        using var metadata = JsonDocument.Parse(firstMetadata);
        Assert.AreEqual(ScoredMatchExporter.SchemaId, metadata.RootElement.GetProperty("schema_id").GetString());
        Assert.AreEqual(Hash(fixture.SchemaPath), metadata.RootElement.GetProperty("schema_sha256").GetString());
        Assert.AreEqual(Hash(fixture.ConfigurationPath), metadata.RootElement.GetProperty("configuration_sha256").GetString());
        Assert.AreEqual(Hash(fixture.SoftwarePath), metadata.RootElement.GetProperty("software_sha256").GetString());
        Assert.AreEqual(2, metadata.RootElement.GetProperty("record_count").GetInt32());
    }

    private static ScoredMatchRecord Record(
        string recordId,
        string queryId,
        string candidateId,
        string lemma,
        string? form,
        double cost,
        int rank) =>
        new(
            ScoredMatchExporter.SchemaVersion,
            recordId,
            "fixture-config",
            queryId,
            "query text",
            2,
            "lexicon",
            "source",
            "fra",
            "Indo-European",
            candidateId,
            lemma,
            form,
            2,
            cost,
            2,
            cost / 2,
            cost / 2,
            rank);

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private sealed class ExportFixture : IDisposable
    {
        public ExportFixture()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"enochian-scored-export-{Guid.NewGuid():N}");
            _ = Directory.CreateDirectory(Path);
            SchemaPath = Write("schema.json", "{\"schema\":1}\n");
            ConfigurationPath = Write("flow.json", "{\"id\":\"fixture\"}\n");
            SoftwarePath = Write("software.dll", "fixture software");
            Options = new ScoredMatchExportOptions
            {
                Jsonl = System.IO.Path.Combine(Path, "scores.jsonl"),
                Csv = System.IO.Path.Combine(Path, "scores.csv"),
                Metadata = System.IO.Path.Combine(Path, "scores.metadata.json"),
                Schema = SchemaPath,
                Definitions = System.IO.Path.Combine(Path, "definitions.jsonl"),
            };
        }

        public string Path { get; }
        public string SchemaPath { get; }
        public string ConfigurationPath { get; }
        public string SoftwarePath { get; }
        public ScoredMatchExportOptions Options { get; }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }

        private string Write(string name, string content)
        {
            var path = System.IO.Path.Combine(Path, name);
            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }
    }
}
