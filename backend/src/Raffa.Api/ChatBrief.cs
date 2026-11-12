using System.Text;
using System.Text.RegularExpressions;
using Raffa.AiFlows.Ask.Agentic;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Domain.Conversations;
using Raffa.Renewals.Application.Cases;

namespace Raffa.Api;

/// <summary>
/// What a renewal case carries over from the chat it was started in, as plain text: first what the user said there
/// (their goals and constraints -- "possiamo ridurre le licenze del 10%"), then Raffa's own latest answers (the
/// levers, the strategy, the briefing -- already grounded and guarded when they were given), newest first, within
/// the case's limit. A long answer is cut, never dropped, so a chat always hands its context over. Read from the
/// stored conversation, never from the client.
/// </summary>
internal static partial class ChatBrief
{
    /// <summary>The most of the brief the user's own messages take; Raffa's answers fill the rest.</summary>
    internal const int UserShareChars = 1200;

    private const int UserMessageMaxChars = 400;
    private const int UserMessagesMax = 6;
    private const int AnswersMax = 3;

    [GeneratedRegex(@"\s*\[\d+\]")]
    private static partial Regex Citation();

    [GeneratedRegex(@"^\s*[-*]\s+", RegexOptions.Multiline)]
    private static partial Regex Bullet();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    public static string? From(ConversationDetailResult conversation, bool italian = true)
    {
        var said = conversation.Messages
            .Where(m => m.Role == ConversationRole.You && !string.IsNullOrWhiteSpace(m.Markdown))
            .Select(m => Spaces().Replace(m.Markdown, " ").Trim())
            // A bare command ("aprilo in rinnovi") says nothing about the renewal itself.
            .Where(t => t.Length > 0 && !(RenewalStartCommand.IsCommand(t) && t.Split(' ').Length <= 6))
            .TakeLast(UserMessagesMax)
            .ToList();

        var userLines = new List<string>();
        var userLength = 0;
        foreach (var text in Enumerable.Reverse(said))
        {
            var line = "• " + Cut(text, UserMessageMaxChars);
            if (userLength + line.Length > UserShareChars)
            {
                break;
            }

            userLines.Insert(0, line);
            userLength += line.Length + 1;
        }

        var answers = conversation.Messages
            .Where(m => m.Role == ConversationRole.Raffa
                && !string.IsNullOrWhiteSpace(m.Markdown)
                // The renewal start / confirmation line itself is not a lever.
                && !(m.PayloadJson?.Contains("actionProposal", StringComparison.Ordinal) ?? false))
            .Select(m => Plain(m.Markdown))
            .Where(t => t.Length > 0)
            .Reverse()
            .Take(AnswersMax)
            .ToList();

        var userBlock = userLines.Count == 0
            ? string.Empty
            : (italian ? "Cosa hai indicato in chat:" : "What you set in chat:") + "\n" + string.Join("\n", userLines);
        var answerHeader = italian ? "Leve e strategia emerse:" : "Levers and strategy:";
        var budget = RenewalCaseEngine.BriefMaxChars - userBlock.Length - answerHeader.Length - 4;

        var picked = new List<string>();
        foreach (var answer in answers)
        {
            if (budget < 200)
            {
                break;
            }

            var kept = Cut(answer, budget);
            picked.Add(kept);
            budget -= kept.Length + 2;
        }

        if (userBlock.Length == 0 && picked.Count == 0)
        {
            return null;
        }

        picked.Reverse();
        var brief = new StringBuilder(userBlock);
        if (picked.Count > 0)
        {
            if (brief.Length > 0)
            {
                brief.Append("\n\n");
            }

            brief.Append(answerHeader).Append('\n').Append(string.Join("\n\n", picked));
        }

        return brief.ToString();
    }

    private static string Cut(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        var cut = text[..Math.Max(0, max - 1)];
        var lastBreak = cut.LastIndexOfAny(['\n', '.', ';']);
        return (lastBreak > max / 2 ? cut[..(lastBreak + 1)] : cut).TrimEnd() + "…";
    }

    private static string Plain(string markdown)
    {
        var text = Citation().Replace(markdown, string.Empty).Replace("**", string.Empty, StringComparison.Ordinal);
        return Bullet().Replace(text, "• ").Trim();
    }
}
