using Raffa.Savings.Application;
using Raffa.Savings.Infrastructure;
using Raffa.Savings.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Savings.Tests;

/// <summary>
/// Proves task E04/F03/US01/T01's (savings-kpis) database-facing half:
/// <see cref="SavingsKpiQueryService.GetSummaryAsync"/> reads back real, tenant-scoped
/// <see cref="Domain.SavingsOpportunity"/> rows — seeded through the real, validated
/// <see cref="SavingsOpportunityService"/> write path, not raw DbContext manipulation — and buckets
/// them via <see cref="SavingsKpiCalculator"/>, against a real Postgres+RLS Testcontainer. Mirrors
/// <see cref="SavingsOpportunityServiceTests"/>'s own shape; <see cref="SavingsKpiCalculator"/>'s own
/// grouping/summing arithmetic is proven independently, with no database, by
/// <see cref="SavingsKpiCalculatorTests"/>.
/// </summary>
public sealed class SavingsKpiQueryServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private SavingsDbContext CreateContext(ITenantContext? tenantContext = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SavingsDbContext>();
        SavingsDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString(), tenantContext);
        return new SavingsDbContext(optionsBuilder.Options);
    }

    private async Task MigrateAsync()
    {
        await using var migrateDb = CreateContext();
        await migrateDb.Database.MigrateAsync();
    }

    private static CreateSavingsOpportunityRequest ValidRequest(
        string currency = "USD", decimal low = 1_000m, decimal high = 2_000m, double confidence = 0.5) =>
        new(
            SupplierId: null,
            ContractId: null,
            Type: "price-renegotiation",
            CurrentSpend: 50_000m,
            Currency: currency,
            EstimatedSavingsLow: low,
            EstimatedSavingsHigh: high,
            Confidence: confidence);

    [Fact]
    public async Task GetSummaryAsync_buckets_by_status_and_currency_for_this_tenant_only()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var otherTenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var auditWriter = new RecordingAuditWriter();

        await using (var db = CreateContext(tenantContext))
        {
            var service = new SavingsOpportunityService(db, tenantContext, clock, auditWriter);

            // Two Identified/USD opportunities for this tenant — must be summed together, not
            // reported as two separate rows.
            await service.CreateAsync(tenantId, ValidRequest("USD", low: 1_000m, high: 2_000m, confidence: 0.4));
            await service.CreateAsync(tenantId, ValidRequest("USD", low: 3_000m, high: 4_000m, confidence: 0.6));

            // One Identified/CHF opportunity for this tenant — must not conflate with the USD sum
            // above (no currency-conversion service anywhere in this codebase).
            await service.CreateAsync(tenantId, ValidRequest("CHF", low: 900m, high: 1_000m));

            // One InProgress/USD opportunity for this tenant — must land in a different bucket
            // than the Identified/USD rows above, even though it shares their currency.
            var inProgress = await service.CreateAsync(tenantId, ValidRequest("USD", low: 500m, high: 800m));
            // realizedAmount: null -- this call only advances status, same shape
            // SavingsOpportunityServiceTests' own status-only UpdateAsync calls use. Pre-existing gap
            // fixed in passing by task E04/F03/US01/T02 (savings-list): SavingsOpportunityService
            // .UpdateAsync gained its required realizedAmount parameter from the concurrently
            // developed task E04/F02/US02/T02 (realized-savings, same wave-spec phase as this file's
            // own task E04/F03/US01/T01), so the two branches' non-conflicting textual diffs still
            // left this call site failing to compile once merged -- restoring the build all of this
            // module's tests (including this task's own) depend on, not a behavior change.
            await service.UpdateAsync(tenantId, inProgress.Value.Id, owner: null, status: "InProgress", realizedAmount: null);

            // Another tenant's own opportunity — must never leak into the first tenant's summary
            // (ADR-009 RLS + the application-level tenant filter, same guarantee
            // SavingsOpportunityRlsCrossTenantIsolationTests already pins for ListAsync).
            await service.CreateAsync(otherTenantId, ValidRequest("USD", low: 999_999m, high: 999_999m));
        }

        await using var readDb = CreateContext(tenantContext);
        var queryService = new SavingsKpiQueryService(readDb, tenantContext, new SavingsKpiCalculator());

        var summary = await queryService.GetSummaryAsync(tenantId);

        Assert.Equal(2, summary.Identified.Count);
        var usdIdentified = Assert.Single(summary.Identified, b => b.Currency == "USD");
        Assert.Equal(4_000m, usdIdentified.Low);
        Assert.Equal(6_000m, usdIdentified.High);
        Assert.Equal(2, usdIdentified.Count);
        Assert.Equal(0.5, usdIdentified.AverageConfidence, precision: 10);

        var chfIdentified = Assert.Single(summary.Identified, b => b.Currency == "CHF");
        Assert.Equal(900m, chfIdentified.Low);
        Assert.Equal(1_000m, chfIdentified.High);

        var usdInProgress = Assert.Single(summary.InProgress);
        Assert.Equal("USD", usdInProgress.Currency);
        Assert.Equal(500m, usdInProgress.Low);
        Assert.Equal(800m, usdInProgress.High);

        Assert.Empty(summary.Realized);
    }

    [Fact]
    public async Task GetSummaryAsync_returns_honestly_empty_buckets_for_a_tenant_with_no_opportunities()
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var queryService = new SavingsKpiQueryService(db, tenantContext, new SavingsKpiCalculator());

        var summary = await queryService.GetSummaryAsync(TenantId.New());

        Assert.Empty(summary.Identified);
        Assert.Empty(summary.InProgress);
        Assert.Empty(summary.Realized);
    }
}
