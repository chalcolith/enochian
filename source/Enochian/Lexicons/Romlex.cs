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
