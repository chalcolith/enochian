using Enochian.Flow;
using Enochian.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Enochian.Lexicon
{
    public abstract class Lexicon : Configurable
    {
        public Lexicon(IConfigurable parent, IFlowResources resources)
            : base(parent)
        {
            Resources = resources;
        }

        public IFlowResources Resources { get; private set; }

        public FeatureSet Features { get; private set; }
        public Encoding Encoding { get; private set; }

        public ICollection<LexiconEntry> Entries { get; protected set; }
        public IDictionary<string, LexiconEntry> EntriesByLemma { get; protected set; }

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            base.Configure(config);

            if (Resources != null)
            {
                var features = config.Get<string>("features", this);
                Features = Resources.FeatureSets.FirstOrDefault(fs => fs.Id == features);
                if (Features == null)
                    AddError("invalid feature set name '{0}'", features);

                var encoding = config.Get<string>("encoding", this);
                Encoding = Resources.Encodings.FirstOrDefault(enc => enc.Id == encoding);
                if (Encoding == null)
                    AddError("invalid encoding name '{0}'", encoding);

                var path = config.Get<string>("path", this);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    var absolutePath = GetChildPath(AbsoluteFilePath, path);

                    if (File.Exists(absolutePath))
                        LoadLexicon(absolutePath);
                    else
                        AddError("invalid data path {0}", absolutePath);
                }
                else
                {
                    AddError("invalid empty path");
                }
            }
            else
            {
                AddError("No Resources specified for lexicon.");
            }

            return this;
        }

        protected abstract void LoadLexicon(string path);
    }

    public class LexiconEntry
    {
        public string Lemma { get; set; }
        public string Encoded { get; set; }
        public IEnumerable<double[]> Vectors { get; set; }
    }
}
