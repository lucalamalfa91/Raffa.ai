using Raffa.Quotes.Application.Normalization;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Proves the Definition of Done for task E05/F01/US01/T02 (quote-normalization) — parent story
/// us-01-quote-line-extraction, product spec §11.1 "Normalize unit economics" — against a real
/// Postgres+RLS database, the same "no in-memory provider that would silently ignore RLS" posture
/// <c>QuoteLineExtractionServiceTests</c> already established for this module.
/// </summary>
public sealed class QuoteLineNormalizationServiceTests : IAsyncLifetime
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

    // Same "bootstrap connection is a Postgres superuser (BYPASSRLS)" note as
    // QuoteLineExtractionServiceTests: this class proves NormalizeLines' own arithmetic/persistence
    // behaviour, not tenant isolation (QuoteRlsCrossTenantIsolationTests' own job) — a tenant scope
    // is still entered around every write below so a real (non-bypassing) role would also succeed
    // unchanged.
    private QuotesDbContext CreateDbContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString());
        return new QuotesDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task Normalizes_a_recognized_annual_term_line_to_the_same_annual_rate()
    {
        var tenantId = TenantId.New();
        var quoteId = EntityId.New();

        var tenantContext = new TenantContext();
        using var scope = tenantContext.BeginScope(tenantId);
        await using var db = CreateDbContext();

        db.QuoteLines.Add(NewLine(tenantId, quoteId, unitPrice: 100m, term: "Annual"));

        var service = new QuoteLineNormalizationService(db);
        var outcome = service.NormalizeLines(tenantId, quoteId);
        await db.SaveChangesAsync();

        Assert.Equal(1, outcome.NormalizedCount);
        Assert.Equal(0, outcome.UnresolvedCount);

        // Re-fetched from the real database (not the change tracker) so this also proves the two
        // new columns actually round-trip through Postgres, not just an in-memory mutation.
        var line = await db.QuoteLines.SingleAsync(l => l.QuoteId == quoteId);
        Assert.Equal(100m, line.NormalizedAnnualUnitPrice);
        Assert.Equal(12, line.NormalizedTermMonths);
    }

    [Fact]
    public async Task Annualizes_a_recognized_monthly_term_line_by_multiplying_by_twelve()
    {
        var tenantId = TenantId.New();
        var quoteId = EntityId.New();

        var tenantContext = new TenantContext();
        using var scope = tenantContext.BeginScope(tenantId);
        await using var db = CreateDbContext();

        db.QuoteLines.Add(NewLine(tenantId, quoteId, unitPrice: 10m, term: "Monthly"));

        var service = new QuoteLineNormalizationService(db);
        var outcome = service.NormalizeLines(tenantId, quoteId);
        await db.SaveChangesAsync();

        Assert.Equal(1, outcome.NormalizedCount);

        var line = await db.QuoteLines.SingleAsync(l => l.QuoteId == quoteId);
        Assert.Equal(120m, line.NormalizedAnnualUnitPrice);
        Assert.Equal(1, line.NormalizedTermMonths);
    }

    [Fact]
    public async Task An_unrecognized_term_leaves_normalization_honestly_unresolved()
    {
        var tenantId = TenantId.New();
        var quoteId = EntityId.New();

        var tenantContext = new TenantContext();
        using var scope = tenantContext.BeginScope(tenantId);
        await using var db = CreateDbContext();

        // "36 months" is a numeric commitment length, not a billing cadence this codebase can
        // actually resolve without guessing (see QuoteBillingCadence's own doc comment) — spec
        // §11.3's own "line-item normalization is unresolved" outcome.
        db.QuoteLines.Add(NewLine(tenantId, quoteId, unitPrice: 100m, term: "36 months"));

        var service = new QuoteLineNormalizationService(db);
        var outcome = service.NormalizeLines(tenantId, quoteId);
        await db.SaveChangesAsync();

        Assert.Equal(0, outcome.NormalizedCount);
        Assert.Equal(1, outcome.UnresolvedCount);

        var line = await db.QuoteLines.SingleAsync(l => l.QuoteId == quoteId);
        Assert.Null(line.NormalizedAnnualUnitPrice);
        Assert.Null(line.NormalizedTermMonths);
    }

    [Fact]
    public async Task Only_normalizes_lines_for_the_requested_quote_not_a_sibling_quote_in_the_same_tenant()
    {
        var tenantId = TenantId.New();
        var quoteId = EntityId.New();
        var otherQuoteId = EntityId.New();

        var tenantContext = new TenantContext();
        using var scope = tenantContext.BeginScope(tenantId);
        await using var db = CreateDbContext();

        // Both lines sit in the same change tracker (the same shape ApplyExtractedLines +
        // NormalizeLines share within one QuoteExtractionPipeline.ProcessAsync run) — NormalizeLines
        // must still only touch the requested quoteId's own rows.
        db.QuoteLines.Add(NewLine(tenantId, quoteId, unitPrice: 100m, term: "Annual"));
        db.QuoteLines.Add(NewLine(tenantId, otherQuoteId, unitPrice: 50m, term: "Annual"));

        var service = new QuoteLineNormalizationService(db);
        var outcome = service.NormalizeLines(tenantId, quoteId);
        await db.SaveChangesAsync();

        Assert.Equal(1, outcome.NormalizedCount);

        var untouchedLine = await db.QuoteLines.SingleAsync(l => l.QuoteId == otherQuoteId);
        Assert.Null(untouchedLine.NormalizedAnnualUnitPrice);
        Assert.Null(untouchedLine.NormalizedTermMonths);
    }

    private static QuoteLine NewLine(TenantId tenantId, EntityId quoteId, decimal? unitPrice, string? term) =>
        new()
        {
            TenantId = tenantId,
            QuoteId = quoteId,
            Description = "Line",
            UnitPrice = unitPrice,
            Term = term,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    // decimal is not a legal custom-attribute argument type in C#, so [InlineData] cannot carry
    // these values directly — MemberData supplying real `decimal?` values mirrors
    // QuoteLineExtractionServiceTests.PricingCases' own identical workaround.
    public static TheoryData<decimal?, string?, decimal?, int?> UnitEconomicsCases => new()
    {
        // unitPrice, term, expectedNormalizedAnnualUnitPrice, expectedTermMonths
        { 100m, "Annual", 100m, 12 },
        { 100m, "annual", 100m, 12 },
        { 100m, "ANNUALLY", 100m, 12 },
        { 100m, "  Annual  ", 100m, 12 },
        { 100m, "yearly", 100m, 12 },
        { 10m, "Monthly", 120m, 1 },
        { 10m, "per month", 120m, 1 },
        { 400m, "Quarterly", 1600m, 3 },
        { 600m, "semi-annual", 1200m, 6 },
        // Deliberately unresolved (QuoteBillingCadence's own small, fixed vocabulary excludes
        // these): a numeric commitment length, "one-time"/"perpetual", a blank term, and a missing
        // unit price all resolve to null/null rather than a guessed conversion (Appendix C rule 10).
        { 100m, "36 months", null, null },
        { 100m, "3 years", null, null },
        { 100m, "One-time", null, null },
        { 100m, "Perpetual", null, null },
        { 100m, "biannual", null, null },
        { 100m, null, null, null },
        { 100m, "", null, null },
        { 100m, "   ", null, null },
        { null, "Annual", null, null },
        { null, null, null, null },
    };

    [Theory]
    [MemberData(nameof(UnitEconomicsCases))]
    public void NormalizeUnitEconomics_recognizes_only_its_own_small_fixed_cadence_vocabulary(
        decimal? unitPrice, string? term, decimal? expectedNormalizedAnnualUnitPrice, int? expectedTermMonths)
    {
        var (normalizedAnnualUnitPrice, termMonths) =
            QuoteLineNormalizationService.NormalizeUnitEconomics(unitPrice, term);

        Assert.Equal(expectedNormalizedAnnualUnitPrice, normalizedAnnualUnitPrice);
        Assert.Equal(expectedTermMonths, termMonths);
    }
}
