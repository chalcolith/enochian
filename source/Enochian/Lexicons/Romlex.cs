using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Enochian.Flow;
using Enochian.Text;
using Newtonsoft.Json;

namespace Enochian.Lexicons
{
    public class Romlex : Lexicon
    {
        static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public Romlex(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }

        public override NLog.Logger Log => logger;

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            return base.Configure(config);
        }

        protected override void LoadLexicon(string path)
        {
            try
            {
                var encoder = new Encoder(Features, Encoding);
                RomlexLexicon lexicon;

                path = Path.GetFullPath(path);
                Log.Info("loading ROMLEX from {0}", path);

                var js = new JsonSerializer
                {
                    Formatting = Formatting.Indented,
                    NullValueHandling = NullValueHandling.Ignore,
                };
                using (var sr = new StreamReader(path))
                using (var jr = new JsonTextReader(sr))
                {
                    lexicon = js.Deserialize<RomlexLexicon>(jr);
                }

                int num = 0;
                var romLookup = lexicon.Entries.ToLookup(e => e.Lemma);
                foreach (var romEntry in romLookup)
                {
                    var lemma = romEntry.Key;
                    (_, _, var phones) = encoder.GetTextAndPhones(lemma);

                    var text = romEntry.Select(e => e.Entry).FirstOrDefault(t => t != lemma) ?? lemma;
                    var def = string.Join("\n", romEntry.Select(e => string.Format("{0}: {1} ({2})",
                        e.PartOfSpeech, e.Entry, lexicon.Languages.FirstOrDefault(l => l.Code == e.SrcLangCode)?.Name ?? "?")));

                    var entry = new LexiconEntry
                    {
                        Lemma = lemma,
                        Text = text,
                        Definition = def,
                        Phones = phones,
                    };

                    Entries.Add(entry);

                    if (!EntriesByLemma.ContainsKey(entry.Lemma))
                        EntriesByLemma.Add(entry.Lemma, entry);
                    else
                        AddError("duplicate lemma '{0}'", entry.Lemma);

                    if ((++num % 1000) == 0)
                        Log.Info("  loaded {0} entries", num);
                }
                Log.Info("loaded {0} total entries", num);
            }
            catch (Exception e)
            {
                AddError("unable to load ROMLX lexicon: {0}", e.Message);
            }
        }
    }

    public class RomlexLexicon
    {
        public string Created { get; set; }
        public IList<RomlexLanguage> Languages { get; set; }
        public IList<RomlexEntry> Entries { get; set; }
    }

    public class RomlexLanguage
    {
        public string Code { get; set; }
        public string Name { get; set; }
    }

    public class RomlexEntry
    {
        string entry;

        public string SrcLangCode { get; set; }
        public string DefLangCode { get; set; }

        public string Lemma { get; set; }
        public string Entry
        {
            get => entry ?? Lemma;
            set => entry = value;
        }
        public string PartOfSpeech { get; set; }
        public string Definition { get; set; }

        public override bool Equals(object obj)
        {
            var other = obj as RomlexEntry;
            if (other == null) return false;

            return SrcLangCode == other.SrcLangCode
                && DefLangCode == other.DefLangCode
                && Lemma == other.Lemma
                && Entry == other.Entry
                && PartOfSpeech == other.PartOfSpeech
                && Definition == other.Definition;
        }

        public override int GetHashCode()
        {
            var hash = base.GetHashCode();
            hash ^= SrcLangCode?.GetHashCode() ?? 0;
            hash ^= DefLangCode?.GetHashCode() ?? 0;
            hash ^= Lemma?.GetHashCode() ?? 0;
            hash ^= Entry?.GetHashCode() ?? 0;
            hash ^= PartOfSpeech?.GetHashCode() ?? 0;
            hash ^= Definition?.GetHashCode() ?? 0;
            return hash;
        }
    }
}
