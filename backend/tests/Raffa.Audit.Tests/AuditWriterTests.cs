using Raffa.Audit.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Audit.Tests;

/// <summary>
/// Proves the Definition of Done for task E01/F06/US02/T01 (story us-02-audit-baseline, AC-1):
/// <see cref="AuditWriter"/> — this module's <see cref="IAuditWriter"/> implementation, the
/// produced `audit-abstraction` artifact — genuinely persists a caller's <see cref="AuditEntry"/>
/// as a readable row, through the exact same Npgsql/EF Core/RLS-interceptor pipeline every other
/// module's write path uses (ADR-003/ADR-009). Any module in this solution can depend on
/// <see cref="IAuditWriter"/> alone (from <c>Raffa.SharedKernel</c>) and get this behaviour
/// without knowing anything about Postgres, EF Core, or this module's internals.
///
/// Spins up its own disposable Postgres container per test run (Testcontainers), so this test
/// needs nothing but a running Docker daemon; no shared/external database to stand up by hand.
/// </summary>
public sealed class AuditWriterTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AuditDbContext CreateContext(ITenantContext? tenantContext = null)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AuditDbContext>();
        AuditDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString(), tenantContext);
        return new AuditDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task WriteAsync_persists_the_entry_as_a_readable_append_only_audit_event_row()
    {
        await using (var migrateDb = CreateContext())
        {
            await migrateDb.Database.MigrateAsync();
        }

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        using var _ = tenantContext.BeginScope(tenantId);

        var entry = new AuditEntry(
            TenantId: tenantId,
            Actor: "user-123@acme.example",
            Action: "document.upload",
            ResourceType: "Document",
            ResourceId: Guid.NewGuid().ToString(),
            Timestamp: new DateTimeOffset(2026, 9, 3, 9, 30, 0, TimeSpan.Zero),
            Detail: "uploaded via web");

        await using (var writeDb = CreateContext(tenantContext))
        {
            IAuditWriter writer = new AuditWriter(writeDb);
            await writer.WriteAsync(entry);
        }

        // Fresh context/connection, same tenant scope: this reads back from Postgres, not the
        // change tracker, and only succeeds if RLS actually lets this tenant see its own row.
        await using var readDb = CreateContext(tenantContext);
        var stored = await readDb.AuditEvents.SingleAsync();

        Assert.Equal(entry.TenantId, stored.TenantId);
        Assert.Equal(entry.Actor, stored.Actor);
        Assert.Equal(entry.Action, stored.Action);
        Assert.Equal(entry.ResourceType, stored.ResourceType);
        Assert.Equal(entry.ResourceId, stored.ResourceId);
        Assert.Equal(entry.Timestamp, stored.OccurredAt);
        Assert.Equal(entry.Detail, stored.Detail);
    }

    [Fact]
    public async Task WriteAsync_persists_a_null_detail_when_the_caller_supplies_none()
    {
        await using (var migrateDb = CreateContext())
        {
            await migrateDb.Database.MigrateAsync();
        }

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);

        var entry = new AuditEntry(
            TenantId: tenantId,
            Actor: "background-job:extraction-worker",
            Action: "contract.extract",
            ResourceType: "Contract",
            ResourceId: Guid.NewGuid().ToString(),
            Timestamp: DateTimeOffset.UtcNow);

        await using (var writeDb = CreateContext(tenantContext))
        {
            IAuditWriter writer = new AuditWriter(writeDb);
            await writer.WriteAsync(entry);
        }

        await using var readDb = CreateContext(tenantContext);
        var stored = await readDb.AuditEvents.SingleAsync();

        Assert.Null(stored.Detail);
    }

    [Fact]
    public async Task WriteAsync_assigns_each_call_its_own_row_rather_than_reusing_one()
    {
        await using (var migrateDb = CreateContext())
        {
            await migrateDb.Database.MigrateAsync();
        }

        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        using var _ = tenantContext.BeginScope(tenantId);

        await using (var writeDb = CreateContext(tenantContext))
        {
            IAuditWriter writer = new AuditWriter(writeDb);

            await writer.WriteAsync(new AuditEntry(
                tenantId, "user-1", "document.upload", "Document", "doc-1", DateTimeOffset.UtcNow));
            await writer.WriteAsync(new AuditEntry(
                tenantId, "user-1", "document.delete", "Document", "doc-1", DateTimeOffset.UtcNow));
        }

        await using var readDb = CreateContext(tenantContext);
        var stored = await readDb.AuditEvents.OrderBy(e => e.Action).ToListAsync();

        Assert.Equal(2, stored.Count);
        Assert.NotEqual(stored[0].Id, stored[1].Id);
    }
}
