using System;
using System.Collections.Generic;
using System.Text;

namespace Enochian.Lexicons
{
    public class Romlex
    {
    }

    public class RomlexLexicon
    {
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
        public string SrcLangCode { get; set; }
        public string DefLangCode { get; set; }

        public string Lemma { get; set; }
        public string PartOfSpeech { get; set; }
        public string Definition { get; set; }
    }
}
