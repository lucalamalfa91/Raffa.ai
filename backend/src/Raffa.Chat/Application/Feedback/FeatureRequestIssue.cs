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
/// <c>Gaps.DiscoveredGapText</c> (no supplier, amount, date, e-mail or link). The user's own
/// free-text answer is scrubbed too, when the issue is composed (F4-T01,
/// <c>FeatureRequestScrubber</c>): <see cref="KnownNames"/> lists the names to remove on top
/// of what the scrubber recognises by shape — the tenant's suppliers and the submitting user.
/// </summary>
public sealed record FeatureRequestIssue(
    string GapKey,
    string GapTitle,
    string Language,
    string Environment,
    string WorkspaceHash,
    FeedbackAnswers Answers,
    GapDiscovery? Discovery = null,
    IReadOnlyList<string>? KnownNames = null);
