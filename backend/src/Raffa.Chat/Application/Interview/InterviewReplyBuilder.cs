using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Reply;

namespace Raffa.Chat.Application.Interview;

/// <summary>The one place an interview becomes a <see cref="CopilotReply"/>: no citations, no
/// model call, no retrieval — just the prompt and the questions (ADR-030).</summary>
public static class InterviewReplyBuilder
{
    public static CopilotReply Interview(InterviewTurn turn, IReadOnlyList<CopilotAction>? actions = null)
    {
        ArgumentNullException.ThrowIfNull(turn);

        return new CopilotReply(
            ReplyKind.Interview,
            turn.Prompt,
            [],
            actions ?? [],
            ReplyProvenance.NoModelCall([]),
            [],
            turn);
    }
}
