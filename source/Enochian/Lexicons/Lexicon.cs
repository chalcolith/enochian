using Enochian.Flow;
using Enochian.Text;

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

    public IDictionary<string, LexiconEntry> EntriesByLemma
    {
        get
        {
            EnsureLexiconLoaded();
            return field ??= new Dictionary<string, LexiconEntry>();
        }
        protected set;
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
                    var cachedPath = Path.Combine(".", CacheDir, Path.GetFileName(absolutePath) + ".bin");
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

    // change this if the binary format changes
    private static readonly byte[] MagicCacheCookie = new Guid("{B2A4E9EB-5178-41B4-935A-47070BCBF37D}").ToByteArray();

    private bool LoadCachedDictionary(string path)
    {
        entries = [];

        Log.LogInformation("reading cached dictionary {Path}...", path);
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
        using (var br = new BinaryReader(fs))
        {
            byte[] cookie = br.ReadBytes(MagicCacheCookie.Length);
            if (!cookie.SequenceEqual(MagicCacheCookie))
            {
                _ = AddError("error reading cached dictionary {0}", path);
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
                entries.Add(new LexiconEntry
                {
                    Lexicon = this,
                    Text = text,
                    Lemma = lemma,
                    Encoded = encoded,
                    Definition = definition,
                    Phones = phones,
                });
            }
        }

        EntriesByLemma = entries.ToDictionary(entry => entry.Lemma);

        Log.LogInformation("read {Count} total entries", entries.Count);
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

        using (var fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write))
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
            }
        }

        Log.LogInformation("saved {Count} total entries", entries.Count);
    }
}

public class LexiconEntry
{
    public Lexicon? Lexicon { get; set; }
    public string Text { get; set; } = string.Empty;
    public string Lemma { get; set; } = string.Empty;
    public string Encoded { get; set; } = string.Empty;
    public string Definition { get; set; } = string.Empty;
    public IList<double[]> Phones { get; set; } = [];
}
