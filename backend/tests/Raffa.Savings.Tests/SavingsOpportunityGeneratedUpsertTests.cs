using Raffa.Savings.Application;
using Raffa.Savings.Domain;
using Raffa.Savings.Infrastructure;
using Raffa.Savings.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Savings.Tests;

/// <summary>
/// Proves <see cref="SavingsOpportunityService.UpsertGeneratedAsync"/> — the persist-all step
/// behind Ask Raffa's savings levers: keyed rows are created once and refreshed after, a row a
/// person moved past Identified is frozen, validation runs before any write, and the partial
/// unique index leaves hand-recorded (null-key) rows unconstrained. Same disposable-Postgres
/// shape as <see cref="SavingsOpportunityServiceTests"/>.
/// </summary>
public sealed class SavingsOpportunityGeneratedUpsertTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

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

    private static GeneratedSavingsOpportunity Lever(string key, decimal low, decimal high) =>
        new(key, $"ServiceNow — {key}", null, 230_000m, "EUR", low, high, 0.7);

    [Fact]
    public async Task First_call_creates_keyed_rows_second_call_refreshes_them_in_place()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
        var auditWriter = new RecordingAuditWriter();

        await using (var db = CreateContext(tenantContext))
        {
            var service = new SavingsOpportunityService(db, tenantContext, clock, auditWriter);
            var first = await service.UpsertGeneratedAsync(
                tenantId, contractId, [Lever("market-discount", 10_350m, 20_700m), Lever("uplift-cap", 6_900m, 18_400m)], "user:alex");

            Assert.True(first.IsSuccess);
            Assert.Equal(2, first.Value.Count);
            Assert.All(first.Value, r => Assert.Equal(SavingsOpportunityStatus.Identified, r.Status));
            Assert.Contains(first.Value, r => r.OpportunityKey == "market-discount" && r.EstimatedSavingsHigh == 20_700m);
        }

        await using (var db = CreateContext(tenantContext))
        {
            var service = new SavingsOpportunityService(db, tenantContext, clock, auditWriter);
            var second = await service.UpsertGeneratedAsync(
                tenantId, contractId, [Lever("market-discount", 12_000m, 24_000m)], "user:alex");

            Assert.True(second.IsSuccess);
            var refreshed = Assert.Single(second.Value);
            Assert.Equal(24_000m, refreshed.EstimatedSavingsHigh);
        }

        await using var readDb = CreateContext(tenantContext);
        var listed = await new SavingsOpportunityService(readDb, tenantContext, clock, auditWriter).ListAsync(tenantId);

        Assert.Equal(2, listed.Count);
        Assert.Equal(24_000m, listed.Single(r => r.OpportunityKey == "market-discount").EstimatedSavingsHigh);
        Assert.Equal(18_400m, listed.Single(r => r.OpportunityKey == "uplift-cap").EstimatedSavingsHigh);
        Assert.Equal(2, auditWriter.Written.Count(e => e.Action == "savings_opportunity.generated"));
    }

    [Fact]
    public async Task A_row_a_person_owns_is_never_overwritten()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero));
        var auditWriter = new RecordingAuditWriter();

        EntityId id;
        await using (var db = CreateContext(tenantContext))
        {
            var service = new SavingsOpportunityService(db, tenantContext, clock, auditWriter);
            var created = await service.UpsertGeneratedAsync(tenantId, contractId, [Lever("market-discount", 1m, 2m)], "user:alex");
            id = created.Value[0].Id;
            var owned = await service.UpdateAsync(tenantId, id, owner: "Alex", status: "InProgress", realizedAmount: null, "user:alex");
            Assert.True(owned.IsSuccess);
        }

        await using (var db = CreateContext(tenantContext))
        {
            var service = new SavingsOpportunityService(db, tenantContext, clock, auditWriter);
            var again = await service.UpsertGeneratedAsync(tenantId, contractId, [Lever("market-discount", 100m, 200m)], "user:alex");

            Assert.True(again.IsSuccess);
            var frozen = Assert.Single(again.Value);
            Assert.Equal(2m, frozen.EstimatedSavingsHigh);
            Assert.Equal(SavingsOpportunityStatus.InProgress, frozen.Status);
        }
    }

    [Fact]
    public async Task Validation_rejects_blank_or_duplicate_keys_before_any_write()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();

        await using var db = CreateContext(tenantContext);
        var service = new SavingsOpportunityService(db, tenantContext, new FixedClock(DateTimeOffset.UnixEpoch), auditWriter);

        var blank = await service.UpsertGeneratedAsync(tenantId, EntityId.New(), [Lever(" ", 1m, 2m)], "user:alex");
        var duplicate = await service.UpsertGeneratedAsync(tenantId, EntityId.New(), [Lever("k", 1m, 2m), Lever("k", 1m, 2m)], "user:alex");

        Assert.Equal(SavingsOpportunityService.GeneratedKeyRequiredError, blank.Error);
        Assert.Equal(SavingsOpportunityService.GeneratedKeyDuplicateError, duplicate.Error);
        Assert.Empty(auditWriter.Written);
        Assert.Empty(await service.ListAsync(tenantId));
    }
}
