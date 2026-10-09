using Raffa.AiFlows.CapabilityGaps.Replies;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Gaps;
using Raffa.Chat.Application.Reply;

namespace Raffa.AiFlows.Tests.CapabilityGaps.Replies;

/// <summary>
/// R1-07: the "which contract?" reply for the email gaps has one builder, behind both the in-turn
/// reply and the investigator's follow-up message. These pin its behaviour: the optional opening,
/// the supplier chips (blank dropped, de-duplicated, sorted, capped), the Portfolio action - or the
/// Documents upload on an empty workspace - and the unknown supplier named back.
/// </summary>
public sealed class CapabilityGapWhichContractTests
{
    private static readonly CapabilityGap Gap = CapabilityGapCatalog.Find(CapabilityGapCatalog.EmailDraftKey)!;
    private static readonly CapabilityRouting Routing = new();
    private static readonly RoutingContext Populated = new(3, CapabilityCallerRole.Standard);
    private static readonly RoutingContext Empty = new(0, CapabilityCallerRole.Standard);

    [Fact]
    public void Without_an_opening_the_reply_is_the_question_alone()
    {
        var reply = CapabilityGapReplyBuilder.WhichContract(
            Gap, "en", opening: null, unknownSupplier: null, portfolioIsEmpty: false, ["Oracle"], Routing, Populated);

        Assert.Equal(ReplyKind.Redirect, reply.Kind);
        Assert.Equal(CapabilityGapCopy.AskWhichContract(Gap, "en", null, portfolioIsEmpty: false), reply.AnswerMarkdown);
    }

    [Fact]
    public void An_opening_leads_the_question_in_the_same_message()
    {
        var opening = CapabilityGapCopy.CheckedOpening("it");

        var reply = CapabilityGapReplyBuilder.WhichContract(
            Gap, "it", opening, unknownSupplier: null, portfolioIsEmpty: false, ["Oracle"], Routing, Populated);

        Assert.Equal(
            opening + " " + CapabilityGapCopy.AskWhichContract(Gap, "it", null, portfolioIsEmpty: false),
            reply.AnswerMarkdown);
    }

    [Fact]
    public void Chips_drop_blanks_and_duplicates_sort_by_name_and_stop_at_five()
    {
        string?[] names = ["Zoom", "oracle", "Oracle", " ", null, "Salesforce", "Adobe", "SAP", "Microsoft", "Atlassian"];

        var reply = CapabilityGapReplyBuilder.WhichContract(
            Gap, "en", opening: null, unknownSupplier: null, portfolioIsEmpty: false, names, Routing, Populated);

        Assert.Equal(CapabilityGapReplyBuilder.MaxSupplierFollowUps, reply.FollowUps.Count);
        string[] expected = ["Adobe", "Atlassian", "Microsoft", "oracle", "Salesforce"];
        Assert.Equal(expected.Select(n => CapabilityGapCopy.DraftFollowUp("en", n)), reply.FollowUps);
    }

    [Fact]
    public void A_populated_workspace_gets_the_portfolio_action()
    {
        var reply = CapabilityGapReplyBuilder.WhichContract(
            Gap, "en", opening: null, unknownSupplier: null, portfolioIsEmpty: false, ["Oracle"], Routing, Populated);

        var expected = Routing.ResolveActions([CapabilityIntent.HowTo(CapabilityCatalog.PortfolioKey)], Populated);
        Assert.Equal(expected, reply.Actions);
        Assert.NotEmpty(reply.Actions);
    }

    [Fact]
    public void An_empty_workspace_gets_the_documents_upload_and_the_upload_invitation()
    {
        var reply = CapabilityGapReplyBuilder.WhichContract(
            Gap, "en", opening: null, unknownSupplier: null, portfolioIsEmpty: true, [], Routing, Empty);

        Assert.Equal(Routing.ResolveActions([CapabilityIntent.UnknownSupplier], Empty), reply.Actions);
        Assert.Contains("upload a contract in Documents", reply.AnswerMarkdown, StringComparison.Ordinal);
        Assert.Empty(reply.FollowUps);
    }

    [Fact]
    public void An_unknown_supplier_is_named_back()
    {
        var reply = CapabilityGapReplyBuilder.WhichContract(
            Gap, "en", opening: null, unknownSupplier: "Databricks", portfolioIsEmpty: false, ["Oracle"], Routing, Populated);

        Assert.Contains("I have no validated Databricks contract", reply.AnswerMarkdown, StringComparison.Ordinal);
    }

    [Fact]
    public void The_reply_carries_the_gap_and_the_feedback_offer()
    {
        var reply = CapabilityGapReplyBuilder.WhichContract(
            Gap, "en", opening: null, unknownSupplier: null, portfolioIsEmpty: false, [], Routing, Populated);

        Assert.Equal(Gap.Key, reply.Payload!.Gap!.Key);
        Assert.NotNull(reply.Payload.FeedbackOffer);
    }
}
