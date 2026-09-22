using Raffa.Api.Tests.TestSupport;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain;
using Raffa.Renewals.Application;
using Raffa.Renewals.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// Task E31/F03/US01/T01 (q3-persist; NW-97; ADR-024 w19 cl. 21/ADR-028/ADR-012 cl. 53-55/ADR-020
/// 40; parent story us-01-q3-persist AC-2/AC-3): proves the one piece of the Q3 answer no earlier
/// task's tests cover — the server-injected <c>/renewals?select={id}</c>
/// <see cref="CopilotActionKind.Navigate"/> action <see cref="AskCopilotService
/// .BuildInDomainReplyAsync"/>'s own <c>isQ3PersistTurn</c> branch now adds to
/// <see cref="CopilotReply.Actions"/>, built through <see cref="CapabilityRouting.ResolveActions"/>
/// (never <c>composed.Value.Result.ActionKeys</c>, the model's own action keys —
/// <see cref="FixtureAiGateway.AnswerFromPack"/>'s own pack-JSON branch always returns an empty
/// <c>ActionKeys</c>, so before this task every <see cref="AskIntent.RenewalStrategy"/> named-contract
/// reply's own <see cref="CopilotReply.Actions"/> was necessarily empty).
///
/// <para>
/// Persist-all / chat-top-3 / todo-idempotency are proven elsewhere and are not re-proven here:
/// <see cref="AskNegotiationPointsPackTests"/> proves the shared ranking+persist helper directly,
/// <see cref="AskRenewalStrategyTodoUpsertTests"/> proves a live Q3 turn's persisted rows survive a
/// repeat ask and a manual tick. This class adds the one assertion neither makes — the reply's own
/// <c>actions[]</c> — including that a second live ask never renders the identical deep-link twice,
/// the same "Record equality" de-dup <see cref="CapabilityRouting.ResolveActions"/> itself already
/// relies on, now exercised across the two action lists <see cref="AskCopilotService
/// .BuildInDomainReplyAsync"/> combines.
/// </para>
///
/// <para>
/// Same direct-DI-resolution shape as <see cref="AskRenewalStrategyTodoUpsertTests"/> (both
/// InMemory-backed, no real Postgres connection): calls <see cref="AskCopilotService.AskAsync"/>
/// itself, since the assertions below need the reply's own <see cref="CopilotReply.Actions"/>, not
/// the HTTP JSON envelope.
/// </para>
/// </summary>
public sealed class AskQ3RenewalsDeepLinkActionTests : IClassFixture<RaffaApiFactory>
{
    private const string Actor = "test-actor@acme.example";
    private const string SupplierName = "AsterCloud GmbH";
    private const string Question = $"What should we negotiate before the {SupplierName} renewal?";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly EntityId _supplierId = EntityId.New();

    public AskQ3RenewalsDeepLinkActionTests(RaffaApiFactory factory)
    {
        _factory = factory
            .WithInMemoryAskEngine(new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()))
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<ISupplierNameLookup>(
                    new StubSupplierNameLookup(new Dictionary<EntityId, string> { [_supplierId] = SupplierName }))));
    }

    /// <summary>AC-2: "the answer carries ... a server-injected /renewals?select={guid} action"
    /// (parent story text, verbatim) — <see cref="CapabilityCatalog.RenewalsKey"/>'s own href shape,
    /// selecting exactly the contract this Q3 turn just ranked and upserted.</summary>
    [Fact]
    public async Task A_live_Q3_turn_injects_the_renewals_select_action()
    {
        var (tenantId, contractId) = await SeedNegotiableContractAsync();

        var reply = await AskAsync(tenantId);

        Assert.Equal(ReplyKind.Answer, reply.Kind);

        var action = Assert.Single(
            reply.Actions, a => a.Kind == CopilotActionKind.Navigate && a.Href.StartsWith("/renewals?select=", StringComparison.Ordinal));
        Assert.Equal($"/renewals?select={contractId}", action.Href);
    }

    /// <summary>AC-3 extended to the reply's own actions, not just the persisted rows
    /// (<see cref="AskRenewalStrategyTodoUpsertTests"/> already proves the rows): a repeat ask must
    /// never render the same deep-link twice — proves the <c>Concat(...).Distinct()</c> merge this
    /// task's own implementation adds is itself idempotent, not just the upsert underneath it.</summary>
    [Fact]
    public async Task A_repeat_Q3_turn_injects_the_action_exactly_once()
    {
        var (tenantId, contractId) = await SeedNegotiableContractAsync();

        await AskAsync(tenantId);
        var reply = await AskAsync(tenantId);

        Assert.Equal(ReplyKind.Answer, reply.Kind);

        var action = Assert.Single(
            reply.Actions, a => a.Kind == CopilotActionKind.Navigate && a.Href.StartsWith("/renewals?select=", StringComparison.Ordinal));
        Assert.Equal($"/renewals?select={contractId}", action.Href);
    }

    /// <summary>The injected action names this turn's own resolved contract, never a stale or
    /// another tenant's id: two tenants sharing the identical supplier name and question each get
    /// their own contract's id — tenant scoping alone (<see cref="PortfolioQueryService"/>'s own
    /// per-tenant fetch inside <see cref="AskCopilotService.AskAsync"/>) is what keeps the two apart,
    /// exactly the same backstop RLS gives every other per-tenant read in this file.</summary>
    [Fact]
    public async Task The_injected_action_names_this_turns_own_contract_not_a_stale_one()
    {
        var (firstTenantId, firstContractId) = await SeedNegotiableContractAsync();
        var (secondTenantId, secondContractId) = await SeedNegotiableContractAsync();

        var firstReply = await AskAsync(firstTenantId);
        var secondReply = await AskAsync(secondTenantId);

        var firstAction = Assert.Single(firstReply.Actions, a => a.Kind == CopilotActionKind.Navigate);
        var secondAction = Assert.Single(secondReply.Actions, a => a.Kind == CopilotActionKind.Navigate);

        Assert.Equal($"/renewals?select={firstContractId}", firstAction.Href);
        Assert.Equal($"/renewals?select={secondContractId}", secondAction.Href);
    }

    // ----- shared scaffolding -----

    /// <summary>Same seed shape as <see cref="AskRenewalStrategyTodoUpsertTests.SeedNegotiableContractAsync"/>
    /// (independently owned per that file's own "each test file owns its own copy" convention): one
    /// validated, auto-renewing contract with a 60-day notice window — enough to ground exactly the
    /// <c>auto-renew-short-notice</c> point and reach the live Q3 branch this class exercises. Every
    /// call uses the one <see cref="_supplierId"/>/<see cref="SupplierName"/> pair the constructor
    /// registered with the stub lookup; two calls in the same test use two different
    /// <see cref="TenantId"/>s instead (tenant scoping is what keeps them apart — see
    /// <see cref="The_injected_action_names_this_turns_own_contract_not_a_stale_one"/>).</summary>
    private async Task<(TenantId TenantId, EntityId ContractId)> SeedNegotiableContractAsync()
    {
        var now = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();

        var contract = PortfolioEndpointTests.NewContract(
            tenantId, now, supplierId: _supplierId, autoRenewal: true,
            endDate: DateOnly.FromDateTime(now.UtcDateTime).AddDays(90));
        contract.CancellationDeadline = DateOnly.FromDateTime(now.UtcDateTime).AddDays(30);

        await _factory.SeedContractAsync(contract);
        await _factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        return (tenantId, contract.Id);
    }

    private async Task<CopilotReply> AskAsync(TenantId tenantId)
    {
        using var scope = _factory.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var askCopilotService = scope.ServiceProvider.GetRequiredService<AskCopilotService>();

        using var tenantScope = tenantContext.BeginScope(tenantId);
        return await askCopilotService.AskAsync(
            tenantId, Question, recentTurns: [], Actor, scopeContractId: null, cancellationToken: CancellationToken.None);
    }
}
