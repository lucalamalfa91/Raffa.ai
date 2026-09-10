using System.Net;
using Raffa.Renewals.Application;
using Raffa.SharedKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E03/F04/US01/T01 (r2-integration) — the isolation half of
/// parent story us-01-final-integration AC-3 ("Pipeline + insight card work on `demo` with tenant
/// isolation") — across the composed R2 HTTP surface: <c>GET /api/renewals</c>,
/// <c>GET /api/renewals/{id}/priority</c>, and <c>POST /api/renewals/{id}/action</c>. Same
/// "prove it across the whole path, not just per-module" shape
/// <see cref="R1CrossTenantIsolationTests"/> already established for R1, and the same reuse of
/// <see cref="R1EndToEndTests"/>'s HTTP helpers <see cref="R2EndToEndTests"/> already uses.
///
/// Per-module RLS coverage already exists independently for each layer this composes
/// (<c>Raffa.Documents.Contracts.Tests.PortfolioQueryServiceTests.Different_tenant_sees_no_rows</c>,
/// <c>Raffa.Renewals.Tests.RenewalActionRlsCrossTenantIsolationTests</c>) — this test's own added
/// value is that nobody had yet driven <c>GET /api/renewals</c>/<c>GET
/// /api/renewals/{id}/priority</c> themselves across two genuinely different tenants through the
/// real host (<c>Raffa.Api.Tests.RenewalsEndpointTests</c> only exercises the 400 branches,
/// deliberately without a database — see that type's own doc comment).
/// </summary>
public sealed class R2CrossTenantIsolationTests : IClassFixture<R2IntegrationFixture>
{
    private readonly R2IntegrationFixture _fixture;

    public R2CrossTenantIsolationTests(R2IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Tenant_b_cannot_reach_tenant_as_renewal_through_any_r2_surface()
    {
        var client = _fixture.CreateClient();
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        var contractA = await _fixture.SeedContractAsync(
            tenantA, annualSpend: 50_000m, endDate: today.AddDays(45), autoRenewal: true);

        // GET /api/renewals: tenant B's own pipeline never includes tenant A's contract.
        var pipelineAsB = await R1EndToEndTests.GetAsync(client, "/api/renewals", tenantB.Value);
        Assert.Equal(HttpStatusCode.OK, pipelineAsB.StatusCode);
        var pipelineBodyB = await R1EndToEndTests.ParseAsync(pipelineAsB);
        Assert.DoesNotContain(
            pipelineBodyB.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("contractId").GetGuid() == contractA.Id.Value);

        // GET /api/renewals/{id}/priority: tenant B reading tenant A's contract id gets 404, not
        // someone else's priority score (same rule as GET /api/contracts/{id}).
        var priorityAsB = await R1EndToEndTests.GetAsync(
            client, $"/api/renewals/{contractA.Id.Value}/priority", tenantB.Value);
        Assert.Equal(HttpStatusCode.NotFound, priorityAsB.StatusCode);

        // POST /api/renewals/{id}/action: RenewalActionService cannot check whether {id} names an
        // existing, tenant-owned contract at all (ADR-002 forbids Raffa.Renewals from referencing
        // Raffa.Documents.Contracts — see that service's own doc comment for the honest gap this
        // leaves), so tenant B's well-formed action against tenant A's contractId still upserts —
        // scoped to tenant B's own row, never tenant A's (RLS plus the explicit TenantId filter).
        var setActionAsA = await R1EndToEndTests.PostAsync(
            client, $"/api/renewals/{contractA.Id.Value}/action", tenantA.Value,
            new { owner = "tenant-a-owner", status = "InProgress", action = "Tenant A's own action" });
        Assert.Equal(HttpStatusCode.OK, setActionAsA.StatusCode);

        var setActionAsB = await R1EndToEndTests.PostAsync(
            client, $"/api/renewals/{contractA.Id.Value}/action", tenantB.Value,
            new { owner = "tenant-b-owner", status = "Completed", action = "Tenant B's own action" });
        Assert.Equal(HttpStatusCode.OK, setActionAsB.StatusCode);

        // No GET route exists for this yet (RenewalActionService's own doc comment) — read back
        // through the same service the host resolves, the same shape R2EndToEndTests already uses.
        using (var scope = _fixture.Services.CreateScope())
        {
            var actionService = scope.ServiceProvider.GetRequiredService<RenewalActionService>();

            var actionForA = await actionService.GetActionAsync(tenantA, contractA.Id);
            var actionForB = await actionService.GetActionAsync(tenantB, contractA.Id);

            // Two independent rows for the same ContractId value, one per tenant — never merged,
            // never leaked, even though nothing in this module can tell they "really" name the
            // same contract only for tenant A.
            Assert.NotNull(actionForA);
            Assert.Equal("tenant-a-owner", actionForA!.Owner);
            Assert.NotNull(actionForB);
            Assert.Equal("tenant-b-owner", actionForB!.Owner);
        }

        // Sanity check, both directions: tenant A's own reads still work — the isolation above is
        // real isolation, not a broken write/read path (mirrors R1CrossTenantIsolationTests's own
        // "both directions" check).
        var pipelineAsA = await R1EndToEndTests.GetAsync(client, "/api/renewals", tenantA.Value);
        Assert.Equal(HttpStatusCode.OK, pipelineAsA.StatusCode);
        var pipelineBodyA = await R1EndToEndTests.ParseAsync(pipelineAsA);
        Assert.Contains(
            pipelineBodyA.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("contractId").GetGuid() == contractA.Id.Value);

        var priorityAsA = await R1EndToEndTests.GetAsync(
            client, $"/api/renewals/{contractA.Id.Value}/priority", tenantA.Value);
        Assert.Equal(HttpStatusCode.OK, priorityAsA.StatusCode);
    }
}
