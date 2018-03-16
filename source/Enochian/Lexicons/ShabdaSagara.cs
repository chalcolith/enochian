using Enochian.Flow;
using Enochian.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Enochian.Lexicons
{
    public class ShabdaSagara : Lexicon
    {
        static readonly NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();

        public ShabdaSagara(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }

        public override NLog.Logger Log => logger;

        public override IConfigurable Configure(IDictionary<string, object> config)
        {
            return base.Configure(config);
        }

        static readonly Regex LemmaLineRegex = new Regex(@"<L>(\d+).*<k1>(.*)<k2>", RegexOptions.Compiled);
        static readonly Regex FirstLineRegex = new Regex(@"(.*)¦\s+(\S+)\s+(.*)", RegexOptions.Compiled);
        static readonly Regex MidLineRegex = new Regex(@"<>(.*)", RegexOptions.Compiled);
        static readonly Regex InlineRegex = new Regex(@"{#([^#]+)#}", RegexOptions.Compiled);

        protected override void LoadLexicon(string path)
        {
            try
            {
                path = Path.GetFullPath(path);
                Log.Info("loading SHS from {0}", path);

                var encoder = new Encoder(Features, Encoding);
                var entries = new List<LexiconEntry>();
                var entriesByLemma = new Dictionary<string, LexiconEntry>();

                int num = 0;
                LexiconEntry currentEntry = null;
                using (var sr = new StreamReader(path))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        var match = LemmaLineRegex.Match(line);
                        if (match.Success)
                        {
                            if (currentEntry != null && !string.IsNullOrWhiteSpace(currentEntry.Lemma) && !string.IsNullOrWhiteSpace(currentEntry.Definition))
                            {
                                if (entriesByLemma.TryGetValue(currentEntry.Lemma, out LexiconEntry existingEntry))
                                {
                                    existingEntry.Definition = existingEntry.Definition + "\n\n" + currentEntry.Definition;
                                }
                                else
                                {
                                    entries.Add(currentEntry);
                                    entriesByLemma[currentEntry.Lemma] = currentEntry;
                                }

                                if ((++num % 1000) == 0)
                                    Log.Info("  loaded {0} entries", num);
                            }

                            string lemmaSlp1 = match.Groups[2].Value;
                            (string text, string lemma, IList<double[]> phones) = encoder.GetTextAndPhones(lemmaSlp1);
                            currentEntry = new LexiconEntry
                            {
                                Lexicon = this,
                                Lemma = lemma,
                                Text = text,
                                Definition = "(" + lemmaSlp1 + ") ",
                                Phones = phones,
                            };
                        }
                        else if (currentEntry != null)
                        {
                            if ((match = FirstLineRegex.Match(line)).Success)
                            {
                                currentEntry.Definition = currentEntry.Definition
                                    + ReplaceSlp1(encoder, match.Groups[2].Value) + " "
                                    + ReplaceSlp1(encoder, match.Groups[3].Value);
                            }
                            else if ((match = MidLineRegex.Match(line)).Success)
                            {
                                currentEntry.Definition = currentEntry.Definition + " " 
                                    + ReplaceSlp1(encoder, match.Groups[1].Value);
                            }
                        }
                    }

                    if (currentEntry != null)
                    {
                        if (entriesByLemma.TryGetValue(currentEntry.Lemma, out LexiconEntry existingEntry))
                        {
                            existingEntry.Definition = existingEntry.Definition + "\n" + currentEntry.Definition;
                        }
                        else
                        {
                            entries.Add(currentEntry);
                            entriesByLemma[currentEntry.Lemma] = currentEntry;
                        }
                    }
                }

                Log.Info("loaded {0} entries from SHS", num);

                Entries = entries;
                EntriesByLemma = entriesByLemma;
            }
            catch (Exception e)
            {
                AddError("unable to load SHS lexicon: {0}", e.Message);
            }
        }

        string ReplaceSlp1(Encoder encoder, string str)
        {
            var match = InlineRegex.Match(str);
            while (match.Success)
            {
                (_, string devanagari, _) = encoder.GetTextAndPhones(match.Groups[1].Value);
                str = str.Substring(0, match.Index)
                    + devanagari + " (" + match.Groups[1].Value + ")"
                    + str.Substring(match.Index + match.Length);

                match = InlineRegex.Match(str);
            }
            return str;
        }
    }
}
