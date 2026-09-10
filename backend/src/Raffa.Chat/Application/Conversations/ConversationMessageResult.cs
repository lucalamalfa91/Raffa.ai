using Raffa.Chat.Domain.Conversations;
using Raffa.SharedKernel;

namespace Raffa.Chat.Application.Conversations;

/// <summary>
/// One turn of a conversation, as <see cref="ConversationService.GetAsync"/> and
/// <see cref="ConversationService.AppendMessageAsync"/> both return it — role, markdown,
/// citations/actions JSON, kind, AI metadata and timestamp (R-CONV-01: "Messages store role,
/// markdown, citations, actions, kind, AI metadata... — never the raw pack").
/// </summary>
public sealed record ConversationMessageResult(
    EntityId MessageId,
    EntityId ConversationId,
    ConversationRole Role,
    ConversationMessageKind Kind,
    string Markdown,
    string CitationsJson,
    string ActionsJson,
    string? ModelId,
    string? PromptVersion,
    string? InputHash,
    DateTimeOffset CreatedAt);
