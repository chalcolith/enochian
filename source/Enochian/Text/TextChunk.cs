using System;
using System.Collections.Generic;
using Enochian.Text;
using Enochian.Flow;
using Enochian.Lexicons;

namespace Enochian.Text
{
    public class TextChunk
    {
        public string Description { get; set; }
        public IList<TextLine> Lines { get; set; }
    }

    public class TextLine
    {
        string text = null;

        public IFlowStep<TextChunk> SourceStep { get; set; }
        public TextLine SourceLine { get; set; }
        public string Text
        {
            get { return text ?? SourceLine?.Text; }
            set { text = value; }
        }
        public IList<TextSegment> Segments { get; set; }
    }

    public class TextSegment
    {
        public string Text { get; set; }
        public IList<TextSegment> SourceSegments { get; set; }
        public IList<SegmentOption> Options { get; set; }
    }

    [Flags]
    public enum TextTag
    {
        None   = 0,
        Repr   = 1 << 0,
        Hypo   = 1 << 1,
        Match  = 1 << 2,
    }

    public class SegmentOption
    {
        Encoding encoding;

        public TextTag Tags { get; set;}
        public LexiconEntry Entry { get; set; }
        public Encoding Encoding
        {
            get => encoding ?? Entry?.Lexicon?.Encoding;
            set => encoding = value;
        }
        public string Text { get; set; }
        public IList<double[]> Phones { get; set; }
    }

    public class OptionComparer : IComparer<SegmentOption>
    {
        public int Compare(SegmentOption x, SegmentOption y)
        {
            if (x == null) throw new ArgumentNullException(nameof(x));
            if (y == null) throw new ArgumentNullException(nameof(y));

            int result = Ordinal(x) - Ordinal(y);
            return result;
        }

        int Ordinal(SegmentOption opt)
        {
            if ((opt.Tags & TextTag.Repr) != TextTag.None)
                return 1;
            if ((opt.Tags & TextTag.Match) != TextTag.None)
                return 2;
            return 0;
        }
    }
}
