using System.Text;
using Raffa.Chat.Application.Gaps;
using Raffa.Chat.Application.Reply;

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
/// the issue lands in is public. ADR-031 adds one member, <see cref="Discovery"/>, for a gap the
/// capability investigator found: generic texts already scrubbed by
/// <c>Gaps.DiscoveredGapText</c> (no supplier, amount, date, e-mail or link).
/// </summary>
public sealed record FeatureRequestIssue(
    string GapKey,
    string GapTitle,
    string Language,
    string Environment,
    string WorkspaceHash,
    FeedbackAnswers Answers,
    GapDiscovery? Discovery = null);

/// <summary>Composes the issue's title, body and labels — English, the developers' language, with
/// the user's own free-text answer quoted as typed. Every issue is a proposal awaiting a human
/// decision (ADR-031): it carries <see cref="AwaitingApprovalLabel"/> and the "Human approval"
/// section, and only a maintainer's <see cref="ApprovedLabel"/> — checked by the
/// <c>feature-request-approval</c> workflow — turns it into work.</summary>
public static class FeatureRequestIssueText
{
    public const string Footer =
        "Submitted through Ask Raffa's in-chat feedback card. Contains no contract data, supplier " +
        "names or user identity by design (ADR-030).";

    /// <summary>On every issue Raffa opens, whatever the configuration says.</summary>
    public const string AwaitingApprovalLabel = "awaiting-approval";

    /// <summary>A maintainer's decision — never set by Raffa, stripped from any configured list.</summary>
    public const string ApprovedLabel = "approved";

    /// <summary>On an issue whose gap the capability investigator discovered (ADR-031).</summary>
    public const string DiscoveredLabel = "ai-discovered";

    /// <summary>The labels to open <paramref name="issue"/> with: the configured ones (minus
    /// <see cref="ApprovedLabel"/>, so no configuration can pre-approve a request), then
    /// <see cref="AwaitingApprovalLabel"/>, then <see cref="DiscoveredLabel"/> for a discovered gap.</summary>
    public static IReadOnlyList<string> Labels(FeatureRequestIssue issue, IEnumerable<string> configured)
    {
        ArgumentNullException.ThrowIfNull(issue);
        ArgumentNullException.ThrowIfNull(configured);

        var labels = configured
            .Where(label => !string.IsNullOrWhiteSpace(label))
            .Select(label => label.Trim())
            .Where(label => !string.Equals(label, ApprovedLabel, StringComparison.OrdinalIgnoreCase))
            .Append(AwaitingApprovalLabel);

        if (issue.Discovery is not null)
        {
            labels = labels.Append(DiscoveredLabel);
        }

        return labels.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static (string Title, string Body) Compose(FeatureRequestIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        var discovery = issue.Discovery;
        var title = discovery is null
            ? $"[Ask Raffa feedback] {issue.GapTitle} ({issue.Environment})"
            : $"[Ask Raffa feature proposal] {discovery.TitleEn} ({issue.Environment})";

        var body = new StringBuilder();
        body.AppendLine("## Capability gap");
        body.AppendLine();
        body.AppendLine($"- Key: `{issue.GapKey}`");
        body.AppendLine($"- Title: {issue.GapTitle}");
        body.AppendLine($"- Question language: {issue.Language}");
        body.AppendLine($"- Environment: {issue.Environment}");
        body.AppendLine($"- Workspace: `{issue.WorkspaceHash}`");
        body.AppendLine();

        if (discovery is not null)
        {
            body.AppendLine("## Discovered by Ask Raffa");
            body.AppendLine();
            body.AppendLine(
                "Ask Raffa's capability investigator judged that the user asked for an operation no " +
                "screen or Ask ability performs today, and proposed it as a feature.");
            body.AppendLine();
            body.AppendLine($"- Proposed feature: {discovery.TitleEn}");
            body.AppendLine($"- What Raffa should do: {discovery.DescriptionEn}");
            body.AppendLine($"- Nearest existing capability: `{discovery.NearestCapability ?? "none"}`");
            body.AppendLine($"- Investigator: `{discovery.InvestigatorVersion}`, confidence {discovery.Confidence}");
            body.AppendLine();
        }

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
        body.AppendLine("## Human approval");
        body.AppendLine();
        body.AppendLine(
            "This is a proposal, not a commitment: nothing is planned or built from it until a " +
            "maintainer approves it.");
        body.AppendLine();
        body.AppendLine(
            $"- [ ] Approve: a maintainer with write access adds the `{ApprovedLabel}` label (the " +
            "`feature-request-approval` workflow removes it when anyone else does).");
        body.AppendLine("- Decline: close the issue as *not planned*.");
        body.AppendLine();
        body.AppendLine("---");
        body.AppendLine();
        body.Append(Footer);

        return (title, body.ToString());
    }

    private static string Quote(string text) =>
        string.Join('\n', text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => "> " + line));
}
