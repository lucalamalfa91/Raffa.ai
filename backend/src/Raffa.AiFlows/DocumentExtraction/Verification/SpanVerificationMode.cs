namespace Raffa.AiFlows.DocumentExtraction.Verification;

/// <summary>What the extraction orchestrator does with the span verifier's verdicts (F5-T07).</summary>
public enum SpanVerificationMode
{
    /// <summary>The verifier does not run.</summary>
    Off = 0,

    /// <summary>The verifier runs and its outcome is counted and logged; no fact, field status or value is
    /// ever changed and nothing extra is persisted. The default.</summary>
    Shadow = 1,

    /// <summary>A fact whose span is not found where the model said (not found at all, or only on another
    /// page) is flagged <c>SpanVerified = false</c>, which the store turns into review-required. The value
    /// is kept and the model's page is never rewritten.</summary>
    Gate = 2,
}
