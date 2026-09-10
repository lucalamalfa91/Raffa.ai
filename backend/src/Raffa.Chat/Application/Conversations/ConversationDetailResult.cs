using Raffa.SharedKernel;

namespace Raffa.Chat.Application.Conversations;

/// <summary>
/// <see cref="ConversationService.GetAsync"/>'s full result — the conversation's own fields plus
/// every <see cref="ConversationMessageResult"/> in creation order (a later task's own
/// `GET /api/conversations/{id}`, AC-2: "returns it with its messages").
/// </summary>
public sealed record ConversationDetailResult(
    EntityId ConversationId,
    string Title,
    EntityId? ScopeContractId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<ConversationMessageResult> Messages);
