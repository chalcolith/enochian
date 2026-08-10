using Enochian.Flow;
using Enochian.Text;
using System.Security.Cryptography;

namespace Enochian.Lexicons;

public abstract class Lexicon(IConfigurable parent, IFlowResources resources) : Configurable(parent)
{
    private static readonly Lock loadLock = new();
    private ICollection<LexiconEntry>? entries;

    public IFlowResources Resources { get; private set; } = resources;

    public FeatureSet? Features { get; private set; }
    public Encoding? Encoding { get; private set; }
    public string? SourcePath { get; private set; }

    public ICollection<LexiconEntry> Entries
    {
        get
        {
            EnsureLexiconLoaded();
            return entries ?? [];
        }
        protected set
        {
            entries = value;
        }
    }

    public IReadOnlyDictionary<string, IReadOnlyList<LexiconEntry>> EntriesByLemma
    {
        get
        {
            EnsureLexiconLoaded();
            return field ??= new Dictionary<string, IReadOnlyList<LexiconEntry>>();
        }
        private set;
    }

    public LexiconEntry? GetEntryByLemma(string lemma)
    {
        return EntriesByLemma.TryGetValue(lemma, out var matches) ? matches[0] : null;
    }

    public int MaxEntriesToLoad { get; set; }

    public override IConfigurable Configure(JsonObject config)
    {
        _ = base.Configure(config);

        var debugLimit = config.Get<int?>("debugLimit", this);
        MaxEntriesToLoad = debugLimit ?? int.MaxValue;

        if (Resources != null)
        {
            var features = config.Get<string>("features", this);
            if (!string.IsNullOrWhiteSpace(features))
            {
                Features = Resources.FeatureSets.FirstOrDefault(fs => fs.Id == features);
                if (Features == null)
                {
                    _ = AddError("invalid feature set name '{0}'", features);
                }
            }
            else
            {
                _ = AddError("no 'features' specified");
            }

            var encoding = config.Get<string>("encoding", this);
            if (!string.IsNullOrWhiteSpace(encoding))
            {
                Encoding = Resources.Encodings.FirstOrDefault(enc => enc.Id == encoding);
                if (Encoding == null)
                {
                    _ = AddError("invalid encoding name '{0}'", encoding);
                }
            }
            else
            {
                _ = AddError("no 'encoding' specified");
            }

            var path = config.Get<string>("path", this);
            if (!string.IsNullOrWhiteSpace(path))
            {
                SourcePath = path;
            }
            else
            {
                _ = AddError("invalid empty path");
            }
        }
        else
        {
            _ = AddError("No Resources specified");
        }

        return this;
    }

    private void EnsureLexiconLoaded()
    {
        if (entries != null)
        {
            return;
        }

        lock (loadLock)
        {
            if (entries != null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(SourcePath))
            {
                _ = AddError("no lexicon path configured");
                return;
            }

            var absolutePath = GetChildPath(AbsoluteFilePath, SourcePath);

            if (File.Exists(absolutePath))
            {
                try
                {
                    bool cacheSuccessful = false;
                    var cachedPath = GetCachePath(absolutePath);
                    if (File.Exists(cachedPath))
                    {
                        var origInfo = new FileInfo(absolutePath);
                        var cacheInfo = new FileInfo(cachedPath);
                        if (cacheInfo.LastWriteTimeUtc > origInfo.LastWriteTimeUtc)
                        {
                            try
                            {
                                cacheSuccessful = LoadCachedDictionary(cachedPath);
                            }
                            catch
                            {
                                cacheSuccessful = false;
                            }
                        }
                    }

                    if (!cacheSuccessful)
                    {
                        LoadLexicon(absolutePath);
                        SaveCachedDictionary(cachedPath);
                    }
                }
                catch (Exception e)
                {
                    _ = AddError("error loading '{0}': {1}", absolutePath, e.Message);
                }
            }
            else
            {
                _ = AddError("invalid lexicon path '{0}'", absolutePath);
            }
        }
    }

    protected abstract void LoadLexicon(string path);

    protected void SetEntries(IEnumerable<LexiconEntry> loadedEntries)
    {
        var preparedEntries = loadedEntries.ToList();
        foreach (var entry in preparedEntries)
        {
            entry.Lexicon = this;
            entry.Language = string.IsNullOrWhiteSpace(entry.Language) ? "und" : entry.Language;
            entry.Family = string.IsNullOrWhiteSpace(entry.Family) ? "unknown" : entry.Family;
            entry.SourceId = string.IsNullOrWhiteSpace(entry.SourceId) ? Id ?? GetType().Name : entry.SourceId;
            entry.SourceRecordId = string.IsNullOrWhiteSpace(entry.SourceRecordId) ? entry.Lemma : entry.SourceRecordId;
            entry.Form = string.IsNullOrWhiteSpace(entry.Form) ? entry.Text : entry.Form;
            entry.SourceEncoding = string.IsNullOrWhiteSpace(entry.SourceEncoding) ? Encoding?.Id ?? string.Empty : entry.SourceEncoding;
        }

        var orderedEntries = preparedEntries
            .OrderBy(entry => entry.Lemma, StringComparer.Ordinal)
            .ThenBy(entry => entry.SourceRecordId, StringComparer.Ordinal)
            .ThenBy(entry => entry.Text, StringComparer.Ordinal)
            .ThenBy(entry => entry.Encoded, StringComparer.Ordinal)
            .ThenBy(entry => entry.Definition, StringComparer.Ordinal)
            .ToList();

        var generatedIdCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in orderedEntries)
        {
            if (string.IsNullOrWhiteSpace(entry.EntryId))
            {
                var idBase = string.Join(":", entry.SourceId, entry.SourceRecordId);
                _ = generatedIdCounts.TryGetValue(idBase, out var ordinal);
                generatedIdCounts[idBase] = ++ordinal;
                entry.EntryId = ordinal == 1 ? idBase : string.Join(":", idBase, ordinal);
            }
        }

        entries = orderedEntries;
        EntriesByLemma = orderedEntries
            .GroupBy(entry => entry.Lemma, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<LexiconEntry>)[.. group],
                StringComparer.Ordinal);
    }

    // change this if the binary format changes
    private static readonly byte[] MagicCacheCookie = new Guid("{14880838-E56B-4954-B746-43616E98A90D}").ToByteArray();

    private string GetCachePath(string absolutePath)
    {
        var identity = string.Join("\n", GetType().FullName, Id, Path.GetFullPath(absolutePath), Features?.Id, Encoding?.Id, MaxEntriesToLoad);
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return Path.Combine(".", CacheDir, Path.GetFileName(absolutePath) + "." + hash[..16] + ".bin");
    }

    private bool LoadCachedDictionary(string path)
    {
        List<LexiconEntry> cachedEntries = [];

        Log.LogInformation("reading cached dictionary {Path}...", path);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
        using (var br = new BinaryReader(fs))
        {
            byte[] cookie = br.ReadBytes(MagicCacheCookie.Length);
            if (!cookie.SequenceEqual(MagicCacheCookie))
            {
                Log.LogInformation("cached dictionary {Path} uses an unsupported format and will be rebuilt", path);
                return false;
            }

            uint numEntries = br.ReadUInt32();
            for (uint i = 0; i < numEntries; i++)
            {
                string text = br.ReadString();
                string lemma = br.ReadString();
                string encoded = br.ReadString();
                string definition = br.ReadString();
                ushort numPhones = br.ReadUInt16();
                double[][] phones = new double[numPhones][];
                for (ushort j = 0; j < numPhones; j++)
                {
                    ushort numFeatures = br.ReadUInt16();
                    phones[j] = new double[numFeatures];
                    for (ushort k = 0; k < numFeatures; k++)
                    {
                        phones[j][k] = br.ReadDouble();
                    }
                }
                cachedEntries.Add(new LexiconEntry
                {
                    Lexicon = this,
                    EntryId = br.ReadString(),
                    Language = br.ReadString(),
                    Family = br.ReadString(),
                    SourceId = br.ReadString(),
                    SourceRecordId = br.ReadString(),
                    Text = text,
                    Lemma = lemma,
                    Form = br.ReadString(),
                    EntryKind = (LexiconEntryKind)br.ReadByte(),
                    Dialect = ReadNullableString(br),
                    PartOfSpeech = ReadNullableString(br),
                    Frequency = ReadNullableDouble(br),
                    SourceEncoding = br.ReadString(),
                    Ipa = ReadNullableString(br),
                    Encoded = encoded,
                    Definition = definition,
                    Phones = phones,
                });
            }
        }

        SetEntries(cachedEntries);

        Log.LogInformation("read {Count} total entries", cachedEntries.Count);
        return true;
    }

    private void SaveCachedDictionary(string path)
    {
        if (entries == null)
        {
            return;
        }

        Log.LogInformation("saving cached dictionary {Path}", path);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            _ = Directory.CreateDirectory(dir);
        }

        var tempPath = path + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp";
        try
        {
            using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(MagicCacheCookie);
                bw.Write((uint)entries.Count);
                foreach (var entry in entries)
                {
                    bw.Write(entry.Text ?? "");
                    bw.Write(entry.Lemma ?? "");
                    bw.Write(entry.Encoded ?? "");
                    bw.Write(entry.Definition ?? "");
                    if (entry.Phones != null)
                    {
                        bw.Write((ushort)entry.Phones.Count);
                        foreach (var phone in entry.Phones)
                        {
                            bw.Write((ushort)phone.Length);
                            foreach (var feature in phone)
                            {
                                bw.Write(feature);
                            }
                        }
                    }
                    else
                    {
                        bw.Write((ushort)0);
                    }

                    bw.Write(entry.EntryId ?? "");
                    bw.Write(entry.Language ?? "");
                    bw.Write(entry.Family ?? "");
                    bw.Write(entry.SourceId ?? "");
                    bw.Write(entry.SourceRecordId ?? "");
                    bw.Write(entry.Form ?? "");
                    bw.Write((byte)entry.EntryKind);
                    WriteNullableString(bw, entry.Dialect);
                    WriteNullableString(bw, entry.PartOfSpeech);
                    WriteNullableDouble(bw, entry.Frequency);
                    bw.Write(entry.SourceEncoding ?? "");
                    WriteNullableString(bw, entry.Ipa);
                }
            }

            File.Move(tempPath, path, true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        Log.LogInformation("saved {Count} total entries", entries.Count);
    }

    private static string? ReadNullableString(BinaryReader reader)
    {
        return reader.ReadBoolean() ? reader.ReadString() : null;
    }

    private static double? ReadNullableDouble(BinaryReader reader)
    {
        return reader.ReadBoolean() ? reader.ReadDouble() : null;
    }

    private static void WriteNullableString(BinaryWriter writer, string? value)
    {
        writer.Write(value != null);
        if (value != null)
        {
            writer.Write(value);
        }
    }

    private static void WriteNullableDouble(BinaryWriter writer, double? value)
    {
        writer.Write(value.HasValue);
        if (value.HasValue)
        {
            writer.Write(value.Value);
        }
    }
}

public enum LexiconEntryKind : byte
{
    Lemma,
    Inflected,
    ProperName,
    Abbreviation,
}

public class LexiconEntry
{
    public Lexicon? Lexicon { get; set; }
    public string EntryId { get; set; } = string.Empty;
    public string Language { get; set; } = "und";
    public string Family { get; set; } = "unknown";
    public string SourceId { get; set; } = string.Empty;
    public string SourceRecordId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string Lemma { get; set; } = string.Empty;
    public string Form { get; set; } = string.Empty;
    public LexiconEntryKind EntryKind { get; set; }
    public string? Dialect { get; set; }
    public string? PartOfSpeech { get; set; }
    public double? Frequency { get; set; }
    public string SourceEncoding { get; set; } = string.Empty;
    public string? Ipa { get; set; }
    public string Encoded { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
    public IList<double[]> Phones { get; set; } = [];
}
