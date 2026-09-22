namespace Raffa.Chat.Domain.Conversations;

/// <summary>
/// The reply shape of one <see cref="ConversationMessage"/> — ADR-024's engine section / R-ASK-07.
/// Stored as a string (see <see cref="ConversationRole"/>'s own doc comment for the
/// naming-convention rationale); the Ask engine is what actually decides which kind a turn gets —
/// this module only persists it.
/// </summary>
public enum ConversationMessageKind
{
    /// <summary>A grounded answer with at least one citation.</summary>
    Answer,

    /// <summary>"Insufficient market data" / "cannot determine reliably" (spec §10.4) — a real
    /// attempt that honestly found no groundable claim, never a fabrication (Appendix C rule 10).</summary>
    Abstain,

    /// <summary>Warm decline + hook (greeting/off-domain) or "upload first" (needs_document) —
    /// never a real answer attempt.</summary>
    Redirect,

    /// <summary>Declines a legal reading and offers the commercial analogue instead (R-ASK-02
    /// "legal" label).</summary>
    Refusal,

    /// <summary>A drafted negotiation email (ADR-030 D2) — the preface in <c>Markdown</c>, the
    /// email in <c>ConversationMessage.PayloadJson</c>.</summary>
    Draft,

    /// <summary>A clarifying question with options (ADR-030); its <c>interview_json</c> carries them.</summary>
    Interview,
}
