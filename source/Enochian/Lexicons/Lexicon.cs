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
                LoadLexicon(absolutePath);
            else
                AddError("invalid lexicon path '{0}'", absolutePath);
        }

        protected abstract void LoadLexicon(string path);
    }

    public class LexiconEntry
    {
        public string Lemma { get; set; }
        public string Encoded { get; set; }
        public IList<double[]> Vectors { get; set; }
    }
}
