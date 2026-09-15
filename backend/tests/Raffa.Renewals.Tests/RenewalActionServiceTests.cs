using Raffa.Renewals.Application;
using Raffa.Renewals.Domain;
using Raffa.Renewals.Infrastructure;
using Raffa.Renewals.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Renewals.Tests;

/// <summary>
/// Proves the Definition of Done for task E03/F03/US01/T02 (renewal-action; parent story
/// us-01-renewal-dashboard-api AC-3): <see cref="RenewalActionService"/> genuinely persists a
/// caller's owner/status/action as a readable, upserted row through the exact same Npgsql/EF
/// Core/RLS-interceptor pipeline every other module's write path uses (ADR-003/ADR-009), validates
/// its three fields before writing anything, and writes one <see cref="IAuditWriter"/> entry per
/// successful call (spec §14.1). Mirrors <c>Raffa.Audit.Tests.AuditWriterTests</c>'s own
/// Testcontainers shape; tenant-isolation-under-RLS is proved separately by
/// <c>RenewalActionRlsCrossTenantIsolationTests</c> (same split
/// <c>Raffa.Documents.Contracts.Tests</c>/<c>Raffa.Tenancy</c> already use).
///
/// Spins up its own disposable Postgres container per test run (Testcontainers), so this test
/// needs nothing but a running Docker daemon; no shared/external database to stand up by hand.
/// </summary>
public sealed class RenewalActionServiceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private RenewalsDbContext CreateContext(ITenantContext? tenantContext = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<RenewalsDbContext>();
        RenewalsDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString(), tenantContext);
        return new RenewalsDbContext(optionsBuilder.Options);
    }

    private async Task MigrateAsync()
    {
        await using var migrateDb = CreateContext();
        await migrateDb.Database.MigrateAsync();
    }

    [Fact]
    public async Task SetActionAsync_persists_a_new_row_readable_through_GetActionAsync()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero));
        var auditWriter = new RecordingAuditWriter();

        await using (var db = CreateContext(tenantContext))
        {
            var service = new RenewalActionService(db, tenantContext, clock, auditWriter);

            var result = await service.SetActionAsync(
                tenantId, contractId, "alice@acme.example", "InProgress", "Started negotiation",
                "test-actor@example.com");

            Assert.True(result.IsSuccess);
            Assert.Equal(contractId, result.Value.ContractId);
            Assert.Equal("alice@acme.example", result.Value.Owner);
            Assert.Equal(RenewalActionStatus.InProgress, result.Value.Status);
            Assert.Equal("Started negotiation", result.Value.Action);
            Assert.Equal(clock.UtcNow, result.Value.UpdatedAt);
        }

        // Fresh context/connection, same tenant scope: this reads back from Postgres, not the
        // change tracker, and only succeeds if RLS actually lets this tenant see its own row.
        await using var readDb = CreateContext(tenantContext);
        var readService = new RenewalActionService(readDb, tenantContext, clock, auditWriter);
        var stored = await readService.GetActionAsync(tenantId, contractId);

        Assert.NotNull(stored);
        Assert.Equal("alice@acme.example", stored!.Owner);
        Assert.Equal(RenewalActionStatus.InProgress, stored.Status);
        Assert.Equal("Started negotiation", stored.Action);

        // Spec §14.1 / Appendix C rule 9: every successful write is audited.
        var entry = Assert.Single(auditWriter.Written);
        Assert.Equal(tenantId, entry.TenantId);
        Assert.Equal("renewal.action_updated", entry.Action);
        Assert.Equal("renewal", entry.ResourceType);
        Assert.Equal(contractId.Value.ToString(), entry.ResourceId);
        Assert.Equal("test-actor@example.com", entry.Actor);
    }

    [Fact]
    public async Task SetActionAsync_updates_the_same_row_instead_of_creating_a_second_one()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();

        await using (var db = CreateContext(tenantContext))
        {
            var service = new RenewalActionService(
                db, tenantContext, new FixedClock(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero)), auditWriter);
            await service.SetActionAsync(
                tenantId, contractId, "alice@acme.example", "NotStarted", "Reviewing terms",
                "test-actor@example.com");
        }

        var secondClock = new FixedClock(new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero));
        await using (var db = CreateContext(tenantContext))
        {
            var service = new RenewalActionService(db, tenantContext, secondClock, auditWriter);
            var updated = await service.SetActionAsync(
                tenantId, contractId, "bob@acme.example", "Completed", "Renewed at same terms",
                "test-actor@example.com");

            Assert.True(updated.IsSuccess);
            Assert.Equal("bob@acme.example", updated.Value.Owner);
            Assert.Equal(RenewalActionStatus.Completed, updated.Value.Status);
        }

        await using var readDb = CreateContext(tenantContext);
        var rows = await readDb.RenewalActions.Where(a => a.TenantId == tenantId).ToListAsync();

        var row = Assert.Single(rows); // upsert, not a second row for the same (tenant, contract).
        Assert.Equal(contractId, row.ContractId);
        Assert.Equal("bob@acme.example", row.Owner);
        Assert.Equal(RenewalActionStatus.Completed, row.Status);
        Assert.Equal("Renewed at same terms", row.Action);
        Assert.Equal(secondClock.UtcNow, row.UpdatedAt);
        Assert.Equal(2, auditWriter.Written.Count); // one audit entry per call, not per row.
    }

    [Fact]
    public async Task GetActionAsync_returns_null_when_nothing_was_ever_recorded()
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = new RenewalActionService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.GetActionAsync(TenantId.New(), EntityId.New());

        Assert.Null(result);
    }

    [Theory]
    [InlineData(null, "InProgress", "Some action")]
    [InlineData("", "InProgress", "Some action")]
    [InlineData("   ", "InProgress", "Some action")]
    public async Task SetActionAsync_rejects_an_empty_owner_without_writing_anything(
        string? owner, string status, string action)
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        await using var db = CreateContext(tenantContext);
        var service = new RenewalActionService(db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), auditWriter);

        var result = await service.SetActionAsync(
            TenantId.New(), EntityId.New(), owner, status, action, "test-actor@example.com");

        Assert.True(result.IsFailure);
        Assert.Equal(RenewalActionService.OwnerRequiredError, result.Error);
        Assert.Empty(auditWriter.Written);
        Assert.Equal(0, await db.RenewalActions.CountAsync());
    }

    [Fact]
    public async Task SetActionAsync_rejects_an_empty_action_without_writing_anything()
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        await using var db = CreateContext(tenantContext);
        var service = new RenewalActionService(db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), auditWriter);

        var result = await service.SetActionAsync(
            TenantId.New(), EntityId.New(), "alice@acme.example", "InProgress", " ", "test-actor@example.com");

        Assert.True(result.IsFailure);
        Assert.Equal(RenewalActionService.ActionRequiredError, result.Error);
        Assert.Empty(auditWriter.Written);
        Assert.Equal(0, await db.RenewalActions.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Renegotiating")] // plausible-looking but not one of the three real enum members.
    public async Task SetActionAsync_rejects_an_unrecognized_status_without_writing_anything(string? status)
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        await using var db = CreateContext(tenantContext);
        var service = new RenewalActionService(db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), auditWriter);

        var result = await service.SetActionAsync(
            TenantId.New(), EntityId.New(), "alice@acme.example", status, "Some action", "test-actor@example.com");

        Assert.True(result.IsFailure);
        Assert.Equal(RenewalActionService.StatusRequiredError, result.Error);
        Assert.Empty(auditWriter.Written);
        Assert.Equal(0, await db.RenewalActions.CountAsync());
    }

    [Fact]
    public async Task SetActionAsync_accepts_status_case_insensitively()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = new RenewalActionService(db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var result = await service.SetActionAsync(
            tenantId, EntityId.New(), "alice@acme.example", "completed", "Renewed", "test-actor@example.com");

        Assert.True(result.IsSuccess);
        Assert.Equal(RenewalActionStatus.Completed, result.Value.Status);
    }

    /// <summary>
    /// Proves the Definition of Done for task E19/F01/US01/T01 (renewal-action-api; ADR-028 §D1)'s
    /// batch read: <c>GET /api/renewals</c> resolves the whole page's <c>savedAction</c> column
    /// through <see cref="RenewalActionService.GetActionsAsync"/> in one query, so this asserts that
    /// call returns exactly one entry per contract id that actually has a persisted row, and no
    /// entry at all — never a default/placeholder <see cref="RenewalActionResult"/> — for a
    /// requested id with nothing recorded or one the caller never even asked about.
    /// </summary>
    [Fact]
    public async Task GetActionsAsync_returns_one_row_per_matching_contract_id_and_nothing_for_the_rest()
    {
        await MigrateAsync();

        var tenantId = TenantId.New();
        var contractWithAction = EntityId.New();
        var contractAlsoWithAction = EntityId.New();
        var contractWithoutAction = EntityId.New();
        var contractNeverAsked = EntityId.New();
        var tenantContext = new TenantContext();
        var clock = new FixedClock(new DateTimeOffset(2026, 9, 4, 12, 0, 0, TimeSpan.Zero));
        var auditWriter = new RecordingAuditWriter();

        await using (var db = CreateContext(tenantContext))
        {
            var service = new RenewalActionService(db, tenantContext, clock, auditWriter);
            await service.SetActionAsync(
                tenantId, contractWithAction, "alice@acme.example", "InProgress", "Started negotiation",
                "test-actor@example.com");
            await service.SetActionAsync(
                tenantId, contractAlsoWithAction, "bob@acme.example", "Completed", "Renewed at same terms",
                "test-actor@example.com");
            // A row for a contract id the batch call below never asks about -- proves the query is
            // scoped to the requested ids, not "every row this tenant has".
            await service.SetActionAsync(
                tenantId, contractNeverAsked, "carol@acme.example", "NotStarted", "Reviewing terms",
                "test-actor@example.com");
        }

        await using var readDb = CreateContext(tenantContext);
        var readService = new RenewalActionService(readDb, tenantContext, clock, auditWriter);

        var results = await readService.GetActionsAsync(
            tenantId, [contractWithAction, contractAlsoWithAction, contractWithoutAction]);

        Assert.Equal(2, results.Count);
        Assert.Equal(RenewalActionStatus.InProgress, results[contractWithAction].Status);
        Assert.Equal("Started negotiation", results[contractWithAction].Action);
        Assert.Equal(RenewalActionStatus.Completed, results[contractAlsoWithAction].Status);
        // Asked-about-but-nothing-recorded, and never-asked-about, both simply have no entry (ADR-028
        // §D1: absence of a row is the status NotStarted, reconstructed by the caller, never
        // fabricated by this service).
        Assert.False(results.ContainsKey(contractWithoutAction));
        Assert.False(results.ContainsKey(contractNeverAsked));
    }

    [Fact]
    public async Task GetActionsAsync_returns_an_empty_map_for_an_empty_request()
    {
        await MigrateAsync();

        var tenantContext = new TenantContext();
        await using var db = CreateContext(tenantContext);
        var service = new RenewalActionService(
            db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());

        var results = await service.GetActionsAsync(TenantId.New(), []);

        Assert.Empty(results);
    }
}
