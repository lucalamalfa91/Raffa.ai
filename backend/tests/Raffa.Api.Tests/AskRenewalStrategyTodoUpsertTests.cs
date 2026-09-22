using Raffa.Api.Tests.TestSupport;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
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
/// Task E29/F02/US01/T01 (todo-host-upsert; NW-85/NW-97; ADR-028/ADR-024 w19 cl. 21; parent story
/// us-01-todo-host-upsert AC-1/AC-2): proves the host-upsert wiring this task adds to
/// <see cref="AskCopilotService.BuildInDomainReplyAsync"/> (the <see cref="AskIntent.RenewalStrategy"/>
/// named-contract branch, via the new <c>BuildRenewalStrategyAndNegotiationTodosPackAsync</c>) over a
/// REAL <see cref="AskCopilotService.AskAsync"/> turn — not the direct
/// <see cref="AskCopilotService.BuildNegotiationPointsPackAsync"/> call
/// <see cref="AskNegotiationPointsPackTests"/> already uses. That class proves the shared helper's
/// own persist-all/chat-top-3/idempotent contract in isolation (its own doc comment: "no intent
/// dispatches to it yet"); this class proves a live Q3 ask actually reaches it now, so
/// <c>GET /api/renewals/{id}/negotiation-todos</c> (and therefore <c>/renewals?select={id}</c>) is
/// genuinely populated after asking — the deep-link this task's own coding objective names.
///
/// <para>
/// Same direct-DI-resolution shape as <see cref="AskNegotiationPointsPackTests"/> and
/// <see cref="AskPricedLinesParityTests"/> (both InMemory-backed, no real Postgres connection): calls
/// <see cref="AskCopilotService.AskAsync"/> itself rather than a full HTTP round trip, since the
/// assertions below only need the reply's <see cref="ReplyKind"/> and the persisted TODO rows, not
/// the HTTP JSON envelope <see cref="AskSupplierResolutionTests"/> asserts on. The seeded contract
/// grounds exactly one negotiation point (auto-renew + short notice) — enough to prove the
/// ordering/idempotency/Done-survives wiring this task owns; ranking breadth and the "top 3 of many"
/// split are already proven by <see cref="AskNegotiationPointsPackTests"/> and
/// <c>Raffa.Insights.Tests.NegotiationPointRankerTests</c>, not re-proven here.
/// </para>
/// </summary>
public sealed class AskRenewalStrategyTodoUpsertTests : IClassFixture<RaffaApiFactory>
{
    private const string Actor = "test-actor@acme.example";
    private const string SupplierName = "AsterCloud GmbH";
    private const string Question = $"What should we negotiate before the {SupplierName} renewal?";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly EntityId _supplierId = EntityId.New();

    public AskRenewalStrategyTodoUpsertTests(RaffaApiFactory factory)
    {
        _factory = factory
            .WithInMemoryAskEngine(new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()))
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<ISupplierNameLookup>(
                    new StubSupplierNameLookup(new Dictionary<EntityId, string> { [_supplierId] = SupplierName }))));
    }

    /// <summary>AC-1: "the host upserts all ranked points after rank and before the answer call."
    /// Proven here as an observable outcome — the row exists once <see cref="AskCopilotService
    /// .AskAsync"/> returns — rather than by instrumenting the call order directly, since the upsert
    /// is a durable write awaited inside pack composition, strictly before
    /// <see cref="Raffa.Chat.Application.Answering.AnswerComposer.AnswerAsync"/> is ever invoked (see
    /// the implementation's own doc comment on <c>BuildRenewalStrategyAndNegotiationTodosPackAsync</c>).</summary>
    [Fact]
    public async Task A_live_Q3_turn_upserts_todos_before_the_answer()
    {
        var (tenantId, contractId) = await SeedNegotiableContractAsync();

        var reply = await AskAsync(tenantId);

        Assert.Equal(ReplyKind.Answer, reply.Kind);

        var persisted = await GetTodosAsync(tenantId, contractId);
        Assert.NotEmpty(persisted);
        Assert.All(persisted, p => Assert.Equal(RenewalNegotiationTodoStatus.Open, p.Status));
        Assert.Contains(persisted, p => p.PointKey == "auto-renew-short-notice");
    }

    /// <summary>AC-2 half 1: "re-ask adds no duplicate Open" — a second live Q3 turn against the
    /// identical contract reconciles onto the same rows (same <c>point_key</c>), never growing the
    /// table.</summary>
    [Fact]
    public async Task A_repeat_Q3_turn_adds_no_duplicate_open_todo()
    {
        var (tenantId, contractId) = await SeedNegotiableContractAsync();

        await AskAsync(tenantId);
        var firstRun = await GetTodosAsync(tenantId, contractId);

        await AskAsync(tenantId);
        var secondRun = await GetTodosAsync(tenantId, contractId);

        Assert.Equal(firstRun.Count, secondRun.Count);
        Assert.Equal(
            firstRun.Select(p => p.PointKey).OrderBy(k => k, StringComparer.Ordinal),
            secondRun.Select(p => p.PointKey).OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(secondRun, p => Assert.Equal(RenewalNegotiationTodoStatus.Open, p.Status));
    }

    /// <summary>AC-2 half 2: "preserves Done" — a point Procurement already ticked survives a repeat
    /// ask untouched, proven through the live turn rather than directly against
    /// <see cref="RenewalNegotiationTodoService.UpsertAsync"/> (already covered by
    /// <c>Raffa.Renewals.Tests.RenewalNegotiationTodoServiceTests</c>) so this class also proves the
    /// host wiring itself never routes around that guarantee.</summary>
    [Fact]
    public async Task A_ticked_done_todo_survives_a_repeat_Q3_turn()
    {
        var (tenantId, contractId) = await SeedNegotiableContractAsync();

        await AskAsync(tenantId);
        var firstRun = await GetTodosAsync(tenantId, contractId);
        var pointKey = Assert.Single(firstRun).PointKey;

        using (var scope = _factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<RenewalNegotiationTodoService>();
            var tickResult = await service.SetDoneAsync(tenantId, contractId, pointKey, Actor);
            Assert.True(tickResult.IsSuccess);
        }

        await AskAsync(tenantId);
        var secondRun = await GetTodosAsync(tenantId, contractId);

        var tickedRow = Assert.Single(secondRun, p => p.PointKey == pointKey);
        Assert.Equal(RenewalNegotiationTodoStatus.Done, tickedRow.Status);
    }

    // ----- shared scaffolding -----

    /// <summary>Seeds one validated, auto-renewing contract whose renewal date is 90 days out and
    /// whose cancellation deadline is 30 days out -- a 60-day notice window, at
    /// <c>NegotiationPointRanker</c>'s own short-notice threshold, grounding exactly the
    /// <c>auto-renew-short-notice</c> point (the same values
    /// <see cref="AskNegotiationPointsPackTests"/> seeds for the identical topic). No priced lines,
    /// risks or payment terms, so no other of the six canonical topics grounds -- this class only
    /// needs one point to prove the ordering/idempotency wiring it owns.</summary>
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

    private async Task<IReadOnlyList<RenewalNegotiationTodoResult>> GetTodosAsync(TenantId tenantId, EntityId contractId)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RenewalNegotiationTodoService>();
        return await service.GetAsync(tenantId, contractId, CancellationToken.None);
    }
}
