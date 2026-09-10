namespace Raffa.Chat.Domain.Conversations;

/// <summary>
/// Who authored one <see cref="ConversationMessage"/> — the two turn types
/// `inputs/requirements.md` §5.2 (R-CONV-01) and the V2 prototype's conversation screen
/// (`raffa-v2/screens-v2.md`: "turns You ... Raffa ...") name. Stored as a string via
/// <c>Infrastructure.Configurations.ConversationMessageConfiguration</c> (same
/// `.HasConversion&lt;string&gt;()` convention every other enum-backed column in this codebase
/// already uses, e.g. <c>Raffa.Documents.Contracts.Domain.Contract.Type</c>) — task
/// E13/F05/US01/T02 (the API layer) owns mapping these PascalCase members onto the wire-format
/// lowercase `you`/`raffa` literals ADR-024's reply contract names.
/// </summary>
public enum ConversationRole
{
    /// <summary>The human asking — the caller identified by <c>ConversationService</c>'s own
    /// <c>userId</c> parameter (ADR-022/OQ-askv2-005), never a system/service identity.</summary>
    You,

    /// <summary>Raffa's own reply — produced by the Ask engine (a later task; this task only
    /// persists whatever the caller already computed, see <c>ConversationService</c>'s doc
    /// comment).</summary>
    Raffa,
}
