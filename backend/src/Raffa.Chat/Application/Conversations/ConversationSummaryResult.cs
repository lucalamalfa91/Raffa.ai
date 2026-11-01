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
    bool Archived = false,
    DateTimeOffset? ArchivedAt = null,
    EntityId? CaseId = null,
    int QuestionCount = 0);

/// <summary>Which of the caller's conversations <see cref="ConversationService.ListRecentAsync"/>
/// returns: all of them, only the ones in use, or only the archived ones.</summary>
/// <summary>AskRaffa 4.0 Chiedi groups (<c>GET /api/conversations?state=</c>): recent = not
/// archived and not a case chat; archived = explicitly archived; case = a renewal case chat.</summary>
public enum ConversationListState
{
    Recent,
    Archived,
    Case,
}

public enum ConversationArchiveFilter
{
    All,
    Active,
    Archived,
}
