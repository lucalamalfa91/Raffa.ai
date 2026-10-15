using Raffa.AiFlows.DocumentExtraction.Validation;
using Raffa.AiFlows.DocumentExtraction.Verification;

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

    /// <summary>F5-T07: what to do with the span verifier's verdicts. <c>Shadow</c> (default) verifies and
    /// records metrics and a log line, and changes nothing: no field status, value, request or persisted row.
    /// <c>Gate</c> routes a fact whose span is not found (or only on another page) to review. <c>Off</c>
    /// does not run the verifier.</summary>
    public SpanVerificationMode SpanVerification { get; set; } = SpanVerificationMode.Shadow;

    /// <summary>F5-D19: what to do with the cross-stage consistency rules (date order, deadline consistency,
    /// TCV vs annual spend). <c>Shadow</c> (default) evaluates and records counts, a log line and metrics and
    /// changes nothing. <c>Gate</c> routes the involved fields to review with the rule id as the named reason,
    /// never editing a value. <c>Off</c> does not run the rules. Gate is configurable only.</summary>
    public ValidationMode CrossStageValidation { get; set; } = ValidationMode.Shadow;

    /// <summary>Minimum similarity (1 - edit distance / span length) for an approximate span match.</summary>
    public double SpanFuzzyThreshold { get; set; } = SpanVerifier.DefaultThreshold;
}
