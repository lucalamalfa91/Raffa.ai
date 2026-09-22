using Raffa.Chat.Application.Capabilities;

namespace Raffa.Chat.Application.Reply;

/// <summary>
/// The full ADR-024 §6 reply contract (task E13/F06/US01/T01, ask-engine; `inputs/requirements.md`
/// §6, R-ASK-07): <c>{ kind, answerMarkdown, citations[], actions[], provenance, followUps[] }</c>.
/// The one shape every gate label / guard outcome ultimately produces — <c>Raffa.Api</c>'s
/// composition root (<c>AskCopilotService</c>) is the only caller that turns this into the
/// `POST /api/conversations/{id}/messages` HTTP JSON body and the persisted
/// <c>Raffa.Chat.Application.Conversations.AppendConversationMessageRequest</c> row.
/// </summary>
/// <param name="Kind">Which of the four reply shapes this is.</param>
/// <param name="AnswerMarkdown">The rendered body — the grounded answer, the capability answer,
/// the redirect/refusal warm prose, or the abstain reason, in the question's own language
/// (OQ-askv2-006). Engineer chrome (guids, "Structured query...", a route line) is never rendered
/// here (R-ASK-08).</param>
/// <param name="Citations">In answer-order (<c>[n]</c> matches <see cref="ReplyCitation.N"/>).
/// Empty for <see cref="ReplyKind.Redirect"/>/<see cref="ReplyKind.Refusal"/>/
/// <see cref="ReplyKind.Abstain"/> unless a citation genuinely backs the decline (rare — most
/// redirects/refusals cite nothing, they only ever offer an action).</param>
/// <param name="Actions">Always from <see cref="CapabilityRouting.ResolveActions"/> — real hrefs
/// from the catalog only (R-SYS-02), never model-authored.</param>
/// <param name="Provenance">See <see cref="ReplyProvenance"/>.</param>
/// <param name="FollowUps">Suggested follow-up questions — populated for
/// <see cref="ReplyKind.Answer"/> (the `answer` role's own <c>followUps</c> field), for
/// <see cref="ReplyKind.Draft"/>, and for a capability-gap <see cref="ReplyKind.Redirect"/> that
/// offers one validated supplier per follow-up (ADR-030); empty for every other kind.</param>
/// <param name="Payload">Structured content beyond prose (ADR-030 D2): the drafted email, the
/// capability gap, the feedback offer or the feedback result — <see langword="null"/> for every
/// turn that carries none, which is every turn that existed before ADR-030. Persisted verbatim
/// as <c>ConversationMessage.PayloadJson</c> and served as the wire's <c>payload</c>.</param>
public sealed record CopilotReply(
    ReplyKind Kind,
    string AnswerMarkdown,
    IReadOnlyList<ReplyCitation> Citations,
    IReadOnlyList<CopilotAction> Actions,
    ReplyProvenance Provenance,
    IReadOnlyList<string> FollowUps,
    ReplyPayload? Payload = null);
