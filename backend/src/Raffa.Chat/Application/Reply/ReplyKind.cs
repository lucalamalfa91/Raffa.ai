namespace Raffa.Chat.Application.Reply;

/// <summary>
/// ADR-024 §6 reply contract kinds (task E13/F06/US01/T01, ask-engine; `inputs/requirements.md`
/// R-ASK-07). Wire-format lowercase via <see cref="ReplyKindWireFormat.ToApiValue"/> — the exact
/// four literals the API contract names: <c>answer</c>, <c>abstain</c>, <c>redirect</c>,
/// <c>refusal</c> — plus, since ADR-030, <c>draft</c>.
/// </summary>
public enum ReplyKind
{
    /// <summary>A grounded answer (including a deterministic capability answer — R-ASK-02's
    /// "capability" label is a real, helpful answer, not a decline) with at least one citation.</summary>
    Answer,

    /// <summary>"Cannot determine reliably" — a real attempt that honestly found no groundable
    /// claim (spec §10.4), shown with the pack's own facts when the guard downgraded rather than
    /// the gateway itself abstaining.</summary>
    Abstain,

    /// <summary>Greeting / off-domain / needs-document — warm prose + one CTA, never a real answer
    /// attempt (R-ASK-02).</summary>
    Redirect,

    /// <summary>Declines outright, never a real answer attempt: a legal reading (R-ASK-02
    /// "legal"), offering the commercial analogue instead; or a conversation's own
    /// <c>scopeContractId</c> that does not resolve to a contract this caller can currently see
    /// (task E27/F02/US01/T01, NW-76; ADR-024 w19 cl. 12).</summary>
    Refusal,

    /// <summary>A drafted negotiation email (ADR-030 D2; ADR-024 amendment 2026-09-22): the
    /// honest preface in <c>AnswerMarkdown</c>, the email itself in
    /// <c>CopilotReply.Payload.Draft</c>, the pack items it was written from as citations, plus the
    /// feedback offer. Never carries inline <c>[n]</c> markers — the draft is guarded by
    /// <c>Application.Drafting.DraftGuard</c>, not by the `answer` role's citation gate.</summary>
    Draft,
}

/// <summary>`GET`/`POST` reply wire-format for <see cref="ReplyKind"/> — lower-case, matching
/// `inputs/requirements.md` §6's own literal <c>kind</c> values (not this codebase's usual bare
/// <c>.ToString()</c> PascalCase convention — same reasoning
/// <c>Raffa.Chat.Application.Capabilities.CapabilityWireFormat</c> already documents for its own
/// two wire-format enums).</summary>
public static class ReplyKindWireFormat
{
    public static string ToApiValue(this ReplyKind kind) => kind switch
    {
        ReplyKind.Answer => "answer",
        ReplyKind.Abstain => "abstain",
        ReplyKind.Redirect => "redirect",
        ReplyKind.Refusal => "refusal",
        ReplyKind.Draft => "draft",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown ReplyKind."),
    };
}
