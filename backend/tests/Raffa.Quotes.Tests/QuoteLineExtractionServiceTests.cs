using Raffa.Quotes.Application.Extraction;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Proves the Definition of Done for task E05/F01/US01/T01 (quote-extraction) AC-2 ("Line items
/// extract quantity/SKU/edition/price/discount/term (evidence + confidence)") and AC-3 ("Separate
/// arithmetic from LLM language (App C #6)") against a real Postgres+RLS database — the same
/// "no in-memory provider that would silently ignore RLS" posture every other module's own
/// persistence test in this solution already takes.
/// </summary>
public sealed class QuoteLineExtractionServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var optionsBuilder = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString());

        await using var db = new QuotesDbContext(optionsBuilder.Options);
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    // The Testcontainers bootstrap connection is a Postgres superuser (BYPASSRLS), which is fine
    // here: this test class proves ApplyExtractedLines' own parsing/arithmetic/persistence
    // behaviour, not tenant isolation (that is QuoteUploadServiceTests'/
    // QuoteRlsCrossTenantIsolationTests' own job) — a tenant scope is still entered around every
    // write below so a real (non-bypassing) role would also succeed unchanged.
    private QuotesDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString());
        return new QuotesDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task Extracts_a_line_with_quantity_sku_edition_price_discount_term_and_evidence()
    {
        var tenantId = TenantId.New();
        var quoteId = EntityId.New();
        var now = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
        const string payload = """
            {"items":[
                {"sku":"SKU-ENT-500","edition":"Enterprise","description":"Enterprise Suite License",
                 "quantity":10,"unit":"seat","unitPrice":100,"listPrice":120,"discountPercent":16.67,
                 "term":"Annual","sourcePage":2,"sourceSpan":"10 seats @ $100/seat annual","confidence":0.92}
            ]}
            """;

        var tenantContext = new TenantContext();
        using var scope = tenantContext.BeginScope(tenantId);
        await using var db = CreateDbContext();
        var service = new QuoteLineExtractionService(db);

        var outcome = service.ApplyExtractedLines(tenantId, quoteId, payload, pageCount: 3, now);
        await db.SaveChangesAsync();

        Assert.Equal(1, outcome.ExtractedCount);
        Assert.Equal(0, outcome.SkippedCount);
        Assert.False(outcome.AnyLowConfidence);

        var line = await db.QuoteLines.SingleAsync(l => l.QuoteId == quoteId);
        Assert.Equal(tenantId, line.TenantId);
        Assert.Equal("SKU-ENT-500", line.Sku);
        Assert.Equal("Enterprise", line.Edition);
        Assert.Equal("Enterprise Suite License", line.Description);
        Assert.Equal(10m, line.Quantity);
        Assert.Equal("seat", line.Unit);
        Assert.Equal("Annual", line.Term);
        Assert.Equal(100m, line.UnitPrice);
        Assert.Equal(120m, line.ListPrice);
        Assert.Equal(16.67m, line.DiscountPercent);

        // AC-3: 1000 is Quantity * UnitPrice computed by QuoteLineExtractionService in code — the
        // fake payload above never states a total anywhere for it to have copied.
        Assert.Equal(1000m, line.ExtendedPrice);

        // AC-2 evidence + confidence tail.
        Assert.Equal("10 seats @ $100/seat annual", line.SourceSpan);
        Assert.Equal(2, line.SourcePage);
        Assert.Equal(0.92, line.Confidence);
        Assert.Equal(now, line.CreatedAt);
    }

    [Fact]
    public async Task Derives_unit_price_from_list_price_and_discount_when_not_reported_directly()
    {
        var tenantId = TenantId.New();
        var quoteId = EntityId.New();
        const string payload = """
            {"items":[
                {"description":"Standard Edition, 2 users","quantity":2,"listPrice":200,
                 "discountPercent":25,"confidence":0.8}
            ]}
            """;

        var tenantContext = new TenantContext();
        using var scope = tenantContext.BeginScope(tenantId);
        await using var db = CreateDbContext();
        var service = new QuoteLineExtractionService(db);

        service.ApplyExtractedLines(tenantId, quoteId, payload, pageCount: 1, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var line = await db.QuoteLines.SingleAsync(l => l.QuoteId == quoteId);

        // AC-3: the extraction payload above never names a unitPrice at all — 150 (200 * 0.75) and
        // 300 (150 * 2) are both derived deterministically in code, never asked of the model.
        Assert.Equal(150m, line.UnitPrice);
        Assert.Equal(300m, line.ExtendedPrice);
    }

    [Fact]
    public async Task Blank_description_line_is_skipped_not_persisted()
    {
        var tenantId = TenantId.New();
        var quoteId = EntityId.New();
        const string payload = """
            {"items":[
                {"sku":"SKU-1","description":"","quantity":1,"unitPrice":10,"confidence":0.9},
                {"sku":"SKU-2","description":"Real line","quantity":1,"unitPrice":10,"confidence":0.9}
            ]}
            """;

        var tenantContext = new TenantContext();
        using var scope = tenantContext.BeginScope(tenantId);
        await using var db = CreateDbContext();
        var service = new QuoteLineExtractionService(db);

        var outcome = service.ApplyExtractedLines(tenantId, quoteId, payload, pageCount: 1, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        Assert.Equal(1, outcome.ExtractedCount);
        Assert.Equal(1, outcome.SkippedCount);

        var persisted = await db.QuoteLines.Where(l => l.QuoteId == quoteId).ToListAsync();
        var onlyLine = Assert.Single(persisted);
        Assert.Equal("Real line", onlyLine.Description);
    }

    [Fact]
    public async Task A_line_below_the_confidence_threshold_flags_the_outcome_for_review()
    {
        var tenantId = TenantId.New();
        var quoteId = EntityId.New();
        const string payload = """
            {"items":[
                {"description":"Uncertain line","quantity":1,"unitPrice":10,"confidence":0.4}
            ]}
            """;

        var tenantContext = new TenantContext();
        using var scope = tenantContext.BeginScope(tenantId);
        await using var db = CreateDbContext();
        var service = new QuoteLineExtractionService(db);

        var outcome = service.ApplyExtractedLines(tenantId, quoteId, payload, pageCount: 1, DateTimeOffset.UtcNow);

        Assert.True(outcome.AnyLowConfidence);
    }

    [Fact]
    public async Task An_out_of_range_source_page_is_clamped_to_null()
    {
        var tenantId = TenantId.New();
        var quoteId = EntityId.New();
        const string payload = """
            {"items":[
                {"description":"Line with a bogus page","sourcePage":99,"confidence":0.9}
            ]}
            """;

        var tenantContext = new TenantContext();
        using var scope = tenantContext.BeginScope(tenantId);
        await using var db = CreateDbContext();
        var service = new QuoteLineExtractionService(db);

        service.ApplyExtractedLines(tenantId, quoteId, payload, pageCount: 2, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();

        var line = await db.QuoteLines.SingleAsync(l => l.QuoteId == quoteId);
        Assert.Null(line.SourcePage);
    }

    // decimal is not a legal custom-attribute argument type in C#, so [InlineData] cannot carry
    // these values directly (an int literal there fails at the reflection invocation boundary,
    // not at compile time) — MemberData supplying real `decimal?` values is this codebase's own
    // workaround, matching how xUnit theories elsewhere in this solution handle decimal cases.
    public static TheoryData<decimal?, decimal?, decimal?, decimal?, decimal?, decimal?> PricingCases => new()
    {
        // quantity, reportedUnitPrice, listPrice, discountPercent, expectedUnitPrice, expectedExtendedPrice
        { null, 100m, null, null, 100m, null },
        { 10m, 100m, null, null, 100m, 1000m },
        { 2m, null, 200m, 25m, 150m, 300m },
        { null, null, 200m, 25m, 150m, null },
        { 5m, null, null, null, null, null },
    };

    [Theory]
    [MemberData(nameof(PricingCases))]
    public void ComputePricing_is_deterministic_and_never_needs_the_model_to_report_a_total(
        decimal? quantity, decimal? reportedUnitPrice, decimal? listPrice, decimal? discountPercent,
        decimal? expectedUnitPrice, decimal? expectedExtendedPrice)
    {
        var (unitPrice, extendedPrice) = QuoteLineExtractionService.ComputePricing(
            quantity, reportedUnitPrice, listPrice, discountPercent);

        Assert.Equal(expectedUnitPrice, unitPrice);
        Assert.Equal(expectedExtendedPrice, extendedPrice);
    }
}
