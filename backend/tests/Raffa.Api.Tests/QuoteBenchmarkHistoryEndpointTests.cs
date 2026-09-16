using System.Net;
using System.Text.Json;
using Raffa.Api.Tests.TestSupport;
using Raffa.Quotes.Domain;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E25/F04/US01/T01 (quote-benchmark-backend; parent story
/// us-01-quote-benchmark-backend AC-1/AC-2; closes NW-57) that `GET /api/quotes/benchmark-history`
/// is actually mapped in <c>Program.cs</c>, enforces its request-shape guard clause, and — unlike
/// <see cref="QuotesEndpointTests"/>'s own sibling tests, which stop at the tenant-header guard
/// clause because a real Postgres is not available in this project by default — reaches a real
/// success path over real HTTP, via <see cref="InMemoryQuotesFactory.WithInMemoryQuotesDb"/>. This is
/// the one route in this file whose entire point is to prove the cold-start and read-back behaviour
/// the task's own Definition of Done names, so a guard-clause-only proof would not actually cover it.
///
/// <para>
/// The fixture-adapter catalog (<c>Raffa.Benchmark.Fixtures.FixtureBenchmarkAdapter</c>) needs no
/// network/DB access of its own (ADR-001: never a paid external API), so it runs unmodified here —
/// only <see cref="Raffa.Quotes.Infrastructure.QuotesDbContext"/> needs the InMemory swap.
/// </para>
/// </summary>
public sealed class QuoteBenchmarkHistoryEndpointTests : IClassFixture<RaffaApiFactory>
{
    private readonly RaffaApiFactory _baseFactory;

    public QuoteBenchmarkHistoryEndpointTests(RaffaApiFactory factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task Missing_tenant_header_returns_400()
    {
        var client = _baseFactory.CreateClient();

        var response = await client.GetAsync("/api/quotes/benchmark-history");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_tenant_header_returns_400()
    {
        var client = _baseFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/quotes/benchmark-history");
        request.Headers.Add("X-Tenant-Id", "not-a-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_tenant_with_no_quotes_gets_200_with_empty_items()
    {
        var factory = _baseFactory.WithInMemoryQuotesDb();
        var tenantId = TenantId.New();

        var body = await GetHistoryAsync(factory, tenantId);

        Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("items").ValueKind);
        Assert.Empty(body.RootElement.GetProperty("items").EnumerateArray());
    }

    // ----- AC-1: an honest InsufficientBenchmarkData cold start, never a fabricated position -----

    [Fact]
    public async Task First_of_type_quote_reports_an_honest_insufficient_benchmark_data_cold_start()
    {
        var factory = _baseFactory.WithInMemoryQuotesDb();
        var tenantId = TenantId.New();
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        // "Cold start": a supplier/product combination the fixture catalog
        // (FixtureBenchmarkAdapter.Catalog) has never heard of, so neither FindStrongMatch nor
        // FindWeakMatch can find anything at all -- the strongest form of "insufficient data",
        // never a fabricated number (ADR-001; Appendix C rule 10).
        var quote = NewQuote(
            tenantId, "first-of-type.pdf", supplier: "Acme Cold Start Co", currency: "USD",
            geography: "US", purchaseDate: new DateOnly(2026, 8, 20), createdAt: now);
        await factory.SeedQuoteAsync(quote);
        await factory.SeedQuoteLineAsync(NewLine(
            tenantId, quote.Id, description: "Totally Unlisted Widget", quantity: 10m,
            term: "12 months", unitPrice: 42m, createdAt: now));

        var body = await GetHistoryAsync(factory, tenantId);

        var entry = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(quote.Id.Value.ToString(), entry.GetProperty("id").GetString());

        var line = Assert.Single(entry.GetProperty("lines").EnumerateArray());
        Assert.Equal("InsufficientBenchmarkData", line.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, line.GetProperty("position").ValueKind);

        var benchmark = line.GetProperty("benchmark");
        Assert.False(benchmark.GetProperty("hasSufficientData").GetBoolean());
        Assert.Equal(JsonValueKind.Null, benchmark.GetProperty("distribution").ValueKind);
    }

    // ----- AC-2: a prior quote's benchmark is durable server state, read back on every call -----

    [Fact]
    public async Task A_prior_quotes_benchmark_reads_back_on_every_call()
    {
        var factory = _baseFactory.WithInMemoryQuotesDb();
        var tenantId = TenantId.New();
        var now = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

        // Matches FixtureBenchmarkAdapter's own AWS/EC2/US comparable (P25 0.085, P50 0.096,
        // P75 0.108 per instance-hour), so this line is confidently Assessed rather than
        // insufficient -- a real, non-degenerate position to prove reads back.
        var quote = NewQuote(
            tenantId, "prior-quote.pdf", supplier: "AWS", currency: "USD", geography: "US",
            purchaseDate: new DateOnly(2026, 7, 10), createdAt: now);
        await factory.SeedQuoteAsync(quote);
        await factory.SeedQuoteLineAsync(NewLine(
            tenantId, quote.Id, description: "EC2 Compute", quantity: 10m, term: "12 months",
            unitPrice: 0.05m, sku: "m5.large", createdAt: now));

        // First read -- as if the quote had already been sitting in the database before this
        // request (the "prior" half of AC-2).
        var firstRead = await GetHistoryAsync(factory, tenantId);
        var firstEntry = Assert.Single(firstRead.RootElement.GetProperty("items").EnumerateArray());
        var firstLine = Assert.Single(firstEntry.GetProperty("lines").EnumerateArray());
        Assert.Equal("Assessed", firstLine.GetProperty("status").GetString());
        // 0.05 <= P25 (0.085) -> BelowMarket (MarketAssessmentCalculator.Classify).
        Assert.Equal("BelowMarket", firstLine.GetProperty("position").GetString());

        // Second read, a fresh HTTP request through a brand-new HttpClient -- the "second browser"
        // proof ADR-028's own "Tests each task carries" convention names: durable server state,
        // never a client-held cache, agrees with itself.
        var secondRead = await GetHistoryAsync(factory, tenantId);
        var secondEntry = Assert.Single(secondRead.RootElement.GetProperty("items").EnumerateArray());
        var secondLine = Assert.Single(secondEntry.GetProperty("lines").EnumerateArray());
        Assert.Equal("Assessed", secondLine.GetProperty("status").GetString());
        Assert.Equal("BelowMarket", secondLine.GetProperty("position").GetString());
        Assert.Equal(
            firstLine.GetProperty("benchmark").GetProperty("distribution").GetProperty("p50").GetDecimal(),
            secondLine.GetProperty("benchmark").GetProperty("distribution").GetProperty("p50").GetDecimal());
    }

    [Fact]
    public async Task Quotes_are_ordered_newest_first()
    {
        var factory = _baseFactory.WithInMemoryQuotesDb();
        var tenantId = TenantId.New();

        var older = NewQuote(
            tenantId, "older.pdf", supplier: null, currency: null, geography: null,
            purchaseDate: null, createdAt: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = NewQuote(
            tenantId, "newer.pdf", supplier: null, currency: null, geography: null,
            purchaseDate: null, createdAt: new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero));
        await factory.SeedQuoteAsync(older);
        await factory.SeedQuoteAsync(newer);

        var body = await GetHistoryAsync(factory, tenantId);

        var ids = body.RootElement.GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString())
            .ToList();
        Assert.Equal([newer.Id.Value.ToString(), older.Id.Value.ToString()], ids);
    }

    private static async Task<JsonDocument> GetHistoryAsync(WebApplicationFactory<Program> factory, TenantId tenantId)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/quotes/benchmark-history");
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static Quote NewQuote(
        TenantId tenantId,
        string fileName,
        string? supplier,
        string? currency,
        string? geography,
        DateOnly? purchaseDate,
        DateTimeOffset createdAt) => new()
    {
        TenantId = tenantId,
        FileName = fileName,
        MimeType = "application/pdf",
        StoragePath = $"{tenantId.Value:D}/{fileName}",
        Checksum = "deadbeef",
        Supplier = supplier,
        Currency = currency,
        Geography = geography,
        PurchaseDate = purchaseDate,
        CreatedAt = createdAt,
    };

    private static QuoteLine NewLine(
        TenantId tenantId,
        EntityId quoteId,
        string description,
        decimal quantity,
        string term,
        decimal unitPrice,
        DateTimeOffset createdAt,
        string? sku = null) => new()
    {
        TenantId = tenantId,
        QuoteId = quoteId,
        Description = description,
        Quantity = quantity,
        Term = term,
        UnitPrice = unitPrice,
        Sku = sku,
        CreatedAt = createdAt,
    };
}
