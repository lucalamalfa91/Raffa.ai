using Raffa.Benchmark.Fixtures;
using Raffa.Quotes.Application.Assessment;
using Raffa.Quotes.Application.Strategy;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.Quotes.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Proves the Definition of Done for task E05/F03/US01/T01 (negotiation-strategy) — parent story
/// us-01-negotiation-strategy AC-1 ("Generate opening target, acceptable range, walk-away
/// threshold, levers, rationale") end to end against a real, migrated Postgres+RLS database
/// (ADR-009's own "no in-memory provider" posture) and the real
/// <see cref="FixtureBenchmarkAdapter"/> — mirrors
/// <c>Raffa.Quotes.Tests.MarketAssessmentServiceTests</c>'s own shape/scaffolding and reuses its
/// exact fixture-catalog scenario (Salesforce, Sales Cloud Enterprise, 12-month term, P25/P50/P75 =
/// 1500/1800/2100 per seat/year) so a target/saving test, a market-position test and this
/// negotiation-strategy test all agree on what the underlying numbers mean.
/// </summary>
public sealed class NegotiationStrategyServiceTests : IAsyncDisposable
{
    private const string AppRoleName = "raffa_negotiation_strategy_app";
    private const string AppRolePassword = "raffa_negotiation_strategy_app_test_password";

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
    public async Task GenerateAsync_computes_a_strategy_per_line_and_is_honest_about_an_unresolved_line()
    {
        await EnsureStartedAsync();

        var tenantId = TenantId.New();
        var quoteId = EntityId.New();
        var now = DateTimeOffset.UtcNow;
        var tenantContext = new TenantContext();

        EntityId aboveMarketLineId, clampedWalkAwayLineId, unresolvedLineId;

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

            // Same P25/P50/P75 = 1500/1800/2100 comparable MarketAssessmentServiceTests/
            // TargetSavingCalculatorTests already exercise: 2300/seat/year is above it.
            var aboveMarketLine = new QuoteLine
            {
                TenantId = tenantId,
                QuoteId = quoteId,
                Description = "Sales Cloud Enterprise",
                Quantity = 100m,
                Unit = "seats",
                Term = "12 months",
                NormalizedTermMonths = 12,
                UnitPrice = 2300m,
                CreatedAt = now,
            };

            // Second line on the same quote (exercises the Bundle lever's "> 1 sibling" branch) —
            // priced close enough to the range high end that the naive walk-away step
            // (1800 + 300 = 2100) would exceed this line's own current price (1900), so the
            // walk-away-never-exceeds-current-price clamp must bind for this line specifically.
            var clampedWalkAwayLine = new QuoteLine
            {
                TenantId = tenantId,
                QuoteId = quoteId,
                Description = "Sales Cloud Enterprise",
                Quantity = 50m,
                Unit = "seats",
                Term = "12 months",
                NormalizedTermMonths = 12,
                UnitPrice = 1900m,
                CreatedAt = now,
            };

            // Quote-data-unresolved: no UnitPrice was ever extracted, so MarketAssessmentService
            // never even attempts a benchmark call for this line — the same "unresolved" fixture
            // shape MarketAssessmentServiceTests already uses.
            var unresolvedLine = new QuoteLine
            {
                TenantId = tenantId,
                QuoteId = quoteId,
                Description = "Sales Cloud Enterprise",
                Quantity = 10m,
                Term = "12 months",
                UnitPrice = null,
                CreatedAt = now,
            };

            aboveMarketLineId = aboveMarketLine.Id;
            clampedWalkAwayLineId = clampedWalkAwayLine.Id;
            unresolvedLineId = unresolvedLine.Id;

            db.QuoteLines.AddRange(aboveMarketLine, clampedWalkAwayLine, unresolvedLine);
            await db.SaveChangesAsync();
        }

        Result<QuoteNegotiationStrategy> result;
        await using (var db = CreateAppContext(tenantContext))
        using (tenantContext.BeginScope(tenantId))
        {
            var marketAssessmentService = new MarketAssessmentService(db, new FixtureBenchmarkAdapter(), tenantContext);
            var clock = new FixedClock(new DateTimeOffset(2026, 2, 10, 0, 0, 0, TimeSpan.Zero));
            var service = new NegotiationStrategyService(marketAssessmentService, db, tenantContext, clock);
            result = await service.GenerateAsync(tenantId, quoteId);
        }

        Assert.True(result.IsSuccess);
        var strategy = result.Value;
        Assert.Equal(quoteId, strategy.QuoteId);
        Assert.Equal(3, strategy.Lines.Count);

        var aboveMarket = Assert.Single(strategy.Lines, l => l.QuoteLineId == aboveMarketLineId);
        Assert.Equal(1500m, aboveMarket.AcceptableRangeLow);
        Assert.Equal(1800m, aboveMarket.AcceptableRangeHigh);
        Assert.Equal(1200m, aboveMarket.OpeningTarget);   // 1500 - (1800-1500)
        Assert.Equal(2100m, aboveMarket.WalkAwayThreshold); // 1800 + 300, current price (2300) does not clamp
        Assert.Equal(7, aboveMarket.Levers.Count);
        var aboveMarketBundle = Assert.Single(aboveMarket.Levers, l => l.LeverType == NegotiationLeverType.Bundle);
        Assert.Contains("3", aboveMarketBundle.Rationale); // 3 lines total on this quote

        // Task E05/F03/US01/T02 (strategy-evidence, AC-2): the same end-to-end path also produces
        // structured evidence, not just prose — proven here against the real, migrated database
        // round trip (not just the pure-calculator unit tests in NegotiationStrategyCalculatorTests).
        Assert.Equal("Quote.LineCount", Assert.Single(aboveMarketBundle.Evidence).FieldName);
        Assert.Equal("3", Assert.Single(aboveMarketBundle.Evidence).Value);
        var aboveMarketVolume = Assert.Single(aboveMarket.Levers, l => l.LeverType == NegotiationLeverType.Volume);
        Assert.Contains(aboveMarketVolume.Evidence, e => e.FieldName == "QuoteLine.Quantity" && e.Value == "100");
        Assert.Contains(aboveMarketVolume.Evidence, e => e.FieldName == "QuoteLine.Unit" && e.Value == "seats");

        var clampedWalkAway = Assert.Single(strategy.Lines, l => l.QuoteLineId == clampedWalkAwayLineId);
        Assert.Equal(1500m, clampedWalkAway.AcceptableRangeLow);
        Assert.Equal(1800m, clampedWalkAway.AcceptableRangeHigh);
        // Naive walk-away (1800 + 300 = 2100) exceeds this line's own current price (1900) — clamped.
        Assert.Equal(1900m, clampedWalkAway.WalkAwayThreshold);

        var unresolved = Assert.Single(strategy.Lines, l => l.QuoteLineId == unresolvedLineId);
        Assert.Null(unresolved.OpeningTarget);
        Assert.Null(unresolved.AcceptableRangeLow);
        Assert.Null(unresolved.WalkAwayThreshold);
        Assert.Empty(unresolved.Levers);
        Assert.NotEmpty(unresolved.Explanation);
    }

    [Fact]
    public async Task GenerateAsync_propagates_the_assessment_failure_when_the_quote_does_not_exist()
    {
        await EnsureStartedAsync();

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        Result<QuoteNegotiationStrategy> result;
        await using (var db = CreateAppContext(tenantContext))
        using (tenantContext.BeginScope(tenantId))
        {
            var marketAssessmentService = new MarketAssessmentService(db, new FixtureBenchmarkAdapter(), tenantContext);
            var clock = new FixedClock(DateTimeOffset.UtcNow);
            var service = new NegotiationStrategyService(marketAssessmentService, db, tenantContext, clock);
            result = await service.GenerateAsync(tenantId, EntityId.New());
        }

        Assert.True(result.IsFailure);
        Assert.Contains("was not found", result.Error);
    }
}
