using Json.Schema;
using Microsoft.VisualBasic.FileIO;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Enochian.Provenance;

public sealed record AstronomicalName(string CatalogId, string OriginalName, string MatchKey, bool Ambiguous = false);

public sealed record AstronomicalNameProtocol
{
    public required string SchemaVersion { get; init; }
    public required string AnalysisId { get; init; }
    public required bool Frozen { get; init; }
    public required string Manifest { get; init; }
    public required string PronunciationPanel { get; init; }
    public required string PronunciationPanelSha256 { get; init; }
    public required string OutputDirectory { get; init; }
    public required string ReportPath { get; init; }
    public required string[] Rules { get; init; }
}

public sealed class AstronomicalNameRegistry(string repositoryRoot)
{
    public async Task RunAsync(string protocolPath, bool acquire, CancellationToken cancellationToken = default)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(protocolPath, new UTF8Encoding(false, true)));
        var schema = JsonSchema.FromText(File.ReadAllText(Resolve(
            "resources/lexicons/schemas/astronomical-names.schema.json")),
            new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        if (!schema.Evaluate(document.RootElement).IsValid)
        {
            throw new InvalidDataException($"{protocolPath}: invalid astronomical name protocol.");
        }

        var protocol = JsonSerializer.Deserialize<AstronomicalNameProtocol>(document.RootElement, PronunciationAudit.JsonOptions)
            ?? throw new InvalidDataException($"{protocolPath}: empty astronomical name protocol.");
        var panelPath = Resolve(protocol.PronunciationPanel);
        if (PronunciationAudit.HashConfiguration(panelPath) != protocol.PronunciationPanelSha256)
        {
            throw new InvalidDataException($"{panelPath}: astronomical analysis pronunciation-panel hash mismatch.");
        }

        var panel = PronunciationPanel.Load(panelPath, repositoryRoot);
        var source = new PronunciationAudit(repositoryRoot).LoadSnapshots([protocol.Manifest]).Single();
        if (source.Parser != "hyg-csv")
        {
            throw new InvalidDataException($"{source.Id}: astronomical registry requires the pinned HYG CSV parser.");
        }

        if (acquire)
        {
            using var client = new HttpClient();
            await PronunciationAudit.AcquireAsync(client, source, cancellationToken);
        }

        PronunciationAudit.Verify(source);
        using var reader = new StreamReader(source.Path, new UTF8Encoding(false, true));
        var names = ReadNames(reader);
        var output = Resolve(protocol.OutputDirectory);
        _ = Directory.CreateDirectory(output);
        var namesPath = Path.Combine(output, "catalog-names.jsonl");
        PronunciationAudit.WriteLines(namesPath, names);
        var reportPath = Resolve(protocol.ReportPath);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        PronunciationAudit.WriteLines(reportPath,
        [
            new
            {
                protocol.SchemaVersion,
                protocol.AnalysisId,
                protocol.Frozen,
                ProtocolSha256 = PronunciationAudit.HashConfiguration(protocolPath),
                protocol.PronunciationPanelSha256,
                ManifestSha256 = PronunciationAudit.Hash(Resolve(protocol.Manifest)),
                Source = new
                {
                    source.Id,
                    source.Url,
                    source.Hash,
                    source.Revision,
                    source.License,
                    source.Citation,
                },
                CatalogNames = names.Length,
                DistinctMatchKeys = names.Select(name => name.MatchKey).Distinct(StringComparer.Ordinal).Count(),
                AmbiguousMatchKeys = names.Where(name => name.Ambiguous).Select(name => name.MatchKey)
                    .Distinct(StringComparer.Ordinal).Count(),
                ArtifactSha256 = PronunciationAudit.Hash(namesPath),
                SoftwareSha256 = PronunciationAudit.Hash(typeof(AstronomicalNameRegistry).Assembly.Location),
                Status = "registered_not_evaluated",
                protocol.Rules,
                Languages = panel.Profiles.OrderBy(profile => profile.Language, StringComparer.Ordinal).Select(profile => new
                {
                    profile.Language,
                    ProfileId = profile.Id,
                    ProfileSha256 = profile.Fingerprint,
                    Status = "not_evaluated",
                }),
            },
        ]);
    }

    public static AstronomicalName[] ReadNames(TextReader reader)
    {
        using var parser = new TextFieldParser(reader)
        {
            TextFieldType = FieldType.Delimited,
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false,
        };
        parser.SetDelimiters(",");
        var header = parser.ReadFields() ?? throw new InvalidDataException("Missing HYG CSV header.");
        var idIndex = Array.IndexOf(header, "id");
        var nameIndex = Array.IndexOf(header, "proper");
        if (idIndex < 0 || nameIndex < 0 || header.Distinct(StringComparer.Ordinal).Count() != header.Length)
        {
            throw new InvalidDataException("HYG CSV requires unique columns including id and proper.");
        }

        var names = new List<AstronomicalName>();
        var ids = new HashSet<int>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields() ?? throw new InvalidDataException("Missing HYG CSV row.");
            if (fields.Length != header.Length ||
                !int.TryParse(fields[idIndex], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
                !ids.Add(id))
            {
                throw new InvalidDataException($"Invalid or duplicate HYG CSV record near line {parser.LineNumber}.");
            }

            var original = fields[nameIndex];
            var key = original.Trim().Normalize(NormalizationForm.FormC);
            if (id != 0 && key.Length != 0)
            {
                names.Add(new AstronomicalName(id.ToString(CultureInfo.InvariantCulture), original, key));
            }
        }

        var counts = names.GroupBy(name => name.MatchKey, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        return [.. names.Select(name => name with { Ambiguous = counts[name.MatchKey] > 1 })
            .OrderBy(name => name.CatalogId, StringComparer.Ordinal)];
    }

    private string Resolve(string path) =>
        Path.GetFullPath(Path.Combine(repositoryRoot, path.Replace('/', Path.DirectorySeparatorChar)));
}
