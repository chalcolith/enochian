namespace Enochian.Math;

public sealed record DynamicTimeWarpResult(
    double Cost,
    int PathLength,
    int SourceLength,
    int TargetLength,
    IReadOnlyList<DynamicTimeWarpPathPoint>? Path)
{
    public double MeanPathCost => PathLength == 0 ? 0 : Cost / PathLength;

    public double MeanInputLengthCost => SourceLength == 0 && TargetLength == 0
        ? 0
        : Cost / ((SourceLength + TargetLength) / 2.0);
}

public sealed record DynamicTimeWarpPathPoint(
    int SourceIndex,
    int TargetIndex,
    DynamicTimeWarpStep Step);

public enum DynamicTimeWarpStep
{
    Match,
    Insertion,
    Deletion,
}
