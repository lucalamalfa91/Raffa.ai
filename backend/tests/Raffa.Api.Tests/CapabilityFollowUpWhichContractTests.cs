using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Gaps;
using Raffa.Chat.Application.Language;
using Raffa.Chat.Application.Reply;
using Raffa.SharedKernel;

namespace Raffa.Api.Tests;

/// <summary>
/// R1-07: the investigator's follow-up for the email gaps and the in-turn "which contract?" reply
/// are built by the same <see cref="CapabilityGapReplyBuilder.WhichContract"/>; the follow-up only
/// adds its opening. (The in-turn side is held by <c>AskCapabilityGapTests</c>.)
/// </summary>
public sealed class CapabilityFollowUpWhichContractTests
{
    private static readonly CapabilityGap Gap = CapabilityGapCatalog.Find(CapabilityGapCatalog.EmailDraftKey)!;
    private static readonly CapabilityRouting Routing = new();

    private static CapabilityCheckRequest Request(
        string question, IReadOnlyList<string> suppliers, int validated, string? named = null) =>
        new(TenantId.New(), question, suppliers, validated, named, NamedContractId: null);

    [Fact]
    public void The_follow_up_is_the_shared_which_contract_reply_behind_its_checked_opening()
    {
        var request = Request("write the renewal email", ["Oracle", "Amazon Web Services"], validated: 2);

        var followUp = CapabilityCheckRunner.BuildFollowUp(Gap, [], request, Routing);

        var language = QuestionLanguage.Detect(request.Question);
        var shared = CapabilityGapReplyBuilder.WhichContract(
            Gap, language, CapabilityGapCopy.CheckedOpening(language), unknownSupplier: null, portfolioIsEmpty: false,
            request.SupplierNames, Routing, new RoutingContext(2, CapabilityCallerRole.Standard));

        Assert.Equal(shared.AnswerMarkdown, followUp.AnswerMarkdown);
        Assert.Equal(shared.FollowUps, followUp.FollowUps);
        Assert.Equal(shared.Actions, followUp.Actions);
        Assert.StartsWith(CapabilityGapCopy.CheckedOpening(language) + " ", followUp.AnswerMarkdown, StringComparison.Ordinal);
        Assert.Equal(
            [
                CapabilityGapCopy.DraftFollowUp(language, "Amazon Web Services"),
                CapabilityGapCopy.DraftFollowUp(language, "Oracle"),
            ],
            followUp.FollowUps);
    }

    [Fact]
    public void A_named_supplier_is_the_only_chip()
    {
        var request = Request("write the renewal email", ["Oracle", "Amazon Web Services"], validated: 2, named: "Oracle");

        var followUp = CapabilityCheckRunner.BuildFollowUp(Gap, [], request, Routing);

        Assert.Equal(
            [CapabilityGapCopy.DraftFollowUp(QuestionLanguage.Detect(request.Question), "Oracle")],
            followUp.FollowUps);
    }

    [Fact]
    public void An_empty_workspace_offers_the_documents_upload_and_no_chips()
    {
        var request = Request("write the renewal email", [], validated: 0);

        var followUp = CapabilityCheckRunner.BuildFollowUp(Gap, [], request, Routing);

        Assert.Empty(followUp.FollowUps);
        Assert.Equal(
            Routing.ResolveActions([CapabilityIntent.UnknownSupplier], new RoutingContext(0, CapabilityCallerRole.Standard)),
            followUp.Actions);
    }
}
