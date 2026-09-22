using Raffa.Chat.Application.Feedback;
using Raffa.Chat.Application.Gaps;

namespace Raffa.Chat.Tests.Feedback;

/// <summary>ADR-030 D5's privacy allow-list, proven on the text that actually leaves the tenant.</summary>
public sealed class FeatureRequestIssueTextTests
{
    [Fact]
    public void Body_carries_only_the_allow_listed_fields()
    {
        var answers = new FeedbackAnswers("Send the email for me\nfrom my mailbox", FeedbackQuestions.FrequencyEveryRenewal, FeedbackQuestions.ImportanceBlocking);
        var issue = new FeatureRequestIssue(CapabilityGapCatalog.EmailDraftKey, "Draft and send negotiation emails", "en", "dev", "0123abcd", answers);

        var (title, body) = FeatureRequestIssueText.Compose(issue);

        Assert.Equal("[Ask Raffa feedback] Draft and send negotiation emails (dev)", title);
        Assert.Contains("`email-draft`", body, StringComparison.Ordinal);
        Assert.Contains("> Send the email for me", body, StringComparison.Ordinal);
        Assert.Contains("> from my mailbox", body, StringComparison.Ordinal);
        Assert.Contains("at every renewal", body, StringComparison.Ordinal);
        Assert.Contains("blocking", body, StringComparison.Ordinal);
        Assert.Contains("`0123abcd`", body, StringComparison.Ordinal);
        Assert.EndsWith(FeatureRequestIssueText.Footer, body, StringComparison.Ordinal);
        Assert.DoesNotContain("@", body, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", body);
    }

    [Fact]
    public void Rejects_unknown_choice_keys_and_overlong_text()
    {
        Assert.NotNull(FeedbackAnswers.Validate("x", "weekly", "unknown").Error);
        Assert.NotNull(FeedbackAnswers.Validate("x", "daily", "blocking").Error);
        Assert.NotNull(FeedbackAnswers.Validate("  ", "weekly", "blocking").Error);
        Assert.NotNull(FeedbackAnswers.Validate(new string('a', FeedbackQuestions.WhatMaxLength + 1), "weekly", "blocking").Error);

        var (answers, error) = FeedbackAnswers.Validate("  keep it  ", " sometimes ", "nice-to-have");
        Assert.Null(error);
        Assert.Equal(new FeedbackAnswers("keep it", "sometimes", "nice-to-have"), answers);
    }

    [Fact]
    public void Confirmation_copy_follows_the_language_and_the_outcome()
    {
        Assert.Contains("#12", FeedbackReplyCopy.Markdown("it", "Titolo", 12), StringComparison.Ordinal);
        Assert.StartsWith("Grazie, la tua segnalazione", FeedbackReplyCopy.Markdown("it", "Titolo", null), StringComparison.Ordinal);
        Assert.StartsWith("Thanks, I opened issue #12", FeedbackReplyCopy.Markdown("en", "Title", 12), StringComparison.Ordinal);
        Assert.Equal("Open issue #12 →", FeedbackReplyCopy.OpenIssueLabel("en", 12));
    }
}
