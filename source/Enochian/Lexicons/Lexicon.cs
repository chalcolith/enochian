using Enochian.Flow;
using Enochian.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Enochian.Lexicons
{
    public abstract class Lexicon : Configurable
    {
        string lexiconPath;
        ICollection<LexiconEntry> entries;
        IDictionary<string, LexiconEntry> entriesByLemma;

        public Lexicon(IConfigurable parent, IFlowResources resources)
            : base(parent)
        {
            Resources = resources;
        }

        public IFlowResources Resources { get; private set; }

        public FeatureSet Features { get; private set; }
        public Encoding Encoding { get; private set; }

        public ICollection<LexiconEntry> Entries
        {
            get
            {
                EnsureLexiconLoaded();
                return entries;
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
                return entriesByLemma;
            }
            protected set
            {
                entriesByLemma = value;
            }
        }

        public int MaxEntriesToLoad { get; set; }

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            var debugLimit = config.Get<int?>("debugLimit", this);
            MaxEntriesToLoad = debugLimit ?? int.MaxValue;

            if (Resources != null)
            {
                var features = config.Get<string>("features", this);
                if (!string.IsNullOrWhiteSpace(features))
                {
                    Features = Resources.FeatureSets.FirstOrDefault(fs => fs.Id == features);
                    if (Features == null)
                        AddError("invalid feature set name '{0}'", features);
                }
                else
                {
                    AddError("no 'features' specified");
                }

                var encoding = config.Get<string>("encoding", this);
                if (!string.IsNullOrWhiteSpace(encoding))
                {
                    Encoding = Resources.Encodings.FirstOrDefault(enc => enc.Id == encoding);
                    if (Encoding == null)
                        AddError("invalid encoding name '{0}'", encoding);
                }
                else
                {
                    AddError("no 'encoding' specified");
                }

                var path = config.Get<string>("path", this);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    lexiconPath = path;
                }
                else
                {
                    AddError("invalid empty path");
                }
            }
            else
            {
                AddError("No Resources specified");
            }

            return this;
        }

        void EnsureLexiconLoaded()
        {
            if (entries != null)
                return;

            if (string.IsNullOrWhiteSpace(lexiconPath))
            {
                AddError("no lexicon path configured");
                return;
            }

            var absolutePath = GetChildPath(AbsoluteFilePath, lexiconPath);

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
                            cacheSuccessful = LoadCachedDictionary(cachedPath);
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
                    AddError("error loading '{0}': {1}", absolutePath, e.Message);
                }
            }
            else
            {
                AddError("invalid lexicon path '{0}'", absolutePath);
            }
        }

        protected abstract void LoadLexicon(string path);

        static readonly byte[] MagicCacheCookie = new Guid("{FF1B7C9F-FF3D-4718-BC92-91009A06BF85}").ToByteArray();

        bool LoadCachedDictionary(string path)
        {
            entries = new List<LexiconEntry>();

            Log.Info("reading cached dictionary {0}...", path);
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
            using (var br = new BinaryReader(fs))
            {
                byte[] cookie = br.ReadBytes(MagicCacheCookie.Length);
                if (!cookie.SequenceEqual(MagicCacheCookie))
                {
                    AddError("error reading cached dictionary {0}", path);
                    return false;
                }

                uint numEntries = br.ReadUInt32();
                for (uint i = 0; i < numEntries; i++)
                {
                    string text = br.ReadString();
                    string lemma = br.ReadString();
                    string encoded = br.ReadString();
                    ushort numPhones = br.ReadUInt16();
                    double[][] phones = new double[numPhones][];
                    for (ushort j = 0; j < numPhones; j++)
                    {
                        ushort numFeatures = br.ReadUInt16();
                        phones[j] = new double[numFeatures];
                        for (ushort k = 0; k < numFeatures; k++)
                            phones[j][k] = br.ReadDouble();
                    }
                    entries.Add(new LexiconEntry
                    {
                        Text = text,
                        Lemma = lemma,
                        Encoded = encoded,
                        Phones = phones,
                    });
                }
            }

            entriesByLemma = entries.ToDictionary(entry => entry.Lemma);

            Log.Info("read {0} total entries", entries.Count);
            return true;
        }

        void SaveCachedDictionary(string path)
        {
            if (entries == null)
                return;

            Log.Info("saving cached dictionary {0}", path);
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

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
                    if (entry.Phones != null)
                    {
                        bw.Write((ushort)entry.Phones.Count);
                        foreach (var phone in entry.Phones)
                        {
                            bw.Write((ushort)phone.Length);
                            foreach (var feature in phone)
                                bw.Write(feature);
                        }
                    }
                    else
                    {
                        bw.Write((ushort)0);
                    }
                }
            }

            Log.Info("saved {0} total entries", entries.Count);
        }
    }

    public class LexiconEntry
    {
        public string Text { get; set; }
        public string Lemma { get; set; }
        public string Encoded { get; set; }
        public IList<double[]> Phones { get; set; }
    }
}
