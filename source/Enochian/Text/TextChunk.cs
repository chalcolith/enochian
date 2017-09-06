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

    public class SegmentOption
    {
        Encoding encoding;

        public Lexicon Lexicon { get; set; }
        public LexiconEntry Entry { get; set; }
        public Encoding Encoding
        {
            get { return encoding ?? Lexicon?.Encoding; }
            set { encoding = value; }
        }
        public string Text { get; set; }
        public IList<double[]> Phones { get; set; }
    }
}
