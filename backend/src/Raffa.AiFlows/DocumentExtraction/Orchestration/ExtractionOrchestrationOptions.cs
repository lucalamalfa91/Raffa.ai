namespace Raffa.AiFlows.DocumentExtraction.Orchestration;

/// <summary>
/// Tuning of <see cref="ExtractionOrchestrator"/>. Every default is today's behaviour. The scheduling
/// switches are declared here and bound from configuration, but the orchestrator currently honours only
/// the defaults (all stages fired at once, no context between stages, no in-run retry).
/// </summary>
public sealed class ExtractionOrchestrationOptions
{
    public const string SectionName = "Documents:Extraction";

    /// <summary>0 = unbounded = all stages fired at once (NW-106).</summary>
    public int MaxConcurrentStages { get; set; }

    /// <summary>Whether list stages wait for the scalar stages and receive their facts. Off = today.</summary>
    public bool UseContextDependencies { get; set; }

    /// <summary>In-run immediate retries of a transient stage failure (today: 0).</summary>
    public int TransientRetriesPerStage { get; set; }
}
