using Raffa.Audit.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Audit.Tests;

/// <summary>
/// Proves the Definition of Done for task E01/F06/US02/T01 (story us-02-audit-baseline): `dotnet
/// ef migrations add` + `database update` succeed against a real Postgres instance, and all three
/// migrations this task adds (schema, RLS, append-only enforcement) are applied together — not
/// just declared in the model.
///
/// Spins up its own disposable Postgres container per test run (Testcontainers), so this test
/// needs nothing but a running Docker daemon; no shared/external database to stand up by hand.
/// </summary>
public sealed class AuditMigrationTests : IAsyncLifetime
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

    [Fact]
    public async Task Migrate_applies_schema_rls_and_append_only_migrations_against_a_real_postgres()
    {
        await using var db = CreateContext();

        await db.Database.MigrateAsync();

        var appliedMigrations = (await db.Database.GetAppliedMigrationsAsync()).ToList();

        Assert.Contains(appliedMigrations, id => id.EndsWith("_Initial", StringComparison.Ordinal));
        Assert.Contains(
            appliedMigrations, id => id.EndsWith("_AddTenantRowLevelSecurity", StringComparison.Ordinal));
        Assert.Contains(
            appliedMigrations, id => id.EndsWith("_AddAppendOnlyEnforcement", StringComparison.Ordinal));
    }
}
