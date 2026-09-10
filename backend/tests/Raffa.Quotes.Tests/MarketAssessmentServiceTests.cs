using Raffa.Benchmark.Fixtures;
using Raffa.Quotes.Application.Assessment;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Proves the Definition of Done for task E05/F02/US01/T01 (market-assessment) — and, since task
/// E05/F02/US01/T02 (target-saving) extended <see cref="MarketAssessmentService"/>'s own per-line
/// result with <c>LineMarketAssessment.TargetSaving</c>, the parent story us-01-market-assessment
/// Definition of Done in full ("`dotnet test` proves assessment + target/saving from fixture
/// benchmark") — end to end against a real, migrated Postgres+RLS database (ADR-009's own "no
/// in-memory provider" posture every other module persistence test in this solution already takes)
/// and the real <see cref="FixtureBenchmarkAdapter"/> — never a stub/mock of the Benchmark Service —
/// mirroring <c>Raffa.Quotes.Tests.SkuNormalizationServiceTests</c>'s own three-layer split (pure
/// core in <c>MarketAssessmentCalculatorTests</c>/<c>MarketAssessmentQueryBuilderTests</c>/
/// <c>TargetSavingCalculatorTests</c>, persistence here).
///
/// All three lines below share one quote from one supplier ("Salesforce"), the same catalog row
/// <c>Raffa.IntegrationTests.R3EndToEndTests</c> already exercises for the analogous Savings
/// comparison (P25/P50/P75 = 1500/1800/2100 per seat/year, sample size 512 for the 12-month
/// comparable) — a "matched quote" stand-in built by hand against a real fixture catalog row, the
/// same convention that type's own doc comment establishes, not a fabricated one.
/// </summary>
public sealed class MarketAssessmentServiceTests : IAsyncDisposable
{
    private const string AppRoleName = "raffa_market_assessment_app";
    private const string AppRolePassword = "raffa_market_assessment_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private string _appConnectionString = string.Empty;
    private bool _started;

    private async Task EnsureStartedAsync()
    {
        if (_started)
        {
            return;
        }

        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using (var adminDb = new QuotesDbContext(adminOptions.Options))
        {
            await adminDb.Database.MigrateAsync();

            await adminDb.Database.ExecuteSqlRawAsync(
                $"""
                CREATE ROLE {AppRoleName} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS;
                GRANT USAGE ON SCHEMA public TO {AppRoleName};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRoleName};
                """);
        }

        _appConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = AppRoleName,
            Password = AppRolePassword,
        }.ConnectionString;

        _started = true;
    }

    public ValueTask DisposeAsync() => _postgres.DisposeAsync();

    private QuotesDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new QuotesDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task AssessAsync_reports_above_market_unresolved_and_insufficient_data_lines_from_one_real_quote()
    {
        await EnsureStartedAsync();

        var tenantId = TenantId.New();
        var quoteId = EntityId.New();
        var now = DateTimeOffset.UtcNow;
        var tenantContext = new TenantContext();

        EntityId aboveMarketLineId, unresolvedLineId, insufficientDataLineId;

        await using (var db = CreateAppContext(tenantContext))
        using (tenantContext.BeginScope(tenantId))
        {
            db.Quotes.Add(new Quote
            {
                Id = quoteId,
                TenantId = tenantId,
                FileName = "salesforce-quote.pdf",
                MimeType = "application/pdf",
                StoragePath = $"{tenantId.Value:D}/quote.pdf",
                Checksum = "checksum",
                Supplier = "Salesforce",
                Currency = "USD",
                Geography = "US",
                PurchaseDate = DateOnly.FromDateTime(now.UtcDateTime),
                CreatedAt = now,
            });

            // Above market: 2300/seat/year is above the catalog's own P75 (2100) for the 12-month
            // comparable (P25/P50/P75 = 1500/1800/2100, sample size 512).
            var aboveMarketLine = new QuoteLine
            {
                TenantId = tenantId,
                QuoteId = quoteId,
                Description = "Sales Cloud Enterprise",
                Quantity = 100m,
                Term = "12 months",
                UnitPrice = 2300m,
                CreatedAt = now,
            };

            // Quote-data-unresolved: same product/quantity/term, but no UnitPrice was ever
            // extracted for this line — MarketAssessmentQueryBuilder cannot build a query without a
            // current price to compare, so no benchmark call is even attempted for this one.
            var unresolvedLine = new QuoteLine
            {
                TenantId = tenantId,
                QuoteId = quoteId,
                Description = "Sales Cloud Enterprise",
                Quantity = 100m,
                Term = "12 months",
                UnitPrice = null,
                CreatedAt = now,
            };

            // Insufficient-benchmark-data: same supplier+product, but a 24-month term the fixture
            // catalog has no comparable for (only 12/36-month rows exist) — FixtureBenchmarkAdapter
            // falls back to its own honest "weak match" (same supplier+product only), reporting
            // real provenance (source/sample size) but no distribution (ADR-001).
            var insufficientDataLine = new QuoteLine
            {
                TenantId = tenantId,
                QuoteId = quoteId,
                Description = "Sales Cloud Enterprise",
                Quantity = 100m,
                Term = "24 months",
                UnitPrice = 1800m,
                CreatedAt = now,
            };

            aboveMarketLineId = aboveMarketLine.Id;
            unresolvedLineId = unresolvedLine.Id;
            insufficientDataLineId = insufficientDataLine.Id;

            db.QuoteLines.AddRange(aboveMarketLine, unresolvedLine, insufficientDataLine);
            await db.SaveChangesAsync();
        }

        Result<QuoteMarketAssessment> result;
        await using (var db = CreateAppContext(tenantContext))
        using (tenantContext.BeginScope(tenantId))
        {
            var service = new MarketAssessmentService(db, new FixtureBenchmarkAdapter(), tenantContext);
            result = await service.AssessAsync(tenantId, quoteId);
        }

        Assert.True(result.IsSuccess);
        var assessment = result.Value;
        Assert.Equal(quoteId, assessment.QuoteId);
        Assert.Equal(3, assessment.Lines.Count);

        var aboveMarket = Assert.Single(assessment.Lines, l => l.QuoteLineId == aboveMarketLineId);
        Assert.Equal(MarketAssessmentStatus.Assessed, aboveMarket.Status);
        Assert.Equal(MarketPosition.AboveMarket, aboveMarket.Position);
        Assert.Equal(2300m, aboveMarket.UnitPrice);
        Assert.Equal(100m, aboveMarket.Quantity);
        Assert.NotNull(aboveMarket.Benchmark);
        Assert.True(aboveMarket.Benchmark!.HasSufficientData);
        Assert.Equal(1500m, aboveMarket.Benchmark.Distribution!.P25);
        Assert.Equal(1800m, aboveMarket.Benchmark.Distribution.P50);
        Assert.Equal(2100m, aboveMarket.Benchmark.Distribution.P75);
        Assert.NotNull(aboveMarket.Provenance);
        Assert.Equal(MarketConfidenceLevel.High, aboveMarket.Provenance!.ConfidenceLevel);
        Assert.Equal("fixture", aboveMarket.Provenance.Source);
        Assert.Equal(512, aboveMarket.Provenance.SampleSize);
        // Task E05/F02/US01/T02 (target-saving), AC-2's "recommended target range + potential
        // saving" half — same deterministic arithmetic TargetSavingCalculatorTests proves in
        // isolation, now proven end to end against a real Postgres+RLS database.
        Assert.NotNull(aboveMarket.TargetSaving);
        Assert.Equal(1500m, aboveMarket.TargetSaving!.RecommendedTargetLow);
        Assert.Equal(1800m, aboveMarket.TargetSaving.RecommendedTargetHigh);
        Assert.Equal(500m, aboveMarket.TargetSaving.SavingsRangeLow);   // 2300 - 1800
        Assert.Equal(800m, aboveMarket.TargetSaving.SavingsRangeHigh); // 2300 - 1500
        Assert.Equal(50_000m, aboveMarket.TargetSaving.TotalSavingsRangeLow);  // 500 * 100
        Assert.Equal(80_000m, aboveMarket.TargetSaving.TotalSavingsRangeHigh); // 800 * 100

        var unresolved = Assert.Single(assessment.Lines, l => l.QuoteLineId == unresolvedLineId);
        Assert.Equal(MarketAssessmentStatus.QuoteDataUnresolved, unresolved.Status);
        Assert.Null(unresolved.Position);
        Assert.Null(unresolved.UnitPrice);
        Assert.Null(unresolved.Benchmark);
        Assert.Null(unresolved.Provenance);
        // No benchmark call was ever made for this line (no UnitPrice to compare) — TargetSaving is
        // null the same way Provenance is, even though Quantity itself was recorded.
        Assert.Equal(100m, unresolved.Quantity);
        Assert.Null(unresolved.TargetSaving);

        var insufficient = Assert.Single(assessment.Lines, l => l.QuoteLineId == insufficientDataLineId);
        Assert.Equal(MarketAssessmentStatus.InsufficientBenchmarkData, insufficient.Status);
        Assert.Null(insufficient.Position);
        Assert.Equal(1800m, insufficient.UnitPrice);
        Assert.NotNull(insufficient.Benchmark);
        Assert.False(insufficient.Benchmark!.HasSufficientData);
        // Still real provenance — spec §11.3's benchmark-trust rule: never withhold provenance just
        // because the comparison itself abstained.
        Assert.NotNull(insufficient.Provenance);
        Assert.Equal("fixture", insufficient.Provenance!.Source);
        // Same benchmark-trust rule applies to TargetSaving: still a non-null object (with a named
        // reason), never a silently-missing value, even though every numeric field is null.
        Assert.NotNull(insufficient.TargetSaving);
        Assert.Null(insufficient.TargetSaving!.RecommendedTargetLow);
        Assert.Null(insufficient.TargetSaving.TotalSavingsRangeLow);
        Assert.Contains("Appendix C rule 10", insufficient.TargetSaving.Explanation);
    }

    [Fact]
    public async Task AssessAsync_reports_every_line_as_unresolved_when_the_quote_itself_has_no_supplier()
    {
        await EnsureStartedAsync();

        var tenantId = TenantId.New();
        var quoteId = EntityId.New();
        var now = DateTimeOffset.UtcNow;
        var tenantContext = new TenantContext();

        await using (var db = CreateAppContext(tenantContext))
        using (tenantContext.BeginScope(tenantId))
        {
            // Uploaded without the optional supplier/currency/geography form fields — an honest,
            // expected state (Quote's own doc comment), not a validation error at upload time.
            db.Quotes.Add(new Quote
            {
                Id = quoteId,
                TenantId = tenantId,
                FileName = "quote-without-supplier.pdf",
                MimeType = "application/pdf",
                StoragePath = $"{tenantId.Value:D}/quote.pdf",
                Checksum = "checksum",
                PurchaseDate = DateOnly.FromDateTime(now.UtcDateTime),
                CreatedAt = now,
            });

            db.QuoteLines.Add(new QuoteLine
            {
                TenantId = tenantId,
                QuoteId = quoteId,
                Description = "Some product",
                Quantity = 10m,
                Term = "12 months",
                UnitPrice = 100m,
                CreatedAt = now,
            });

            await db.SaveChangesAsync();
        }

        Result<QuoteMarketAssessment> result;
        await using (var db = CreateAppContext(tenantContext))
        using (tenantContext.BeginScope(tenantId))
        {
            var service = new MarketAssessmentService(db, new FixtureBenchmarkAdapter(), tenantContext);
            result = await service.AssessAsync(tenantId, quoteId);
        }

        Assert.True(result.IsSuccess);
        var line = Assert.Single(result.Value.Lines);
        Assert.Equal(MarketAssessmentStatus.QuoteDataUnresolved, line.Status);
        Assert.Contains("Supplier", line.Explanation);
        // The line's own price is still echoed — it does not depend on the benchmark.
        Assert.Equal(100m, line.UnitPrice);
    }

    [Fact]
    public async Task AssessAsync_fails_honestly_for_a_quote_id_that_does_not_exist_for_this_tenant()
    {
        await EnsureStartedAsync();

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var db = CreateAppContext(tenantContext);
        using var scope = tenantContext.BeginScope(tenantId);

        var service = new MarketAssessmentService(db, new FixtureBenchmarkAdapter(), tenantContext);
        var result = await service.AssessAsync(tenantId, EntityId.New());

        Assert.True(result.IsFailure);
        Assert.Contains("was not found", result.Error);
    }
}
