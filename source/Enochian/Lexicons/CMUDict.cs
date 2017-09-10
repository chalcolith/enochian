using Enochian.Flow;
using Enochian.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Enochian.Lexicons
{
    public class CMUDict : Lexicon
    {
        static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public CMUDict(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }

        public override NLog.Logger Log => logger;

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            return base.Configure(config);
        }

        static readonly char[] WS = new[] { ' ', '\t' };

        protected override void LoadLexicon(string path)
        {
            try
            {
                var encoder = new Encoder(Features, Encoding);

                var entries = new List<LexiconEntry>();
                var entriesByLemma = new Dictionary<string, LexiconEntry>();

                path = Path.GetFullPath(path);
                Log.Info("loading CMUDICT from {0}", path);

                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var sr = new StreamReader(fs, System.Text.Encoding.ASCII))
                {
                    string line;
                    int num = 0;
                    while ((line = sr.ReadLine()) != null && num++ < MaxEntriesToLoad)
                    {
                        if (line.StartsWith(";;;") || string.IsNullOrWhiteSpace(line))
                            continue;

                        var tokens = line.Split(WS, StringSplitOptions.RemoveEmptyEntries);
                        if (tokens.Length < 2)
                            continue;

                        var sb = new System.Text.StringBuilder();
                        var lemma = tokens[0].ToUpperInvariant();
                        var phones = tokens
                            .Skip(1)
                            .SelectMany(t =>
                            {
                                sb.Append(t);
                                var input = new TextSegment
                                {
                                    Options = new[] { new SegmentOption { Text = t } }
                                };
                                var result = encoder.ProcessSegment(input);
                                return result.Options.SelectMany(o => o.Phones);
                            })
                            .ToArray();

                        var entry = new LexiconEntry
                        {
                            Text = tokens[0],
                            Lemma = lemma,
                            Encoded = sb.ToString(),
                            Phones = phones,
                        };

                        entries.Add(entry);
                        if (!entriesByLemma.ContainsKey(lemma))
                        {
                            entriesByLemma.Add(lemma, entry);
                        }
                        else
                        {
                            AddError("duplicate lemma '{0}'", lemma);
                        }

                        if ((num % 1000) == 0)
                            Log.Info("  loaded {0} entries", num);
                    }
                    Log.Info("loaded {0} total entries", num);
                }

                Entries = entries;
                EntriesByLemma = entriesByLemma;
            }
            catch (Exception e)
            {
                AddError("unable to load CMUDICT lexicon: {0}", e.Message);
            }
        }
    }
}
