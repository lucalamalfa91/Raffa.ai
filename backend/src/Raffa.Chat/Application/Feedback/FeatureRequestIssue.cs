using System.Text;
using Raffa.Chat.Application.Gaps;

namespace Raffa.Chat.Application.Feedback;

/// <summary>The three interview answers (<see cref="FeedbackQuestions"/>'s keys).</summary>
/// <param name="What">Free text, at most <see cref="FeedbackQuestions.WhatMaxLength"/> chars.</param>
/// <param name="Frequency">One of <see cref="FeedbackQuestions.FrequencyKeys"/>.</param>
/// <param name="Importance">One of <see cref="FeedbackQuestions.ImportanceKeys"/>.</param>
public sealed record FeedbackAnswers(string What, string Frequency, string Importance)
{
    /// <summary>The trimmed, validated answers, or the reason they are invalid. Server-side and
    /// final: a client cannot submit a fourth question or a fifth choice.</summary>
    public static (FeedbackAnswers? Answers, string? Error) Validate(string? what, string? frequency, string? importance)
    {
        var trimmedWhat = (what ?? string.Empty).Trim();
        if (trimmedWhat.Length == 0)
        {
            return (null, "'answers.what' is required.");
        }

        if (trimmedWhat.Length > FeedbackQuestions.WhatMaxLength)
        {
            return (null, $"'answers.what' must be at most {FeedbackQuestions.WhatMaxLength} characters.");
        }

        var trimmedFrequency = (frequency ?? string.Empty).Trim();
        if (!FeedbackQuestions.FrequencyKeys.Contains(trimmedFrequency))
        {
            return (null, $"'answers.frequency' must be one of: {string.Join(", ", FeedbackQuestions.FrequencyKeys)}.");
        }

        var trimmedImportance = (importance ?? string.Empty).Trim();
        if (!FeedbackQuestions.ImportanceKeys.Contains(trimmedImportance))
        {
            return (null, $"'answers.importance' must be one of: {string.Join(", ", FeedbackQuestions.ImportanceKeys)}.");
        }

        return (new FeedbackAnswers(trimmedWhat, trimmedFrequency, trimmedImportance), null);
    }
}

/// <summary>
/// Everything a published issue may carry (ADR-030 D5's privacy allow-list, enforced by the
/// type: there is no field for the question, a supplier, a contract value or a user). The repo
/// the issue lands in is public.
/// </summary>
public sealed record FeatureRequestIssue(
    string GapKey,
    string GapTitle,
    string Language,
    string Environment,
    string WorkspaceHash,
    FeedbackAnswers Answers);

/// <summary>Composes the issue's title and body — English, the developers' language, with the
/// user's own free-text answer quoted as typed.</summary>
public static class FeatureRequestIssueText
{
    public const string Footer =
        "Submitted through Ask Raffa's in-chat feedback card. Contains no contract data, supplier " +
        "names or user identity by design (ADR-030).";

    public static (string Title, string Body) Compose(FeatureRequestIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        var title = $"[Ask Raffa feedback] {issue.GapTitle} ({issue.Environment})";

        var body = new StringBuilder();
        body.AppendLine("## Capability gap");
        body.AppendLine();
        body.AppendLine($"- Key: `{issue.GapKey}`");
        body.AppendLine($"- Title: {issue.GapTitle}");
        body.AppendLine($"- Question language: {issue.Language}");
        body.AppendLine($"- Environment: {issue.Environment}");
        body.AppendLine($"- Workspace: `{issue.WorkspaceHash}`");
        body.AppendLine();
        body.AppendLine("## Answers");
        body.AppendLine();
        body.AppendLine("**What exactly should Raffa do?**");
        body.AppendLine();
        body.AppendLine(Quote(issue.Answers.What));
        body.AppendLine();
        body.AppendLine($"**How often would you need it?** {FeedbackQuestions.LabelFor(issue.Answers.Frequency)}");
        body.AppendLine();
        body.AppendLine($"**How important is it?** {FeedbackQuestions.LabelFor(issue.Answers.Importance)}");
        body.AppendLine();
        body.AppendLine("---");
        body.AppendLine();
        body.Append(Footer);

        return (title, body.ToString());
    }

    private static string Quote(string text) =>
        string.Join('\n', text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => "> " + line));
}
