using Raffa.Chat.Domain.Conversations;

namespace Raffa.Chat.Application.Conversations;

/// <summary>
/// Input to <see cref="ConversationService.AppendMessageAsync"/> — everything the Ask engine (a
/// later task) has already computed for one turn. This module never calls
/// <c>Raffa.AiGateway</c> or a retrieval pipeline itself (see
/// <see cref="Domain.Conversations.ConversationMessage"/>'s own doc comment); it only persists
/// what the caller hands it, the same "caller already did the work" shape
/// <c>RagAnswerService.AnswerAsync</c>'s <c>evidence</c> parameter already uses.
/// </summary>
/// <param name="Role">Who authored this turn.</param>
/// <param name="Kind">The reply shape — irrelevant (but still required) for a
/// <see cref="ConversationRole.You"/> turn; callers may use
/// <see cref="ConversationMessageKind.Answer"/> as the inert default for the user's own
/// question.</param>
/// <param name="Markdown">The rendered body (or the user's own question text).</param>
/// <param name="CitationsJson">The `citations[]` array, already serialized to JSON — pass
/// `"[]"` for none, never <see langword="null"/>.</param>
/// <param name="ActionsJson">The `actions[]` array, already serialized to JSON — pass `"[]"`
/// for none, never <see langword="null"/>.</param>
/// <param name="ModelId">AI reproducibility metadata (ADR-011) — <see langword="null"/> when no
/// `answer`-role model call produced this turn.</param>
/// <param name="PromptVersion">Same nullability contract as <paramref name="ModelId"/>.</param>
/// <param name="InputHash">Same nullability contract as <paramref name="ModelId"/>.</param>
public sealed record AppendConversationMessageRequest(
    ConversationRole Role,
    ConversationMessageKind Kind,
    string Markdown,
    string CitationsJson,
    string ActionsJson,
    string? ModelId = null,
    string? PromptVersion = null,
    string? InputHash = null);
