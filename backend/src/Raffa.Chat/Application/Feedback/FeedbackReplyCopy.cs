using Raffa.Chat.Application.Language;

namespace Raffa.Chat.Application.Feedback;

/// <summary>The confirmation turn's copy, in the language the offer was shown in (ADR-030 D4/D5).</summary>
public static class FeedbackReplyCopy
{
    /// <summary>The thank-you, then what happens next (ADR-031): a person on the team reviews the
    /// request and approves it before anything is built.</summary>
    public static string Markdown(string language, string gapTitle, int? issueNumber)
    {
        if (QuestionLanguage.IsItalian(language))
        {
            return issueNumber is { } n
                ? $"Grazie, ho aperto la segnalazione #{n} al team Raffa.ai: «{gapTitle}». Una persona del team " +
                  "la valuterà e dovrà approvarla prima che venga sviluppata: puoi seguirla dal link qui sotto."
                : $"Grazie, la tua segnalazione «{gapTitle}» è stata registrata: il team Raffa.ai la riceverà, " +
                  "e una persona del team dovrà approvarla prima che venga sviluppata.";
        }

        return issueNumber is { } number
            ? $"Thanks, I opened issue #{number} for the Raffa.ai team: “{gapTitle}”. A person on the team " +
              "reviews it and must approve it before it is built: you can follow it from the link below."
            : $"Thanks, your report “{gapTitle}” has been recorded: the Raffa.ai team will receive it, and a " +
              "person on the team must approve it before it is built.";
    }

    public static string OpenIssueLabel(string language, int issueNumber) =>
        QuestionLanguage.IsItalian(language)
            ? $"Apri la segnalazione #{issueNumber} →"
            : $"Open issue #{issueNumber} →";
}
