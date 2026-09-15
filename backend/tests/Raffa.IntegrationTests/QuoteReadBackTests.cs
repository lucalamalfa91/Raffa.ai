using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E19/F02/US01/T01 (quote-read-api) — parent story
/// us-01-quote-read-api AC-1 (<c>GET /api/quotes</c> is tenant-scoped), AC-2 (<c>GET
/// /api/quotes/{id}</c> embeds recorded outcomes, newest first, empty list rather than 404 for
/// none), AC-3 (404 unknown id, 400 non-GUID) and AC-4 (cross-tenant read is 404 with zero leaked
/// fields) — end to end, over real HTTP through the real <c>Raffa.Api</c> composition root, against
/// a real, migrated Postgres+RLS database (see <see cref="QuoteIntegrationFixture"/>). AC-5/AC-6 are
/// proved elsewhere by design, not by a test in this class: AC-5 by
/// <c>R4EndToEndTests</c>'s own unchanged assertions on the assessment/recalculate shapes (this task
/// added no field to either), and AC-6 by this task's own Definition of Done grep (there is no
/// route to exercise). Mirrors <see cref="QuoteEndToEndTests"/>/
/// <see cref="NegotiationOutcomePropagationEndToEndTests"/>'s own shape: one real host, no
/// hand-rolled container, no mocked <c>QuoteQueryService</c>.
///
/// <para>
/// Reads response bodies as raw <see cref="JsonElement"/>s rather than typed DTOs — same reason as
/// <c>QuoteEndToEndTests</c>'s own doc comment: every endpoint under test serializes anonymous
/// objects with ASP.NET Core's default camelCase policy.
/// </para>
/// </summary>
public sealed class QuoteReadBackTests : IClassFixture<QuoteIntegrationFixture>
{
    private readonly QuoteIntegrationFixture _fixture;

    public QuoteReadBackTests(QuoteIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetQuote_embeds_recorded_outcomes_newest_first()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();
        var quoteId = await SeedQuoteAsync(tenantId);

        // Two sequential, real captures through the write path
        // NegotiationOutcomePropagationEndToEndTests already proves end to end -- each await
        // happens strictly after the previous one completes, so CapturedAt (caller-request-time,
        // NegotiationOutcome's own doc comment) is strictly increasing between them.
        await CaptureOutcomeAsync(client, tenantId, quoteId, finalPrice: 500_000m);
        await CaptureOutcomeAsync(client, tenantId, quoteId, finalPrice: 480_000m);

        var quote = await GetQuoteAsync(client, tenantId, quoteId);

        Assert.Equal(quoteId, quote.GetProperty("id").GetGuid());
        var outcomes = quote.GetProperty("outcomes").EnumerateArray().ToList();
        Assert.Equal(2, outcomes.Count);
        // Newest first (AC-2): the second capture (480k) is the most recent.
        Assert.Equal(480_000m, outcomes[0].GetProperty("finalPrice").GetDecimal());
        Assert.Equal(500_000m, outcomes[1].GetProperty("finalPrice").GetDecimal());
    }

    [Fact]
    public async Task GetQuote_returns_an_empty_outcomes_list_not_404_when_none_recorded()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();
        var quoteId = await SeedQuoteAsync(tenantId);

        var quote = await GetQuoteAsync(client, tenantId, quoteId);

        Assert.Equal(quoteId, quote.GetProperty("id").GetGuid());
        Assert.Empty(quote.GetProperty("outcomes").EnumerateArray());
    }

    [Fact]
    public async Task GetQuote_records_an_outcome_in_one_session_and_reads_it_back_in_a_different_session()
    {
        var tenantId = Guid.NewGuid();
        var quoteId = await SeedQuoteAsync(tenantId);

        // Session A: records the outcome.
        var sessionA = _fixture.CreateClient();
        await CaptureOutcomeAsync(sessionA, tenantId, quoteId, finalPrice: 435_000m);

        // Session B: a distinct HttpClient instance -- the same "a different browser tab" shape
        // ImplicitTenantAdminStartupFilter already treats as its own caller (X-Tenant-Id alone, no
        // X-User-Id, so each session idempotently resolves to the tenant's implicit Admin; see that
        // type's own doc comment) -- reads back the fact session A recorded.
        var sessionB = _fixture.CreateClient();
        var quote = await GetQuoteAsync(sessionB, tenantId, quoteId);

        var outcome = Assert.Single(quote.GetProperty("outcomes").EnumerateArray());
        Assert.Equal(435_000m, outcome.GetProperty("finalPrice").GetDecimal());
    }

    [Fact]
    public async Task GetQuote_returns_404_for_an_unknown_quote_id()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/quotes/{Guid.NewGuid()}");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetQuote_returns_400_for_a_non_guid_id()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/quotes/not-a-guid");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetQuote_for_a_different_tenant_returns_404_and_leaks_no_tenant_a_field()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var quoteId = await SeedQuoteAsync(tenantA, fileName: "tenant-a-only.pdf");

        var client = _fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/quotes/{quoteId}");
        request.Headers.Add("X-Tenant-Id", tenantB.ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("tenant-a-only.pdf", body);
    }

    [Fact]
    public async Task ListQuotes_returns_only_the_tenants_own_quotes()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var quoteAId = await SeedQuoteAsync(tenantA, fileName: "tenant-a.pdf");
        await SeedQuoteAsync(tenantB, fileName: "tenant-b.pdf");

        var client = _fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/quotes");
        request.Headers.Add("X-Tenant-Id", tenantA.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = document.RootElement.GetProperty("items").EnumerateArray().ToList();

        var item = Assert.Single(items);
        Assert.Equal(quoteAId, item.GetProperty("id").GetGuid());
        Assert.Equal("tenant-a.pdf", item.GetProperty("fileName").GetString());
    }

    private static async Task<JsonElement> GetQuoteAsync(HttpClient client, Guid tenantId, Guid quoteId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/quotes/{quoteId}");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    /// <summary>Same worked example as <c>NegotiationOutcomePropagationEndToEndTests
    /// .CaptureOutcomeAsync</c>, minus the savings-opportunity link this task's own scope does not
    /// touch (it defaults to <see langword="null"/> — <c>NegotiationOutcomeCaptureRequest</c>'s own
    /// trailing optional parameter) -- <paramref name="finalPrice"/> is the only dimension these
    /// tests vary, so each capture is independently identifiable in the newest-first assertions
    /// above.</summary>
    private static async Task CaptureOutcomeAsync(HttpClient client, Guid tenantId, Guid quoteId, decimal finalPrice)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/negotiations/outcomes")
        {
            Content = JsonContent.Create(new
            {
                quoteId,
                originalQuoteTotal = 520_000m,
                targetPrice = 420_000m,
                finalPrice,
                negotiationDurationDays = 24,
                leversUsed = new[] { "Term" },
            }),
        };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>Same shape as <c>NegotiationOutcomePropagationEndToEndTests.SeedQuoteAsync</c>,
    /// with a caller-supplied <paramref name="fileName"/> so the cross-tenant tests above can name
    /// a distinctive, assertable value.</summary>
    private async Task<Guid> SeedQuoteAsync(Guid tenantId, string fileName = "quote.pdf")
    {
        using var scope = _fixture.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));
        var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();

        var quoteId = EntityId.New();
        db.Quotes.Add(new Quote
        {
            Id = quoteId,
            TenantId = new TenantId(tenantId),
            FileName = fileName,
            MimeType = "application/pdf",
            StoragePath = $"{tenantId:D}/{fileName}",
            Checksum = "deadbeef",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return quoteId.Value;
    }
}
