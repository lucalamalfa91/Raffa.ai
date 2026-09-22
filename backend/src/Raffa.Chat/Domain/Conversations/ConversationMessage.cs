using Raffa.SharedKernel;

namespace Raffa.Chat.Domain.Conversations;

/// <summary>
/// One turn in a <see cref="Conversation"/> — either the user's own question
/// (<see cref="ConversationRole.You"/>) or Raffa's reply
/// (<see cref="ConversationRole.Raffa"/>). Task E13/F05/US01/T01 only persists whatever the
/// caller (a later Ask-engine task) already computed; this module never calls
/// <c>Raffa.AiGateway</c> or a retrieval pipeline itself.
/// </summary>
public sealed class ConversationMessage : TenantScopedEntity
{
    public required EntityId ConversationId { get; set; }

    public required ConversationRole Role { get; set; }

    public required ConversationMessageKind Kind { get; set; }

    public required string Markdown { get; set; }

    public required string CitationsJson { get; set; }

    public required string ActionsJson { get; set; }

    public string? ModelId { get; set; }

    public string? PromptVersion { get; set; }

    public string? InputHash { get; set; }

    /// <summary>
    /// The reply's structured half (ADR-030 D2; <c>Application.Reply.ReplyPayload</c> serialized
    /// by <c>ReplyPayloadJson</c>): the drafted email, the capability gap, the feedback offer or
    /// the feedback result. <see langword="null"/> for every turn that carries none.
    /// </summary>
    public string? PayloadJson { get; set; }

    /// <summary>ADR-030. On a Raffa <see cref="ConversationMessageKind.Interview"/> row: the
    /// questions, their options and each option's server-side resolution, plus the consumed-at
    /// stamp once a single-use (consent) option was taken. On the You row that answered an
    /// interview: which message/question/option it answered. Null everywhere else.</summary>
    public string? InterviewJson { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
