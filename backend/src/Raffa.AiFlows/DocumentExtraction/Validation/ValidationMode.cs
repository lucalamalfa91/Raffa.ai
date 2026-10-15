namespace Raffa.AiFlows.DocumentExtraction.Validation;

/// <summary>What the extraction orchestrator does with the cross-stage consistency rules (F5-D19).</summary>
public enum ValidationMode
{
    /// <summary>The rules do not run.</summary>
    Off = 0,

    /// <summary>The rules run over the parsed scalar facts; the outcome is counted, logged and metered, and
    /// nothing else changes: no value, no field status, no persisted row, no request. The default.</summary>
    Shadow = 1,

    /// <summary>The fields a violated rule involves are routed to review (<c>review_required</c>) with the rule
    /// id as the named reason. Values are never edited and no fact is dropped.</summary>
    Gate = 2,
}
