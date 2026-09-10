using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Chat.Tests.Capabilities;

/// <summary>
/// Proves task E13/F08/US01/T01's routing table (story us-01-capability-catalog AC-2/AC-3):
/// <see cref="CapabilityRouting.ResolveActions"/> maps the fixed planner intents (ADR-024
/// "planner (fixed intents)") to <see cref="CopilotAction"/>s built only from catalog patterns and
/// known object ids, and replaces a greyed capability's action with the Documents upload action
/// (R-SYS-04) when no contract is validated yet.
/// </summary>
public sealed class CapabilityRoutingTests
{
    private readonly CapabilityRouting _routing = new();

    private static RoutingContext ContextWith(
        int validatedContractCount = 1,
        CapabilityCallerRole role = CapabilityCallerRole.Standard,
        EntityId? contractId = null,
        EntityId? quoteId = null,
        EntityId? documentId = null) =>
        new(validatedContractCount, role, contractId, quoteId, documentId);

    [Fact]
    public void Benchmark_intent_resolves_to_quote_check()
    {
        var actions = _routing.ResolveActions([CapabilityIntent.Benchmark], ContextWith());

        Assert.Contains(actions, a => a.Href == "/quotes" && a.Kind == CopilotActionKind.Navigate);
    }

    [Fact]
    public void Benchmark_intent_also_offers_contract_360_when_a_contract_is_known()
    {
        var contractId = EntityId.New();

        var actions = _routing.ResolveActions([CapabilityIntent.Benchmark], ContextWith(contractId: contractId));

        Assert.Contains(actions, a => a.Href == $"/contracts/{contractId}");
    }

    [Fact]
    public void Benchmark_intent_does_not_offer_contract_360_when_no_contract_is_known()
    {
        var actions = _routing.ResolveActions([CapabilityIntent.Benchmark], ContextWith());

        Assert.DoesNotContain(actions, a => a.Href.StartsWith("/contracts/", StringComparison.Ordinal));
    }

    [Fact]
    public void Unknown_supplier_intent_resolves_to_upload_in_documents()
    {
        var actions = _routing.ResolveActions([CapabilityIntent.UnknownSupplier], ContextWith());

        var action = Assert.Single(actions);
        Assert.Equal("Upload in Documents", action.Label);
        Assert.Equal("/documents", action.Href);
        Assert.Equal(CopilotActionKind.Upload, action.Kind);
    }

    [Fact]
    public void Deadline_intent_resolves_to_renewals()
    {
        var actions = _routing.ResolveActions([CapabilityIntent.Deadline], ContextWith());

        var action = Assert.Single(actions);
        Assert.Equal("/renewals", action.Href);
    }

    [Fact]
    public void Savings_intent_resolves_to_savings()
    {
        var actions = _routing.ResolveActions([CapabilityIntent.Savings], ContextWith());

        var action = Assert.Single(actions);
        Assert.Equal("/savings", action.Href);
    }

    [Fact]
    public void How_to_intent_resolves_to_the_named_capability()
    {
        var actions = _routing.ResolveActions(
            [CapabilityIntent.HowTo("documents-attention")], ContextWith());

        var action = Assert.Single(actions);
        Assert.Equal("/documents?filter=attention", action.Href);
    }

    [Fact]
    public void How_to_with_an_unknown_capability_key_throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _routing.ResolveActions([CapabilityIntent.HowTo("not-a-capability")], ContextWith()));
    }

    [Fact]
    public void Capability_list_intent_resolves_to_renewals_portfolio_and_quote_check_in_order()
    {
        var actions = _routing.ResolveActions([CapabilityIntent.CapabilityList], ContextWith());

        Assert.Equal(["/renewals", "/contracts", "/quotes"], actions.Select(a => a.Href));
    }

    [Fact]
    public void Zero_validated_contracts_replaces_a_gated_action_with_the_upload_action()
    {
        var actions = _routing.ResolveActions(
            [CapabilityIntent.Deadline], ContextWith(validatedContractCount: 0));

        var action = Assert.Single(actions);
        Assert.Equal("Upload a contract", action.Label);
        Assert.Equal("/documents", action.Href);
        Assert.Equal(CopilotActionKind.Upload, action.Kind);
    }

    [Fact]
    public void Empty_state_copy_matches_the_prototype_verbatim()
    {
        Assert.Equal(
            "The portfolio lights up from validated contracts. Upload one to start.",
            CapabilityRouting.ValidatedContractsEmptyStateCopy);
    }

    [Fact]
    public void Capability_list_with_zero_validated_contracts_collapses_to_one_upload_action()
    {
        var actions = _routing.ResolveActions(
            [CapabilityIntent.CapabilityList], ContextWith(validatedContractCount: 0));

        var action = Assert.Single(actions);
        Assert.Equal("Upload a contract", action.Label);
    }

    [Fact]
    public void Renewals_action_deep_links_to_a_known_contract()
    {
        var contractId = EntityId.New();

        var actions = _routing.ResolveActions([CapabilityIntent.Deadline], ContextWith(contractId: contractId));

        var action = Assert.Single(actions);
        Assert.Equal($"/renewals?select={contractId}", action.Href);
    }

    [Fact]
    public void Quote_check_action_deep_links_to_a_known_quote()
    {
        var quoteId = EntityId.New();

        var actions = _routing.ResolveActions([CapabilityIntent.Benchmark], ContextWith(quoteId: quoteId));

        Assert.Contains(actions, a => a.Href == $"/quotes/{quoteId}");
    }

    [Fact]
    public void No_resolved_href_ever_contains_an_unresolved_placeholder()
    {
        var context = ContextWith(
            contractId: EntityId.New(), quoteId: EntityId.New(), documentId: EntityId.New());

        var actions = _routing.ResolveActions(
            [
                CapabilityIntent.Benchmark,
                CapabilityIntent.UnknownSupplier,
                CapabilityIntent.Deadline,
                CapabilityIntent.Savings,
                CapabilityIntent.CapabilityList,
                CapabilityIntent.HowTo("documents-review"),
                CapabilityIntent.HowTo("contract-360"),
            ],
            context);

        Assert.NotEmpty(actions);
        Assert.All(actions, a => Assert.DoesNotContain('{', a.Href));
    }

    [Fact]
    public void How_to_targeting_a_parameterized_capability_without_its_id_throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            _routing.ResolveActions([CapabilityIntent.HowTo("contract-360")], ContextWith()));
    }

    [Fact]
    public void Non_admin_caller_receives_no_action_for_an_admin_gated_how_to()
    {
        var actions = _routing.ResolveActions(
            [CapabilityIntent.HowTo("workspace-members")],
            ContextWith(role: CapabilityCallerRole.Standard));

        Assert.Empty(actions);
    }

    [Fact]
    public void Admin_caller_receives_the_workspace_members_action()
    {
        var actions = _routing.ResolveActions(
            [CapabilityIntent.HowTo("workspace-members")],
            ContextWith(role: CapabilityCallerRole.Admin));

        var action = Assert.Single(actions);
        Assert.Equal("/workspace/members", action.Href);
    }

    [Fact]
    public void ResolveActions_rejects_null_intents()
    {
        Assert.Throws<ArgumentNullException>(() => _routing.ResolveActions(null!, ContextWith()));
    }

    [Fact]
    public void ResolveActions_rejects_null_context()
    {
        Assert.Throws<ArgumentNullException>(() => _routing.ResolveActions([CapabilityIntent.Savings], null!));
    }

    /// <summary>Proves "Register the catalog in AddChatModule" (task coding objective): unlike
    /// <c>Raffa.Chat.Tests.ServiceCollectionExtensionsTests</c> (task E13/F08/US01/T01's own
    /// "Files to create or modify" table does not list that file — this task is not its writer),
    /// this needs no <c>IAiGateway</c>/<c>IAuditWriter</c> fakes: <see cref="CapabilityRouting"/>
    /// has no dependency, so a bare, unvalidated container resolves it.</summary>
    [Fact]
    public void AddChatModule_registers_CapabilityRouting()
    {
        var services = new ServiceCollection();
        services.AddChatModule();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<CapabilityRouting>());
    }
}
