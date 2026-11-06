using System.Text;
using System.Text.RegularExpressions;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Domain.Conversations;
using Raffa.Renewals.Application.Cases;

namespace Raffa.Api;

/// <summary>
/// What a renewal case carries over from the chat it was started in: Raffa's own earlier answers of that
/// conversation (the levers, the strategy, the briefing -- already grounded and guarded when they were given),
/// newest first until the case's limit, as plain text. Read from the stored conversation, never from the client.
/// </summary>
internal static partial class ChatBrief
{
    [GeneratedRegex(@"\s*\[\d+\]")]
    private static partial Regex Citation();

    [GeneratedRegex(@"^\s*[-*]\s+", RegexOptions.Multiline)]
    private static partial Regex Bullet();

    public static string? From(ConversationDetailResult conversation)
    {
        var answers = conversation.Messages
            .Where(m => m.Role == ConversationRole.Raffa
                && !string.IsNullOrWhiteSpace(m.Markdown)
                // The confirmation question itself is not a lever.
                && !(m.PayloadJson?.Contains("actionProposal", StringComparison.Ordinal) ?? false))
            .Select(m => Plain(m.Markdown))
            .Where(t => t.Length > 0)
            .Reverse()
            .ToList();

        var picked = new List<string>();
        var length = 0;
        foreach (var answer in answers)
        {
            if (picked.Count == 3 || length + answer.Length > RenewalCaseEngine.BriefMaxChars)
            {
                break;
            }

            picked.Add(answer);
            length += answer.Length + 2;
        }

        if (picked.Count == 0)
        {
            return null;
        }

        picked.Reverse();
        return string.Join("\n\n", picked);
    }

    private static string Plain(string markdown)
    {
        var text = Citation().Replace(markdown, string.Empty).Replace("**", string.Empty, StringComparison.Ordinal);
        return Bullet().Replace(text, "• ").Trim();
    }
}
