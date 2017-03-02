﻿using Enochian.Flow;
using Enochian.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Enochian.Lexicon
{
    public class CMUDict : Lexicon
    {
        public CMUDict(IConfigurable parent, IFlowResources resources)
            : base(parent, resources)
        {
        }

        static readonly char[] WS = new[] { ' ', '\t' };

        protected override void LoadLexicon(string path)
        {
            try
            {
                var encoder = new Encoder(Features, Encoding);

                Entries = new List<LexiconEntry>();
                EntriesByLemma = new Dictionary<string, LexiconEntry>();

                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                using (var sr = new StreamReader(fs, System.Text.Encoding.ASCII))
                {
                    string line;
                    while ((line = sr.ReadLine()) != null)
                    {
                        if (line.StartsWith(";;;") || string.IsNullOrWhiteSpace(line))
                            continue;

                        var tokens = line.Split(WS, StringSplitOptions.RemoveEmptyEntries);
                        if (tokens.Length < 2)
                            continue;

                        var sb = new System.Text.StringBuilder();
                        var lemma = tokens[0].ToUpperInvariant();
                        var vectors = tokens.Skip(1).SelectMany(t =>
                        {
                            sb.Append(t);
                            var input = new Segment { Text = t };
                            var result = encoder.ProcessSegment(input);
                            return result.Vectors;
                        })
                        .ToArray();

                        var entry = new LexiconEntry
                        {
                            Lemma = lemma,
                            Encoded = sb.ToString(),
                            Vectors = vectors,
                        };

                        Entries.Add(entry);
                        if (!EntriesByLemma.ContainsKey(lemma))
                        {
                            EntriesByLemma.Add(lemma, entry);
                        }
                        else
                        {
                            AddError("duplicate lemma '{0}'", lemma);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                AddError("unable to load CMUDICT lexicon: {0}", e.Message);
            }
        }
    }
}
