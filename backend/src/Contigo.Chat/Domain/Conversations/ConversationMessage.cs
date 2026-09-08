using Contigo.SharedKernel;

namespace Contigo.Chat.Domain.Conversations;

/// <summary>
/// One turn in a <see cref="Conversation"/> — either the user's own question
/// (<see cref="ConversationRole.You"/>) or Contigo's reply
/// (<see cref="ConversationRole.Contigo"/>). Task E13/F05/US01/T01 only persists whatever the
/// caller (a later Ask-engine task) already computed; this module never calls
/// <c>Contigo.AiGateway</c> or a retrieval pipeline itself — same "operate on caller-supplied
/// data" shape <c>Application.RagAnswerService</c> and <c>Application.DeterministicQueryHandler</c>
/// already use.
///
/// ADR-011 ("no raw prompt / pack in rows"): this row stores the already-rendered
/// <see cref="Markdown"/> reply plus its citations/actions/AI-metadata, never the retrieved
/// evidence, the pack, or the prompt that produced it.
/// </summary>
public sealed class ConversationMessage : TenantScopedEntity
{
    public required EntityId ConversationId { get; set; }

    public required ConversationRole Role { get; set; }

    public required ConversationMessageKind Kind { get; set; }

    /// <summary>The rendered reply body (or the user's own question text) with inline `[n]`
    /// citation markers (R-WEB-04) — never the raw retrieval pack (ADR-011).</summary>
    public required string Markdown { get; set; }

    /// <summary>
    /// The reply's `citations[]` array (ADR-024 §6 reply contract), serialized as JSON —
    /// required, not nullable: even a <see cref="ConversationMessageKind.Abstain"/>/
    /// <see cref="ConversationMessageKind.Redirect"/>/<see cref="ConversationMessageKind.Refusal"/>
    /// turn (and a <see cref="ConversationRole.You"/> question) carries an explicit empty-array
    /// `"[]"`, never a missing column — the caller (a later task) always serializes one.
    /// </summary>
    public required string CitationsJson { get; set; }

    /// <summary>The reply's `actions[]` array (ADR-024 §6), serialized as JSON — same
    /// "always present, possibly empty-array" contract as <see cref="CitationsJson"/>.</summary>
    public required string ActionsJson { get; set; }

    /// <summary>
    /// AI reproducibility metadata (ADR-011 "log model/version/prompt-version/timestamp/input
    /// hash — never the raw prompt or retrieved content") — <see langword="null"/> for a
    /// <see cref="ConversationRole.You"/> message (the user's own question was never a model
    /// call) and for any turn the domain gate answered deterministically with no `answer`-role
    /// call at all (R-ASK-02's greeting/off_domain/capability/needs_document redirects).
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>Same nullability contract as <see cref="ModelId"/> — the versioned persona
    /// prompt's own version (R-ASK-05), not this row's schema version.</summary>
    public string? PromptVersion { get; set; }

    /// <summary>Same nullability contract as <see cref="ModelId"/> — a content hash of the
    /// retrieved evidence/prompt (ADR-011's "Input hash" definition), never the confidential
    /// input itself.</summary>
    public string? InputHash { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
