using System.Net;

namespace Contigo.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E02/F06/US01/T01 (r1-integration) AC-3: "Cross-tenant
/// isolation holds across the whole path" — not just within one module (already exhaustively
/// covered per-module, e.g. <c>Contigo.Documents.Contracts.Tests.PortfolioQueryServiceTests</c>,
/// and already proved for the R0 document/audit path by <c>R0CrossTenantIsolationTests</c> and for
/// Ask Contigo alone by <c>AskContigoRagCrossTenantIsolationTests</c>), but across the full R1
/// surface this task wires together — portfolio, Contract 360, correction, correction history, and
/// Ask Contigo — driven through the one real host, with two genuinely different, independently
/// created tenants sharing nothing but the same physical database (ADR-009).
/// </summary>
public sealed class R1CrossTenantIsolationTests : IClassFixture<R1IntegrationFixture>
{
    private readonly R1IntegrationFixture _fixture;

    public R1CrossTenantIsolationTests(R1IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Tenant_b_cannot_reach_tenant_as_contract_through_any_r1_surface()
    {
        var client = _fixture.CreateClient();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var (_, contractId) = await R1EndToEndTests.UploadAndProcessAsync(
            client, tenantA, R1ExtractionFixtures.BuildBornDigitalPdfBytes(),
            R1ExtractionFixtures.BornDigitalFileName, R1ExtractionFixtures.BornDigitalMimeType);

        // Portfolio: tenant B's own list never includes tenant A's contract.
        var portfolioAsB = await R1EndToEndTests.GetAsync(client, "/api/contracts", tenantB);
        Assert.Equal(HttpStatusCode.OK, portfolioAsB.StatusCode);
        var portfolioBodyB = await R1EndToEndTests.ParseAsync(portfolioAsB);
        Assert.DoesNotContain(
            portfolioBodyB.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("contractId").GetString() == contractId.ToString());

        // Contract 360: tenant B reading tenant A's contract id gets 404, not someone else's data.
        var contract360AsB = await R1EndToEndTests.GetAsync(client, $"/api/contracts/{contractId}", tenantB);
        Assert.Equal(HttpStatusCode.NotFound, contract360AsB.StatusCode);

        // Correction: tenant B cannot correct a contract it cannot see.
        var correctAsB = await R1EndToEndTests.PatchAsync(
            client, $"/api/contracts/{contractId}", tenantB,
            new { corrections = new Dictionary<string, string?> { ["annualSpend"] = "1" } });
        Assert.Equal(HttpStatusCode.NotFound, correctAsB.StatusCode);

        // Correction history: same 404, not an empty-but-200 leak of the contract's existence.
        var historyAsB = await R1EndToEndTests.GetAsync(client, $"/api/contracts/{contractId}/corrections", tenantB);
        Assert.Equal(HttpStatusCode.NotFound, historyAsB.StatusCode);

        // Ask Contigo: tenant B's own semantic query never retrieves or cites tenant A's document —
        // an authorized retrieval that genuinely finds nothing for this tenant is an honest
        // "cannot determine" (spec §8.4 "no evidence, no claim"), never tenant A's answer. Task
        // E13/F06/US01/T01 (ask-engine) replaced the old `{ canDetermine, citations }` shape with
        // the ADR-024 §6 reply contract (`kind: "abstain"` for the identical "nothing to ground"
        // case) and made `POST /api/chat/query` resolve caller identity — see
        // R1EndToEndTests.PostAsync's own doc comment on the X-User-Id overload this now needs.
        var chatAsB = await R1EndToEndTests.PostAsync(
            client, "/api/chat/query", tenantB, "bob@tenant-b.example",
            new { question = "What does the master services agreement cover?" });
        Assert.Equal(HttpStatusCode.OK, chatAsB.StatusCode);
        var chatBodyB = await R1EndToEndTests.ParseAsync(chatAsB);
        Assert.Equal("abstain", chatBodyB.GetProperty("kind").GetString());
        Assert.Equal(0, chatBodyB.GetProperty("citations").GetArrayLength());

        // Sanity check, both directions: tenant A's own reads still work — the 404s/empty results
        // above are cross-tenant isolation, not a broken write path or a coincidentally-wrong id.
        var portfolioAsA = await R1EndToEndTests.GetAsync(client, "/api/contracts", tenantA);
        Assert.Equal(HttpStatusCode.OK, portfolioAsA.StatusCode);
        var portfolioBodyA = await R1EndToEndTests.ParseAsync(portfolioAsA);
        Assert.Contains(
            portfolioBodyA.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("contractId").GetString() == contractId.ToString());

        var contract360AsA = await R1EndToEndTests.GetAsync(client, $"/api/contracts/{contractId}", tenantA);
        Assert.Equal(HttpStatusCode.OK, contract360AsA.StatusCode);

        var chatAsA = await R1EndToEndTests.PostAsync(
            client, "/api/chat/query", tenantA, "alice@tenant-a.example",
            new { question = "What does the master services agreement cover?" });
        var chatBodyA = await R1EndToEndTests.ParseAsync(chatAsA);
        Assert.Equal("answer", chatBodyA.GetProperty("kind").GetString());
        Assert.NotEmpty(chatBodyA.GetProperty("citations").EnumerateArray());
    }
}
