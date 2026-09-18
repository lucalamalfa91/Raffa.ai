using Raffa.Api.Tests.TestSupport;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Chat.Application.Pack;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Renewals.Application;
using Raffa.Renewals.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// Reviewer gap on task E31/F02/US01/T01 (point-ranker; NW-96; ADR-024 w19 cl. 23; parent story
/// us-01-point-ranker AC-3, "chat cap top 3; persist all via the shared helper"):
/// <see cref="AskCopilotService.BuildNegotiationPointsPackAsync"/>'s own doc comment claims exactly
/// that split, but before this class no test anywhere called it -- <c>NegotiationPointRankerTests</c>
/// (<c>Raffa.Insights.Tests</c>) only exercises the pure <c>NegotiationPointRanker.Rank</c>, which
/// never touches <see cref="RenewalNegotiationTodoService"/>. This file's own seeded contract grounds
/// five of the six canonical points precisely so "top 3" and "all of them" are never the same number
/// -- a contract with three or fewer grounded points could never distinguish the two halves.
///
/// <para>
/// Same direct-DI-resolution shape as <see cref="AskPricedLinesParityTests"/> (that type's own doc
/// comment explains why: no intent dispatches to <c>BuildNegotiationPointsPackAsync</c> yet -- a
/// later task wires it into a live turn -- so an HTTP round trip through <c>AskAsync</c> cannot reach
/// it), but built on the plain <c>InMemoryAskEngineFactory.WithInMemoryAskEngine</c> host (the same
/// swap <see cref="RenewalNegotiationTodoEndpointTests"/> already uses for this exact module) rather
/// than a real-Postgres connection string: every DbContext this call path touches
/// (<see cref="DocumentsContractsDbContext"/> via <c>Contract360QueryService</c>, Identity/Workspace's
/// own context via <c>BenchmarkKeyResolution</c>, <see cref="RenewalsDbContext"/> via
/// <see cref="RenewalNegotiationTodoService"/>) is already InMemory-swapped by that helper. The seeded
/// contract also carries no <c>SupplierId</c> -- <c>AskCopilotService.ResolveDisplayNameAsync</c>/
/// <c>ResolveBenchmarkKeyAsync</c> both short-circuit on a null supplier id without ever calling
/// <c>ISupplierNameLookup</c> or <c>IBenchmarkService</c> (see those methods' own doc comments), so
/// neither port needs stubbing here. That null <c>SupplierId</c> doubles as this test's control on the
/// grounded count: the above-band-price topic needs a resolved benchmark band, which therefore never
/// happens, so exactly five of the six canonical topics ground here, never six.
/// </para>
/// </summary>
public sealed class AskNegotiationPointsPackTests : IClassFixture<RaffaApiFactory>
{
    private const string Actor = "test-actor@acme.example";

    private readonly WebApplicationFactory<Program> _factory;

    public AskNegotiationPointsPackTests(RaffaApiFactory factory)
    {
        _factory = factory.WithInMemoryAskEngine(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));
    }

    [Fact]
    public async Task Chat_pack_is_capped_at_the_top_three_ranked_points()
    {
        var (tenantId, contractId) = await SeedFiveGroundedPointsContractAsync();

        var pack = await BuildPackAsync(tenantId, contractId, persistTodos: false);

        Assert.Equal(3, pack.Count);
        Assert.All(pack, item =>
            Assert.StartsWith("calc:negotiation-point[", item.CitationKey, StringComparison.Ordinal));
        // Rank order, not insertion order -- the fixed NegotiationPointTopic declaration order
        // (NegotiationPointRankerTests pins the identical order at the pure-calculator level), with
        // the ungrounded above-band-price topic simply absent, never a gap.
        Assert.Equal("calc:negotiation-point[uncapped-or-high-liability]", pack[0].CitationKey);
        Assert.Equal("calc:negotiation-point[auto-renew-short-notice]", pack[1].CitationKey);
        Assert.Equal("calc:negotiation-point[sla-credits]", pack[2].CitationKey);
    }

    [Fact]
    public async Task Persist_todos_true_persists_the_whole_ranked_set_not_just_the_chat_top_three()
    {
        var (tenantId, contractId) = await SeedFiveGroundedPointsContractAsync();

        var pack = await BuildPackAsync(tenantId, contractId, persistTodos: true);
        Assert.Equal(3, pack.Count); // the chat cap itself is unaffected by persistTodos.

        var persisted = await GetTodosAsync(tenantId, contractId);

        // "Persist-all": every one of the five grounded points is readable back, not just the three
        // the chat pack returned above -- points ranked #4/#5 still reach /renewals?select= even
        // though chat never narrated them (BuildNegotiationPointsPackAsync's own doc comment,
        // verbatim: "so a point ranked #4 still reaches... even though chat never narrates it").
        Assert.Equal(
            new[]
            {
                "uncapped-or-high-liability",
                "auto-renew-short-notice",
                "sla-credits",
                "term-volume",
                "payment-terms",
            },
            persisted.Select(p => p.PointKey).ToArray());
        Assert.All(persisted, p => Assert.Equal(RenewalNegotiationTodoStatus.Open, p.Status));
    }

    [Fact]
    public async Task A_repeat_call_does_not_duplicate_rows()
    {
        var (tenantId, contractId) = await SeedFiveGroundedPointsContractAsync();

        await BuildPackAsync(tenantId, contractId, persistTodos: true);
        await BuildPackAsync(tenantId, contractId, persistTodos: true);

        var persisted = await GetTodosAsync(tenantId, contractId);

        // Proves the host wiring itself is idempotent end to end -- not just
        // RenewalNegotiationTodoService.UpsertAsync in isolation (already proved directly by
        // RenewalNegotiationTodoEndpointTests) -- a second Ask turn re-ranking the identical contract
        // must never grow the table.
        Assert.Equal(5, persisted.Count);
        Assert.Equal(5, persisted.Select(p => p.PointKey).Distinct(StringComparer.Ordinal).Count());
    }

    // ----- shared scaffolding -----

    /// <summary>Seeds one contract grounding exactly five of the six canonical
    /// <c>NegotiationPointTopic</c> categories -- see this type's own doc comment for why
    /// above-band-price is the one left out. Two <see cref="Risk"/> rows ground liability/SLA (the
    /// preferred grounding source over a clause -- <c>NegotiationPointRanker</c>'s own doc comment),
    /// the auto-renewal/cancellation dates ground the short-notice point, the one
    /// <see cref="ContractLineItem"/> plus <see cref="Contract.RenewalTermMonths"/> ground term/
    /// volume, and <see cref="Contract.PaymentTerms"/> grounds the payment-terms point.</summary>
    private async Task<(TenantId TenantId, EntityId ContractId)> SeedFiveGroundedPointsContractAsync()
    {
        var now = new DateTimeOffset(2026, 9, 18, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();

        var contract = PortfolioEndpointTests.NewContract(
            tenantId, now, supplierId: null, autoRenewal: true,
            endDate: DateOnly.FromDateTime(now.UtcDateTime).AddDays(90));
        // 90 - 30 = 60-day notice window -- at the short-notice threshold, so grounded (Moderate,
        // not Strong; strength is not asserted by this class).
        contract.CancellationDeadline = DateOnly.FromDateTime(now.UtcDateTime).AddDays(30);
        contract.RenewalTermMonths = 12; // grounds term-volume's term half.
        contract.PaymentTerms = "Net 30"; // grounds payment-terms.
        await _factory.SeedContractAsync(contract);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();

            db.ContractLineItems.Add(new ContractLineItem
            {
                TenantId = tenantId,
                ContractId = contract.Id,
                Sku = "SKU-1",
                Description = "Sales Cloud Enterprise",
                Quantity = 100m, // grounds term-volume's volume half.
                Unit = "seats",
                UnitPrice = 2300m,
                BillingPeriod = "Annual",
                Confidence = 0.9,
                CreatedAt = now,
            });

            db.Risks.Add(new Risk
            {
                TenantId = tenantId,
                ContractId = contract.Id,
                RiskType = "Uncapped liability",
                Description = "The limitation of liability clause carries no cap.",
                Severity = RiskSeverity.High,
                IdentifiedAt = now,
            });

            db.Risks.Add(new Risk
            {
                TenantId = tenantId,
                ContractId = contract.Id,
                RiskType = "Missing SLA",
                Description = "No service level credits were found in the extracted text.",
                Severity = RiskSeverity.Medium,
                IdentifiedAt = now,
            });

            await db.SaveChangesAsync();
        }

        return (tenantId, contract.Id);
    }

    private async Task<IReadOnlyList<PackItem>> BuildPackAsync(TenantId tenantId, EntityId contractId, bool persistTodos)
    {
        using var scope = _factory.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var askCopilotService = scope.ServiceProvider.GetRequiredService<AskCopilotService>();

        using var tenantScope = tenantContext.BeginScope(tenantId);
        return await askCopilotService.BuildNegotiationPointsPackAsync(
            contractId, includeRenewalUrgency: false, persistTodos, Actor, CancellationToken.None);
    }

    private async Task<IReadOnlyList<RenewalNegotiationTodoResult>> GetTodosAsync(TenantId tenantId, EntityId contractId)
    {
        using var scope = _factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<RenewalNegotiationTodoService>();
        return await service.GetAsync(tenantId, contractId, CancellationToken.None);
    }
}
