using Raffa.AiFlows.Ask.Answering;
using Raffa.AiFlows.Ask.Reply;
using Raffa.AiFlows.Shared.Routing;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Domain;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;

namespace Raffa.AiFlows.Tests.Ask.Reply;

/// <summary>
/// Pins the intent-keyed tables <see cref="AbstainRecovery"/> moved out of <c>AskCopilotService</c>
/// (F1 Ask split, PR-1): which next steps, follow-ups and proposal area each intent gets.
/// </summary>
public sealed class AbstainRecoveryTests
{
    private const string GibberishQuestion = "zzqq xxyy";

    private static readonly CapabilityRouting Routing = new();
    private static readonly AbstainRecovery Recovery = new(Routing);

    private static PortfolioListItem Item() =>
        new(Guid.NewGuid(), Guid.NewGuid(), ContractDocumentType.Msa, 1000m, "EUR", null, null, null, null,
            false, "completed", null, null, null);

    private static PortfolioPage Page(int count) =>
        new(Enumerable.Range(0, count).Select(_ => Item()).ToList(), 1, 25, count);

    private static RoutingContext Context(PortfolioPage page, Guid? contractId = null) =>
        new(page.TotalCount, CapabilityCallerRole.Standard, contractId is { } id ? new Raffa.SharedKernel.EntityId(id) : null);

    public static TheoryData<AskIntent?, bool, string> RecoveryIntentTable() => new()
    {
        { AskIntent.Savings, false, "savings+deadline" },
        { AskIntent.PortfolioSavingsTarget, false, "savings+deadline" },
        { AskIntent.PortfolioStrategy, false, "savings+deadline" },
        { AskIntent.RenewalStrategy, false, "deadline+portfolio" },
        { AskIntent.StructuredFact, false, "deadline+portfolio" },
        { AskIntent.MarketCompare, false, "benchmark" },
        { AskIntent.PortfolioMarketPosition, false, "benchmark" },
        { AskIntent.Clause, true, "contractDetail+documents" },
        { AskIntent.Clause, false, "portfolio+documents" },
        { AskIntent.DocumentStatus, false, "documentsAttention" },
        { AskIntent.Navigate, false, "portfolio" },
        { null, false, "portfolio" },
    };

    private static CapabilityIntent[] ExpectedIntents(string table) => table switch
    {
        "savings+deadline" => [CapabilityIntent.Savings, CapabilityIntent.Deadline],
        "deadline+portfolio" => [CapabilityIntent.Deadline, CapabilityIntent.HowTo(CapabilityCatalog.PortfolioKey)],
        "benchmark" => [CapabilityIntent.Benchmark],
        "contractDetail+documents" => [CapabilityIntent.HowTo(CapabilityCatalog.ContractDetailKey), CapabilityIntent.HowTo(CapabilityCatalog.DocumentsKey)],
        "portfolio+documents" => [CapabilityIntent.HowTo(CapabilityCatalog.PortfolioKey), CapabilityIntent.HowTo(CapabilityCatalog.DocumentsKey)],
        "documentsAttention" => [CapabilityIntent.HowTo(CapabilityCatalog.DocumentsAttentionKey)],
        _ => [CapabilityIntent.HowTo(CapabilityCatalog.PortfolioKey)],
    };

    [Theory]
    [MemberData(nameof(RecoveryIntentTable))]
    public void Recovery_actions_follow_the_intent_table(AskIntent? intent, bool withContract, string table)
    {
        var page = Page(2);
        var context = Context(page, withContract ? page.Items[0].ContractId : null);

        var actual = Recovery.ResolveAbstainRecoveryActions(page, context, intent);

        Assert.NotEmpty(actual);
        Assert.Equal(Routing.ResolveActions(ExpectedIntents(table), context), actual);
    }

    [Fact]
    public void Recovery_actions_for_an_empty_workspace_are_the_documents_upload()
    {
        var page = Page(0);
        var context = Context(page);

        var actual = Recovery.ResolveAbstainRecoveryActions(page, context, AskIntent.Savings);

        Assert.Equal(Routing.ResolveActions([CapabilityIntent.UnknownSupplier], context), actual);
    }

    [Fact]
    public void The_resolver_delegate_is_the_intent_less_form()
    {
        var page = Page(1);
        var context = Context(page);

        Assert.Equal(Recovery.ResolveAbstainRecoveryActions(page, context), Recovery.Resolver(page, context));
    }

    [Theory]
    [InlineData(AskIntent.Savings, CapabilityCatalog.SavingsKey)]
    [InlineData(AskIntent.PortfolioSavingsTarget, CapabilityCatalog.SavingsKey)]
    [InlineData(AskIntent.PortfolioStrategy, CapabilityCatalog.SavingsKey)]
    [InlineData(AskIntent.RenewalStrategy, CapabilityCatalog.RenewalsKey)]
    [InlineData(AskIntent.StructuredFact, CapabilityCatalog.RenewalsKey)]
    [InlineData(AskIntent.MarketCompare, CapabilityCatalog.QuoteCheckKey)]
    [InlineData(AskIntent.PortfolioMarketPosition, CapabilityCatalog.QuoteCheckKey)]
    [InlineData(AskIntent.DocumentStatus, CapabilityCatalog.DocumentsKey)]
    [InlineData(AskIntent.Clause, CapabilityCatalog.AskKey)]
    [InlineData(AskIntent.Navigate, CapabilityCatalog.AskKey)]
    public void Follow_ups_come_from_the_intents_capability_area(AskIntent intent, string capabilityKey)
    {
        var actual = AbstainRecovery.AbstainFollowUps("what now", Page(1), intent);

        Assert.Equal(GroundedFallbackAnswer.SuggestedQuestions("what now", capabilityKey), actual);
    }

    [Fact]
    public void Follow_ups_for_an_empty_workspace_come_from_documents_whatever_the_intent()
    {
        var actual = AbstainRecovery.AbstainFollowUps("what now", Page(0), AskIntent.Savings);

        Assert.Equal(GroundedFallbackAnswer.SuggestedQuestions("what now", CapabilityCatalog.DocumentsKey), actual);
    }

    [Theory]
    [InlineData(AskIntent.Savings, CapabilityCatalog.SavingsKey)]
    [InlineData(AskIntent.PortfolioSavingsTarget, CapabilityCatalog.SavingsKey)]
    [InlineData(AskIntent.RenewalStrategy, CapabilityCatalog.RenewalsKey)]
    [InlineData(AskIntent.MarketCompare, CapabilityCatalog.QuoteCheckKey)]
    [InlineData(AskIntent.PortfolioMarketPosition, CapabilityCatalog.QuoteCheckKey)]
    [InlineData(AskIntent.Clause, CapabilityCatalog.ContractDetailKey)]
    [InlineData(AskIntent.DocumentStatus, CapabilityCatalog.DocumentsKey)]
    [InlineData(AskIntent.StructuredFact, null)]
    [InlineData(AskIntent.Navigate, null)]
    public void Proposal_area_is_the_intents_area_when_the_question_names_none(AskIntent intent, string? expected)
    {
        Assert.Null(HelpfulFallbackAnswer.AreaFor(GibberishQuestion));

        Assert.Equal(expected, AbstainRecovery.ProposalArea(GibberishQuestion, intent));
    }
}
