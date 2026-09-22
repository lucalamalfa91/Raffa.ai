using Raffa.Chat.Application.Language;

namespace Raffa.Chat.Application.Feedback;

/// <summary>The confirmation turn's copy, in the language the offer was shown in (ADR-030 D4/D5).</summary>
public static class FeedbackReplyCopy
{
    public static string Markdown(string language, string gapTitle, int? issueNumber)
    {
        if (QuestionLanguage.IsItalian(language))
        {
            return issueNumber is { } n
                ? $"Grazie, ho aperto la segnalazione #{n} al team Raffa.ai: «{gapTitle}». Puoi seguirla dal link qui sotto."
                : $"Grazie, la tua segnalazione «{gapTitle}» è stata registrata: il team Raffa.ai la riceverà.";
        }

        return issueNumber is { } number
            ? $"Thanks, I opened issue #{number} for the Raffa.ai team: “{gapTitle}”. You can follow it from the link below."
            : $"Thanks, your report “{gapTitle}” has been recorded: the Raffa.ai team will receive it.";
    }

    public static string OpenIssueLabel(string language, int issueNumber) =>
        QuestionLanguage.IsItalian(language)
            ? $"Apri la segnalazione #{issueNumber} →"
            : $"Open issue #{issueNumber} →";
}
