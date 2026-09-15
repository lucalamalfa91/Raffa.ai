using Raffa.Quotes.Application;
using Raffa.Quotes.Application.Strategy;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Proves the Definition of Done for task E19/F02/US01/T01 (quote-read-api) — parent story
/// us-01-quote-read-api — for <see cref="QuoteQueryService"/> itself, against a real, migrated
/// Postgres+RLS database (ADR-009's own "no in-memory provider" posture). Mirrors
/// <c>NegotiationOutcomeServiceTests</c>/<c>QuoteRlsCrossTenantIsolationTests</c>'s own
/// shape/scaffolding.
///
/// Runs all assertions through a dedicated, deliberately unprivileged Postgres role
/// (<see cref="AppRoleName"/>: `NOSUPERUSER NOBYPASSRLS`, not the table owner) — the Testcontainers
/// bootstrap role is always a superuser, and superusers unconditionally bypass row security, so
/// asserting tenant-scoped behavior over that connection would pass vacuously.
/// </summary>
public sealed class QuoteQueryServiceTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_quote_query_app";
    private const string AppRolePassword = "raffa_quote_query_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
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
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private QuotesDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new QuotesDbContext(optionsBuilder.Options);
    }

    private async Task<EntityId> SeedQuoteAsync(
        TenantId tenantId,
        string fileName = "quote.pdf",
        string? supplier = "AWS",
        string? currency = "USD",
        string? geography = "US",
        DateOnly? purchaseDate = null,
        DateTimeOffset? createdAt = null)
    {
        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);

        var quoteId = EntityId.New();
        db.Quotes.Add(new Quote
        {
            Id = quoteId,
            TenantId = tenantId,
            FileName = fileName,
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value:D}/{fileName}",
            Checksum = "deadbeef",
            Supplier = supplier,
            Currency = currency,
            Geography = geography,
            PurchaseDate = purchaseDate,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();

        return quoteId;
    }

    private async Task<EntityId> SeedOutcomeAsync(
        TenantId tenantId,
        EntityId quoteId,
        DateTimeOffset capturedAt,
        decimal originalQuoteTotal = 520_000m,
        decimal? targetPrice = 420_000m,
        decimal finalPrice = 435_000m,
        decimal realizedSaving = 85_000m,
        decimal discountPercent = 16.3462m,
        int negotiationDurationDays = 24,
        EntityId? savingsOpportunityId = null)
    {
        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);

        var outcomeId = EntityId.New();
        db.NegotiationOutcomes.Add(new NegotiationOutcome
        {
            Id = outcomeId,
            TenantId = tenantId,
            QuoteId = quoteId,
            OriginalQuoteTotal = originalQuoteTotal,
            TargetPrice = targetPrice,
            FinalPrice = finalPrice,
            RealizedSaving = realizedSaving,
            DiscountPercent = discountPercent,
            NegotiationDurationDays = negotiationDurationDays,
            LeversUsed = [NegotiationLeverType.Term],
            CapturedAt = capturedAt,
            SavingsOpportunityId = savingsOpportunityId,
        });
        await db.SaveChangesAsync();

        return outcomeId;
    }

    [Fact]
    public async Task ListAsync_returns_only_this_tenants_quotes()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var quoteA = await SeedQuoteAsync(tenantA, fileName: "tenant-a.pdf");
        await SeedQuoteAsync(tenantB, fileName: "tenant-b.pdf");

        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new QuoteQueryService(db, tenantContext);

        var quotes = await service.ListAsync(tenantA);

        var quote = Assert.Single(quotes);
        Assert.Equal(quoteA, quote.Id);
        Assert.Equal("tenant-a.pdf", quote.FileName);
    }

    [Fact]
    public async Task ListAsync_orders_newest_first()
    {
        var tenantId = TenantId.New();
        var older = await SeedQuoteAsync(
            tenantId, fileName: "older.pdf", createdAt: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = await SeedQuoteAsync(
            tenantId, fileName: "newer.pdf", createdAt: new DateTimeOffset(2026, 9, 10, 0, 0, 0, TimeSpan.Zero));

        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new QuoteQueryService(db, tenantContext);

        var quotes = await service.ListAsync(tenantId);

        Assert.Equal([newer, older], quotes.Select(q => q.Id));
    }

    [Fact]
    public async Task GetAsync_returns_null_for_an_unknown_quote_id()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new QuoteQueryService(db, tenantContext);

        var result = await service.GetAsync(tenantId, EntityId.New());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_returns_null_for_a_quote_belonging_to_a_different_tenant()
    {
        var ownerTenant = TenantId.New();
        var otherTenant = TenantId.New();
        var quoteId = await SeedQuoteAsync(ownerTenant);

        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new QuoteQueryService(db, tenantContext);

        var result = await service.GetAsync(otherTenant, quoteId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_returns_an_empty_outcomes_list_when_none_recorded()
    {
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);

        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new QuoteQueryService(db, tenantContext);

        var result = await service.GetAsync(tenantId, quoteId);

        Assert.NotNull(result);
        Assert.Empty(result!.Outcomes);
    }

    [Fact]
    public async Task GetAsync_embeds_recorded_outcomes_newest_first()
    {
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(tenantId);
        var older = await SeedOutcomeAsync(
            tenantId, quoteId, capturedAt: new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero), finalPrice: 500_000m);
        var newer = await SeedOutcomeAsync(
            tenantId, quoteId, capturedAt: new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero), finalPrice: 480_000m);

        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new QuoteQueryService(db, tenantContext);

        var result = await service.GetAsync(tenantId, quoteId);

        Assert.NotNull(result);
        Assert.Equal([newer, older], result!.Outcomes.Select(o => o.Id));
        Assert.Equal(480_000m, result.Outcomes[0].FinalPrice);
        Assert.Equal(500_000m, result.Outcomes[1].FinalPrice);
    }

    [Fact]
    public async Task GetAsync_returns_stored_fields_verbatim_never_recomputing_them()
    {
        // ADR-028 §D2 / this task's own unit-cover requirement: the query service returns stored
        // fields only, never a recomputation. Proved directly: OriginalQuoteTotal (520k) minus
        // FinalPrice (435k) would recompute to 85k (NegotiationOutcomeCalculator's own worked
        // example), but the row below deliberately stores a different RealizedSaving/
        // DiscountPercent -- if GetAsync recomputed instead of reading the stored column, this
        // assertion would fail.
        var tenantId = TenantId.New();
        var quoteId = await SeedQuoteAsync(
            tenantId,
            fileName: "verbatim.pdf",
            supplier: "Salesforce",
            currency: "CHF",
            geography: "EU",
            purchaseDate: new DateOnly(2026, 8, 15));
        await SeedOutcomeAsync(
            tenantId, quoteId,
            capturedAt: new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero),
            originalQuoteTotal: 520_000m,
            finalPrice: 435_000m,
            realizedSaving: 1m,
            discountPercent: 2m);

        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new QuoteQueryService(db, tenantContext);

        var result = await service.GetAsync(tenantId, quoteId);

        Assert.NotNull(result);
        Assert.Equal("Salesforce", result!.Quote.Supplier);
        Assert.Equal("CHF", result.Quote.Currency);
        Assert.Equal("EU", result.Quote.Geography);
        Assert.Equal(new DateOnly(2026, 8, 15), result.Quote.PurchaseDate);

        var outcome = Assert.Single(result.Outcomes);
        // The stored (deliberately non-recomputed) values, verbatim -- never 85_000m/16.3m.
        Assert.Equal(1m, outcome.RealizedSaving);
        Assert.Equal(2m, outcome.DiscountPercent);
    }
}
