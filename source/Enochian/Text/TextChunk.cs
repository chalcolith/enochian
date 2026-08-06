using Enochian.Flow;
using Enochian.Lexicons;

namespace Enochian.Text;

public class TextChunk
{
    public string? Description { get; set; }
    public IList<TextLine> Lines { get; set; } = [];
}

public class TextLine
{
    public IFlowStep<TextChunk>? SourceStep { get; set; }
    public TextLine? SourceLine { get; set; }
    public string? Text
    {
        get { return field ?? SourceLine?.Text; }
        set;
    }
    public IList<TextSegment> Segments { get; set; } = [];
}

public class TextSegment
{
    public string? Text { get; set; }
    public IList<TextSegment> SourceSegments { get; set; } = [];
    public IList<SegmentOption> Options { get; set; } = [];
}

[Flags]
public enum TextTag
{
    None = 0,
    Repr = 1 << 0,
    Hypo = 1 << 1,
    Match = 1 << 2,
}

public class SegmentOption
{
    public TextTag Tags { get; set; }
    public LexiconEntry? Entry { get; set; }
    public Encoding? Encoding
    {
        get => field ?? Entry?.Lexicon?.Encoding;
        set;
    }
    public string? Text { get; set; }
    public IList<double[]> Phones { get; set; } = [];
}

public class OptionComparer : IComparer<SegmentOption>
{
    public int Compare(SegmentOption? x, SegmentOption? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        int result = Ordinal(x) - Ordinal(y);
        return result;
    }

    private static int Ordinal(SegmentOption opt)
    {
        if ((opt.Tags & TextTag.Repr) != TextTag.None)
        {
            return 1;
        }

        if ((opt.Tags & TextTag.Match) != TextTag.None)
        {
            return 2;
        }

        return 0;
    }
}
