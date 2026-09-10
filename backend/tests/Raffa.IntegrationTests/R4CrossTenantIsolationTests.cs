using System.Net;

namespace Raffa.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E05/F04/US01/T01 (r4-integration) — the isolation half of
/// parent story us-01-final-integration's R4 proof (ADR-009 is named in this task's own "Architecture
/// decisions in force") — across the composed R4 HTTP surface: <c>GET
/// /api/quotes/{id}/assessment</c> and <c>POST /api/negotiations/outcomes</c>. Same "prove it across
/// the whole path, not just per-module" shape <see cref="R1CrossTenantIsolationTests"/>/
/// <see cref="R2CrossTenantIsolationTests"/>/<see cref="R3CrossTenantIsolationTests"/> already
/// established, and the same reuse of <see cref="R1EndToEndTests"/>'s HTTP helpers those classes
/// already use.
///
/// Per-module RLS coverage already exists independently for <c>Quote</c>/<c>QuoteLine</c>
/// (<c>Raffa.Quotes.Tests.QuoteRlsCrossTenantIsolationTests</c>) and
/// <c>SkuProductMapping</c>/<c>NegotiationOutcome</c> (<c>SkuNormalizationServiceTests
/// .A_different_tenant_cannot_see_another_tenants_sku_product_mapping</c>,
/// <c>NegotiationOutcomeServiceTests</c>'s own cross-tenant coverage) — this test's own added value is
/// that nobody had yet driven <c>GET /api/quotes/{id}/assessment</c>/<c>POST
/// /api/negotiations/outcomes</c> themselves across two genuinely different tenants through the real
/// host (<c>Raffa.Api.Tests.QuotesEndpointTests</c>/<c>NegotiationsEndpointTests</c> only exercise
/// the 400/404 validation branches, deliberately without a database — the same convention those
/// classes' own sibling doc comments already document for every other endpoint-test class in this
/// solution). It is also the first test to exercise <c>GET /api/quotes/{id}/assessment</c> at all
/// against a real, <b>unprivileged</b> Postgres role — see <see cref="R4EndToEndTests"/>'s own doc
/// comment for the tenant-scope bug that endpoint carried until this task fixed it.
/// </summary>
public sealed class R4CrossTenantIsolationTests : IClassFixture<R4IntegrationFixture>
{
    private readonly R4IntegrationFixture _fixture;

    public R4CrossTenantIsolationTests(R4IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Tenant_b_cannot_reach_tenant_as_quote_assessment_or_capture_an_outcome_against_it()
    {
        var client = _fixture.CreateClient();
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();

        var uploadBody = await R4EndToEndTests.UploadQuoteAsync(client, tenantA);
        var quoteIdForA = uploadBody.GetProperty("id").GetGuid();

        // GET /api/quotes/{id}/assessment: tenant B naming tenant A's quote id gets 404, not tenant
        // A's own market position/target-saving/benchmark data leaked to a stranger — same rule
        // GET /api/contracts/{id} and GET /api/renewals/{id}/priority already establish for a
        // cross-tenant id.
        var assessmentAsB = await R1EndToEndTests.GetAsync(
            client, $"/api/quotes/{quoteIdForA}/assessment", tenantB);
        Assert.Equal(HttpStatusCode.NotFound, assessmentAsB.StatusCode);

        // POST /api/negotiations/outcomes: tenant B naming tenant A's quote id gets 404
        // (NegotiationOutcomeService.QuoteNotFoundError), never a negotiation outcome recorded
        // against a quote tenant B does not own.
        var captureAsB = await R1EndToEndTests.PostAsync(
            client, "/api/negotiations/outcomes", tenantB,
            new
            {
                quoteId = quoteIdForA,
                originalQuoteTotal = 220_000m,
                finalPrice = 190_000m,
                negotiationDurationDays = 5,
                leversUsed = new[] { "Volume" },
            });
        Assert.Equal(HttpStatusCode.NotFound, captureAsB.StatusCode);

        // Sanity check, both directions: tenant A's own reads/writes still work — the isolation
        // above is real isolation, not a broken path (mirrors
        // R1CrossTenantIsolationTests/R2CrossTenantIsolationTests/R3CrossTenantIsolationTests's own
        // "both directions" check).
        var assessmentAsA = await R1EndToEndTests.GetAsync(
            client, $"/api/quotes/{quoteIdForA}/assessment", tenantA);
        Assert.Equal(HttpStatusCode.OK, assessmentAsA.StatusCode);
        var assessmentBodyForA = await R1EndToEndTests.ParseAsync(assessmentAsA);
        var lineForA = Assert.Single(assessmentBodyForA.GetProperty("lines").EnumerateArray());
        Assert.Equal("Assessed", lineForA.GetProperty("status").GetString());

        var captureAsA = await R1EndToEndTests.PostAsync(
            client, "/api/negotiations/outcomes", tenantA,
            new
            {
                quoteId = quoteIdForA,
                originalQuoteTotal = 220_000m,
                finalPrice = 190_000m,
                negotiationDurationDays = 5,
                leversUsed = new[] { "Volume" },
            });
        Assert.Equal(HttpStatusCode.Created, captureAsA.StatusCode);
    }
}
