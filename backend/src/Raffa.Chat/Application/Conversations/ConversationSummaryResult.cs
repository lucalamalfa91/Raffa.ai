using Raffa.SharedKernel;

namespace Raffa.Chat.Application.Conversations;

/// <summary>
/// One row of <see cref="ConversationService.ListRecentAsync"/> (a later task's own
/// `GET /api/conversations`) — id, title, scope and recency only, never the message list (see
/// <see cref="ConversationDetailResult"/> for that).
/// </summary>
public sealed record ConversationSummaryResult(
    EntityId ConversationId,
    string Title,
    EntityId? ScopeContractId,
    DateTimeOffset UpdatedAt);
