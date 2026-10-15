namespace Raffa.AiFlows.DocumentExtraction.Orchestration;

/// <summary>
/// Tuning of <see cref="ExtractionOrchestrator"/>. Every default is today's behaviour (all stages fired
/// at once, no context between stages, no in-run retry); the switches are honoured by
/// <see cref="StageScheduler"/> (F5-T05).
/// </summary>
public sealed class ExtractionOrchestrationOptions
{
    public const string SectionName = "Documents:Extraction";

    /// <summary>0 = unbounded = all stages fired at once (NW-106). N &gt; 0 = at most N gateway calls in flight.</summary>
    public int MaxConcurrentStages { get; set; }

    /// <summary>Whether list stages wait for the scalar stages and receive their auto-accept-grade facts as a
    /// <c>[[CONTEXT]]</c> block ahead of the document text. Off = today. On, each such stage's job hash is that of
    /// its effective request text and its checkpoint is never reused by a resume.</summary>
    public bool UseContextDependencies { get; set; }

    /// <summary>In-run immediate retries of a transient stage failure before it is recorded (today: 0; the
    /// durable, cooled-down retry stays in the hung-processing recovery).</summary>
    public int TransientRetriesPerStage { get; set; }
}
