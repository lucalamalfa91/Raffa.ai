using Raffa.JevBenchmark.Tests.Harness;
using Raffa.JevBenchmark.Tests.Metrics;

namespace Raffa.JevBenchmark.Tests.Reporting;

public sealed record UsageSummary(
    int FoundryCalls,
    int FoundryInputTokens,
    int FoundryOutputTokens,
    int JevHttpAttempts,
    int JevHttpFailures,
    int JevInputTokens,
    int JevOutputTokens,
    double? JevReportedCostUsd,
    double? FoundryEstimatedCostUsd,
    double? TotalCostUsd,
    double? CostPer1000Calls,
    bool CostComplete,
    double MeanTokensPerCall);

/// <summary>Run-to-run stability: the same input repeated <c>Repetitions</c> times.</summary>
public sealed record StabilitySummary(int CasesMeasured, double MeanConsistency, int CasesWithFlips, IReadOnlyList<string> FlippedCaseIds);

public sealed record ArmSummary(
    string Arm,
    bool JevEnabled,
    int Calls,
    int Errors,
    int Timeouts,
    double ErrorRate,
    double TimeoutRate,
    int Fallbacks,
    double FallbackRate,
    int FallbackJevError,
    int FallbackLowConfidence,
    int ServedByJev,
    double StrictAccuracy,
    double AnsweredAccuracy,
    double MacroF1,
    IReadOnlyList<ClassMetrics> PerClass,
    IReadOnlyList<string> ConfusionLabels,
    int[][] Confusion,
    LatencyStats Latency,
    UsageSummary Usage,
    StabilitySummary Stability);

public sealed record McNemarSummary(int BothCorrect, int OnlyBaselineCorrect, int OnlyJevCorrect, int BothWrong, double PValue);

public sealed record Verdict(
    string Quality,
    string Latency,
    string Cost,
    string Summary,
    IReadOnlyList<string> Warnings);

public sealed record ComparisonSummary(
    int CasesCompared,
    int PairedCalls,
    double Agreement,
    double Kappa,
    double BaselineCaseAccuracy,
    double JevCaseAccuracy,
    Interval AccuracyDelta,
    McNemarSummary McNemar,
    IReadOnlyList<string> FixedByJev,
    IReadOnlyList<string> RegressedByJev,
    double P50DeltaMs,
    double P95DeltaMs,
    double P99DeltaMs,
    Interval PairedLatencyDeltaMs,
    double? CostPer1000DeltaUsd,
    double? MacroF1Delta,
    Verdict Verdict);

public sealed record DatasetInfo(
    string Track,
    string Name,
    string Version,
    string Provenance,
    string SourceFile,
    string ContentHash,
    int Cases,
    IReadOnlyList<string> Labels,
    IReadOnlyDictionary<string, int> LabelCounts);

public sealed record TrackResult(
    string Track,
    DatasetInfo Dataset,
    ArmSummary Baseline,
    ArmSummary WithJev,
    ComparisonSummary Comparison);

public sealed record BenchmarkResult(
    int SchemaVersion,
    string GeneratedAtUtc,
    double DurationSeconds,
    BenchConfigSnapshot Config,
    IReadOnlyList<TrackResult> Tracks,
    IReadOnlyList<CaseObservation> Observations);

/// <summary>The exact configuration of a run. Never contains a secret: the Jev key is reported only as present/absent.</summary>
public sealed record BenchConfigSnapshot(
    BenchSettings Settings,
    string FoundryEndpointHost,
    string ClassifyModelId,
    string ClassifyModelVersion,
    string AnalystModelId,
    string JevModel,
    string JevEndpoint,
    bool JevApiKeyConfigured,
    double JevClassifyMinConfidence,
    bool JevCheckOptionOrder,
    int JevClassifyMaxInputChars,
    double GapJevHighConfidenceThreshold,
    double GapJevMediumConfidenceThreshold,
    string GapMinConfidence,
    int ResilienceMaxRetries,
    int ResilienceRequestTimeoutSeconds,
    string Runtime,
    string OperatingSystem,
    string? GitSha,
    string? RunUrl,
    string ArmADescription,
    string ArmBDescription);
