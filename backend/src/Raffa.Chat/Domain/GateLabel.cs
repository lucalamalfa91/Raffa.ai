namespace Raffa.Chat.Domain;

/// <summary>
/// The fixed domain-gate label set (task E13/F06/US01/T01, ask-engine; ADR-024 "engine pipeline";
/// `inputs/requirements.md` R-ASK-02). Every turn is classified into exactly one of these six
/// labels before a planner, a pack, or a model ever runs — <see cref="OffDomain"/> and
/// <see cref="Greeting"/> read identically downstream (R-ASK-02's own table: "warm decline +
/// portfolio hook... no retrieval") but are kept as distinct labels because they are detected by
/// two distinct lexicons and the audit trail (<c>chat.redirected</c>) benefits from knowing which
/// one actually fired.
/// </summary>
public enum GateLabel
{
    /// <summary>"ciao" / "hello" — small talk with no question at all.</summary>
    Greeting,

    /// <summary>A real question, but not about contracts/procurement (a recipe, a photo, the
    /// weather, ...). Same behaviour as <see cref="Greeting"/> (warm decline + hook), a distinct
    /// label because it is matched by a separate topic lexicon.</summary>
    OffDomain,

    /// <summary>Asks for a legal reading/advice ("can I sue", "is this enforceable") — refused,
    /// with a commercial analogue offered instead (R-ASK-02).</summary>
    Legal,

    /// <summary>Asks what Raffa can do / how to use a feature — answered from the capability
    /// catalog, never retrieval (R-SYS-01/02).</summary>
    Capability,

    /// <summary>Names a supplier that does not resolve to any of this tenant's known suppliers —
    /// answered with an upload invite, never retrieval (R-ASK-03).</summary>
    NeedsDocument,

    /// <summary>Everything else: a real procurement/contract question that must go through the
    /// planner, the context pack and the guarded answer role.</summary>
    InDomain,
}
