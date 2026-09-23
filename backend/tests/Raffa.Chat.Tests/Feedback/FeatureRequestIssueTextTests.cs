using Raffa.Chat.Application.Feedback;
using Raffa.Chat.Application.Gaps;
using Raffa.Chat.Application.Reply;

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
    public void Every_issue_is_a_proposal_awaiting_human_approval()
    {
        var answers = new FeedbackAnswers("Export every quarter", FeedbackQuestions.FrequencySometimes, FeedbackQuestions.ImportanceNiceToHave);
        var issue = new FeatureRequestIssue(CapabilityGapCatalog.ExportFileKey, "Export to Excel or Word", "en", "dev", "0123abcd", answers);

        var (_, body) = FeatureRequestIssueText.Compose(issue);

        Assert.Contains("## Human approval", body, StringComparison.Ordinal);
        Assert.Contains("`approved` label", body, StringComparison.Ordinal);
        Assert.DoesNotContain("## Discovered by Ask Raffa", body, StringComparison.Ordinal);

        // Configuration can add labels but never pre-approve, and never drop the approval gate.
        Assert.Equal(
            ["feedback", "ask-raffa", FeatureRequestIssueText.AwaitingApprovalLabel],
            FeatureRequestIssueText.Labels(issue, ["feedback", " ask-raffa ", "Approved", ""]));
    }

    [Fact]
    public void A_discovered_gap_opens_as_a_feature_proposal_with_what_the_investigator_found()
    {
        var answers = new FeedbackAnswers("A monthly summary for my manager", FeedbackQuestions.FrequencyWeekly, FeedbackQuestions.ImportanceVeryUseful);
        var discovery = new GapDiscovery(
            "Management reports",
            "Generate a periodic report on contracts, spend and savings for management.",
            "portfolio",
            "high",
            CapabilityInvestigatorAgent.Version);
        var issue = new FeatureRequestIssue(
            "discovered:management-report", "Report per il management", "it", "demo", "0123abcd", answers, discovery);

        var (title, body) = FeatureRequestIssueText.Compose(issue);

        Assert.Equal("[Ask Raffa feature proposal] Management reports (demo)", title);
        Assert.Contains("## Discovered by Ask Raffa", body, StringComparison.Ordinal);
        Assert.Contains("- What Raffa should do: Generate a periodic report", body, StringComparison.Ordinal);
        Assert.Contains("- Nearest existing capability: `portfolio`", body, StringComparison.Ordinal);
        Assert.Contains($"`{CapabilityInvestigatorAgent.Version}`, confidence high", body, StringComparison.Ordinal);
        Assert.Contains("> A monthly summary for my manager", body, StringComparison.Ordinal);
        Assert.Contains("## Human approval", body, StringComparison.Ordinal);
        Assert.EndsWith(FeatureRequestIssueText.Footer, body, StringComparison.Ordinal);
        Assert.Equal(
            ["feedback", FeatureRequestIssueText.AwaitingApprovalLabel, FeatureRequestIssueText.DiscoveredLabel],
            FeatureRequestIssueText.Labels(issue, ["feedback"]));
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
        Assert.Contains("must approve it before it is built", FeedbackReplyCopy.Markdown("en", "Title", 12), StringComparison.Ordinal);
        Assert.Contains("dovrà approvarla prima che venga sviluppata", FeedbackReplyCopy.Markdown("it", "Titolo", null), StringComparison.Ordinal);
        Assert.Equal("Open issue #12 →", FeedbackReplyCopy.OpenIssueLabel("en", 12));
    }
}
