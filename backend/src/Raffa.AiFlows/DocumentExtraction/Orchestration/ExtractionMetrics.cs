using System.Diagnostics.Metrics;

namespace Raffa.AiFlows.DocumentExtraction.Orchestration;

/// <summary>Orchestration counters (meter shared with the span verifier and the cross-stage rules).</summary>
internal static class ExtractionMetrics
{
    private static readonly Meter Meter = new("Raffa.AiFlows.Extraction", "1.0.0");

    public static readonly Counter<long> TruncatedStages = Meter.CreateCounter<long>(
        "raffa.extraction.truncated_stages",
        description: "Stage calls that read only the first MaxChunks chunks of a very long document.");
}
