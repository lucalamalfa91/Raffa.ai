using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Tenancy.Tests;

/// <summary>
/// This is the CI migration check task E01/F04/US03/T01 (AC-4) requires: it runs as part of the
/// normal `dotnet test` step in `.github/workflows/backend.yml`, migrates a disposable Postgres
/// instance, and fails the build if any tenant-scoped table is missing `ROW LEVEL SECURITY`,
/// `FORCE ROW LEVEL SECURITY`, or an actual policy.
///
/// The table list is discovered dynamically from the EF model (every
/// <see cref="TenantScopedEntity"/> subclass) instead of being hardcoded, so this check keeps
/// working -- and keeps rejecting an omission -- as new tenant-scoped tables are added by future
/// tasks/modules, without anyone remembering to update this test.
/// </summary>
public sealed class TenantRlsMigrationCheckTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    public Task InitializeAsync() => _postgres.StartAsync();

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Every_tenant_scoped_table_has_forced_row_level_security_and_a_policy()
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString());

        await using var db = new DocumentsContractsDbContext(optionsBuilder.Options);
        await db.Database.MigrateAsync();

        var tenantScopedTables = db.Model.GetEntityTypes()
            .Where(entityType => typeof(TenantScopedEntity).IsAssignableFrom(entityType.ClrType))
            .Select(entityType => entityType.GetTableName())
            .Where(tableName => tableName is not null)
            .Cast<string>()
            .Distinct()
            .OrderBy(tableName => tableName, StringComparer.Ordinal)
            .ToList();

        // Guards the guard: if this is ever empty, table discovery itself is broken (e.g. a
        // renamed base type) and every assertion below would pass vacuously without proving
        // anything.
        Assert.NotEmpty(tenantScopedTables);

        await db.Database.OpenConnectionAsync();
        try
        {
            foreach (var table in tenantScopedTables)
            {
                var (rowSecurityEnabled, rowSecurityForced) = await ReadRowSecurityFlagsAsync(db, table);

                Assert.True(
                    rowSecurityEnabled,
                    $"[ADR-009/AC-2] Tenant-scoped table \"{table}\" does not have ROW LEVEL " +
                    "SECURITY enabled. Add a migration: ALTER TABLE ... ENABLE ROW LEVEL SECURITY.");
                Assert.True(
                    rowSecurityForced,
                    $"[ADR-009/AC-2] Tenant-scoped table \"{table}\" is not FORCE ROW LEVEL " +
                    "SECURITY, so the policy would not apply to the table owner. Add a " +
                    "migration: ALTER TABLE ... FORCE ROW LEVEL SECURITY.");

                var policyCount = await ReadPolicyCountAsync(db, table);

                Assert.True(
                    policyCount >= 1,
                    $"[ADR-009/AC-4] Tenant-scoped table \"{table}\" has no RLS policy. Add a " +
                    "migration: CREATE POLICY ... ON ... USING (tenant_id = " +
                    "current_setting('app.tenant_id', true)::uuid).");
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<(bool Enabled, bool Forced)> ReadRowSecurityFlagsAsync(
        DocumentsContractsDbContext db, string table)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT relrowsecurity, relforcerowsecurity FROM pg_class WHERE oid = to_regclass(@table)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "table";
        parameter.Value = table;
        command.Parameters.Add(parameter);

        await using var reader = await command.ExecuteReaderAsync();
        var hasRow = await reader.ReadAsync();
        Assert.True(hasRow, $"[ADR-009] Table \"{table}\" was not found in pg_class after migrating.");

        return (reader.GetBoolean(0), reader.GetBoolean(1));
    }

    private static async Task<long> ReadPolicyCountAsync(DocumentsContractsDbContext db, string table)
    {
        var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText =
            "SELECT count(*) FROM pg_policies WHERE schemaname = 'public' AND tablename = @table";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "table";
        parameter.Value = table;
        command.Parameters.Add(parameter);

        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
