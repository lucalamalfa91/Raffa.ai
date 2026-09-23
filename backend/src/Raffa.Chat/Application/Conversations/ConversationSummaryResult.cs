using Raffa.SharedKernel;

namespace Raffa.Chat.Application.Conversations;

/// <summary>
/// One row of <see cref="ConversationService.ListRecentAsync"/> (a later task's own
/// `GET /api/conversations`) — id, title, scope and recency only, never the message list (see
/// <see cref="ConversationDetailResult"/> for that). <see cref="CustomTitle"/> is the name the
/// user gave the chat, <see langword="null"/> while it keeps its automatic <see cref="Title"/>.
/// <see cref="Archived"/>: not used for <see cref="ConversationService.ArchiveAfter"/> — the rail
/// files it under "Archive" (see <see cref="ConversationService.RestoreAsync"/>).
/// </summary>
public sealed record ConversationSummaryResult(
    EntityId ConversationId,
    string Title,
    EntityId? ScopeContractId,
    DateTimeOffset UpdatedAt,
    string? CustomTitle = null,
    bool Archived = false);

/// <summary>Which of the caller's conversations <see cref="ConversationService.ListRecentAsync"/>
/// returns: all of them, only the ones in use, or only the archived ones.</summary>
public enum ConversationArchiveFilter
{
    All,
    Active,
    Archived,
}
