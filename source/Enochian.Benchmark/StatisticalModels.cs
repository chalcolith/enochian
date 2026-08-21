namespace Enochian.Benchmark;

public sealed record ScoreCalibration(
    double? EmpiricalPercentile,
    double? StandardizedScore,
    int NullCount,
    double? NullMean,
    double? NullStandardDeviation,
    string? Diagnostic);

public sealed record PairedValue(string QueryId, double Target, double Control, int Weight = 1)
{
    public double Difference => Target - Control;
}

public sealed record PermutationResult(
    double Estimate,
    double PValue,
    string Alternative,
    int RandomizationCount,
    bool Exact);

public sealed record BootstrapInterval(
    double Lower,
    double Upper,
    double ConfidenceLevel,
    int BootstrapCount);

public sealed record AdjustedPValue(
    string ContrastId,
    double PValue,
    double AdjustedPValueValue,
    int FamilySize);

public sealed record CalibratedScoreRow(
    string SchemaVersion,
    string StatisticsId,
    string AnalysisId,
    string AnalysisMode,
    string SampleId,
    int RequestedSize,
    int Repetition,
    string QueryId,
    int QueryLength,
    string Section,
    string FrequencyBand,
    int Weight,
    string Language,
    string Family,
    string CalibrationNullKind,
    double RawDistance,
    double? EmpiricalPercentile,
    double? StandardizedScore,
    int NullCount,
    double? NullMean,
    double? NullStandardDeviation);

public sealed record StatisticalEstimateRow(
    string SchemaVersion,
    string StatisticsId,
    string AnalysisId,
    string AnalysisMode,
    int RequestedSize,
    string Scope,
    string Stratum,
    string Metric,
    string? ContrastId,
    string? GroupId,
    double Estimate,
    int QueryCount,
    int SampleCount);

public sealed record StatisticalIntervalRow(
    string SchemaVersion,
    string StatisticsId,
    string AnalysisId,
    string AnalysisMode,
    int RequestedSize,
    string ContrastId,
    string Metric,
    double Lower,
    double Upper,
    double ConfidenceLevel,
    int BootstrapCount,
    string ResamplingUnit);

public sealed record StatisticalTestRow(
    string SchemaVersion,
    string StatisticsId,
    string AnalysisId,
    string AnalysisMode,
    int RequestedSize,
    string ContrastId,
    bool Primary,
    string Metric,
    double Estimate,
    double PValue,
    string Alternative,
    int ConfiguredRandomizationCount,
    int RandomizationCount,
    bool Exact,
    string PlannedCorrectionMethod,
    int QueryCount);

public sealed record AdjustedPValueRow(
    string SchemaVersion,
    string StatisticsId,
    string AnalysisId,
    string AnalysisMode,
    int RequestedSize,
    string ContrastId,
    double PValue,
    double AdjustedPValue,
    string CorrectionMethod,
    int FamilySize);

public sealed record StatisticalDiagnosticRow(
    string SchemaVersion,
    string StatisticsId,
    string Severity,
    string Code,
    string Message,
    string? AnalysisId,
    string? AnalysisMode,
    int? RequestedSize,
    string? SampleId,
    string? QueryId,
    string? Language,
    string? ContrastId);

public sealed record StatisticalAnalysisResult(
    IReadOnlyList<CalibratedScoreRow> CalibratedScores,
    IReadOnlyList<StatisticalEstimateRow> Estimates,
    IReadOnlyList<StatisticalIntervalRow> Intervals,
    IReadOnlyList<StatisticalTestRow> Tests,
    IReadOnlyList<AdjustedPValueRow> AdjustedPValues,
    IReadOnlyList<StatisticalDiagnosticRow> Diagnostics);
