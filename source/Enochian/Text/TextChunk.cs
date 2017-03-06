using System;
using System.Collections.Generic;
using Enochian.Text;
using Enochian.Flow;
using Enochian.Lexicons;

namespace Enochian.Text
{
    public class TextChunk
    {
        public IList<Interline> Lines { get; set; }
    }

    public class Interline
    {
        public FlowStep SourceStep { get; set; }
        public string Text { get; set; }
        public Encoding Encoding { get; set; }
        public IList<Segment> Segments { get; set; }
    }

    public class Segment
    {
        public Segment SourceSegment { get; set; }

        public Lexicon Lexicon { get; set; }
        public LexiconEntry Entry { get; set; }

        public string Text { get; set; }
        public IList<double[]> Vectors { get; set; }
    }
}
