using Raffa.Audit.Domain;
using Raffa.Audit.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Audit.Tests;

/// <summary>
/// Proves the Definition of Done for task E01/F06/US02/T01's own coding objective — "Implement
/// append-only audit abstraction" — at the database level, not just at the C# API surface.
/// Migration `AddAppendOnlyEnforcement` installs a `BEFORE UPDATE OR DELETE` trigger on
/// `audit_event`; these tests prove it actually rejects both operations (regardless of which
/// Postgres role issues them — even the migrating/table-owning connection used here, which is
/// deliberately the least favourable case for this trigger-based approach, unlike Row-Level
/// Security's `FORCE` clause which only targets non-owner roles) while leaving `INSERT`
/// untouched, so <see cref="AuditWriter"/> keeps working.
///
/// Spins up its own disposable Postgres container per test run (Testcontainers), so this test
/// needs nothing but a running Docker daemon; no shared/external database to stand up by hand.
/// </summary>
public sealed class AuditAppendOnlyEnforcementTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AuditDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<AuditDbContext>();
        AuditDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString());
        return new AuditDbContext(optionsBuilder.Options);
    }

    private static async Task<AuditEvent> SeedOneEventAsync(AuditDbContext db)
    {
        var auditEvent = new AuditEvent
        {
            TenantId = TenantId.New(),
            Actor = "user-1",
            Action = "document.upload",
            ResourceType = "Document",
            ResourceId = Guid.NewGuid().ToString(),
            OccurredAt = DateTimeOffset.UtcNow,
        };

        db.AuditEvents.Add(auditEvent);
        await db.SaveChangesAsync();
        return auditEvent;
    }

    [Fact]
    public async Task Updating_an_existing_audit_event_row_is_rejected_by_the_database()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var auditEvent = await SeedOneEventAsync(db);

        // Mutating the already-tracked entity is enough for EF Core's change tracker to issue an
        // UPDATE on the next SaveChanges — the same path a future (mistaken) caller would hit.
        auditEvent.Action = "tampered";

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        var postgresException = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, postgresException.SqlState);
        Assert.Contains("append-only", postgresException.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deleting_an_existing_audit_event_row_is_rejected_by_the_database()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        var auditEvent = await SeedOneEventAsync(db);

        db.AuditEvents.Remove(auditEvent);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        var postgresException = Assert.IsType<PostgresException>(ex.InnerException);
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, postgresException.SqlState);
        Assert.Contains("append-only", postgresException.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Row_survives_untouched_after_a_rejected_update_attempt()
    {
        await using var seedDb = CreateContext();
        await seedDb.Database.MigrateAsync();
        var auditEvent = await SeedOneEventAsync(seedDb);

        await using (var mutateDb = CreateContext())
        {
            var tracked = await mutateDb.AuditEvents.SingleAsync(e => e.Id == auditEvent.Id);
            tracked.Action = "tampered";
            await Assert.ThrowsAsync<DbUpdateException>(() => mutateDb.SaveChangesAsync());
        }

        // A fresh context/connection: the rejected UPDATE must not have partially applied.
        await using var readDb = CreateContext();
        var stored = await readDb.AuditEvents.SingleAsync(e => e.Id == auditEvent.Id);
        Assert.Equal("document.upload", stored.Action);
    }

    [Fact]
    public async Task Inserting_a_new_audit_event_row_is_still_permitted()
    {
        await using var db = CreateContext();
        await db.Database.MigrateAsync();

        // Must not throw: the trigger only fires BEFORE UPDATE OR DELETE, never BEFORE INSERT.
        await SeedOneEventAsync(db);

        Assert.Equal(1, await db.AuditEvents.CountAsync());
    }
}
