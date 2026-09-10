using System.Net;
using Raffa.Savings.Application;
using Raffa.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E04/F04/US01/T01 (r3-integration) — the isolation half of
/// parent story us-01-final-integration's R3 proof (AC-2 "SavingsOpportunity lifecycle works
/// end-to-end on `demo`" only means something if it is also tenant-isolated; ADR-009) — across the
/// composed R3 HTTP surface: <c>GET /api/savings</c>, <c>PATCH /api/savings/{id}</c> and
/// <c>GET /api/savings/kpis</c>. Same "prove it across the whole path, not just per-module" shape
/// <see cref="R1CrossTenantIsolationTests"/>/<see cref="R2CrossTenantIsolationTests"/> already
/// established, and the same reuse of <see cref="R1EndToEndTests"/>'s HTTP helpers
/// <see cref="R2EndToEndTests"/>/<see cref="R2CrossTenantIsolationTests"/> already use.
///
/// Per-module RLS coverage already exists independently for <c>SavingsOpportunity</c>/
/// <c>RealizedSavings</c> (<c>Raffa.Savings.Tests.SavingsOpportunityRlsCrossTenantIsolationTests</c>/
/// <c>RealizedSavingsRlsCrossTenantIsolationTests</c>) — this test's own added value is that nobody
/// had yet driven <c>GET /api/savings</c>/<c>PATCH /api/savings/{id}</c>/<c>GET /api/savings/kpis</c>
/// themselves across two genuinely different tenants through the real host (<c>Raffa.Api.Tests
/// .SavingsEndpointTests</c>/<c>SavingsKpiEndpointTests</c> only exercise the 400 branches,
/// deliberately without a database — see those types' own doc comments).
/// </summary>
public sealed class R3CrossTenantIsolationTests : IClassFixture<R3IntegrationFixture>
{
    private readonly R3IntegrationFixture _fixture;

    public R3CrossTenantIsolationTests(R3IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Tenant_b_cannot_reach_tenant_as_savings_opportunity_through_any_r3_surface()
    {
        var client = _fixture.CreateClient();
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();

        EntityId opportunityIdForA;
        using (var scope = _fixture.Services.CreateScope())
        {
            var savingsOpportunityService = scope.ServiceProvider.GetRequiredService<SavingsOpportunityService>();
            var created = await savingsOpportunityService.CreateAsync(
                tenantA,
                new CreateSavingsOpportunityRequest(
                    SupplierId: EntityId.New(),
                    ContractId: EntityId.New(),
                    Type: "benchmark-price-comparison",
                    CurrentSpend: 50_000m,
                    Currency: "USD",
                    EstimatedSavingsLow: 4_000m,
                    EstimatedSavingsHigh: 9_000m,
                    Confidence: 0.85));

            Assert.True(created.IsSuccess);
            opportunityIdForA = created.Value.Id;
        }

        // GET /api/savings: tenant B's own list never includes tenant A's opportunity.
        var listAsB = await R1EndToEndTests.GetAsync(client, "/api/savings", tenantB.Value);
        Assert.Equal(HttpStatusCode.OK, listAsB.StatusCode);
        var listBodyB = await R1EndToEndTests.ParseAsync(listAsB);
        Assert.Empty(listBodyB.GetProperty("items").EnumerateArray());
        Assert.Equal(0, listBodyB.GetProperty("totalCount").GetInt32());

        // PATCH /api/savings/{id}: tenant B naming tenant A's opportunity id gets 404, not someone
        // else's opportunity updated out from under them (same rule GET /api/contracts/{id} and
        // GET /api/renewals/{id}/priority already establish for a cross-tenant id).
        var patchAsB = await R1EndToEndTests.PatchAsync(
            client, $"/api/savings/{opportunityIdForA.Value}", tenantB.Value,
            new { owner = "tenant-b-owner" });
        Assert.Equal(HttpStatusCode.NotFound, patchAsB.StatusCode);

        // GET /api/savings/kpis: tenant B's own procurement-homepage KPIs stay honestly empty —
        // tenant A's opportunity never leaks into tenant B's "Savings Identified" bucket.
        var kpisAsB = await R1EndToEndTests.GetAsync(client, "/api/savings/kpis", tenantB.Value);
        Assert.Equal(HttpStatusCode.OK, kpisAsB.StatusCode);
        var kpisBodyB = await R1EndToEndTests.ParseAsync(kpisAsB);
        Assert.Empty(kpisBodyB.GetProperty("savingsIdentified").EnumerateArray());
        Assert.Empty(kpisBodyB.GetProperty("savingsInProgress").EnumerateArray());
        Assert.Empty(kpisBodyB.GetProperty("savingsRealized").EnumerateArray());

        // Sanity check, both directions: tenant A's own reads/writes still work — the isolation
        // above is real isolation, not a broken write/read path (mirrors
        // R1CrossTenantIsolationTests/R2CrossTenantIsolationTests's own "both directions" check).
        var listAsA = await R1EndToEndTests.GetAsync(client, "/api/savings", tenantA.Value);
        Assert.Equal(HttpStatusCode.OK, listAsA.StatusCode);
        var listBodyA = await R1EndToEndTests.ParseAsync(listAsA);
        Assert.Contains(
            listBodyA.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("id").GetGuid() == opportunityIdForA.Value);

        var patchAsA = await R1EndToEndTests.PatchAsync(
            client, $"/api/savings/{opportunityIdForA.Value}", tenantA.Value,
            new { owner = "tenant-a-owner" });
        Assert.Equal(HttpStatusCode.OK, patchAsA.StatusCode);

        var kpisAsA = await R1EndToEndTests.GetAsync(client, "/api/savings/kpis", tenantA.Value);
        Assert.Equal(HttpStatusCode.OK, kpisAsA.StatusCode);
        var kpisBodyA = await R1EndToEndTests.ParseAsync(kpisAsA);
        var identifiedBucket = Assert.Single(kpisBodyA.GetProperty("savingsIdentified").EnumerateArray());
        Assert.Equal("USD", identifiedBucket.GetProperty("currency").GetString());
        Assert.Equal(1, identifiedBucket.GetProperty("count").GetInt32());
    }
}
