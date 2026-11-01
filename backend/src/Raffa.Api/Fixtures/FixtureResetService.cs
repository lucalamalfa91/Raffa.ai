using Npgsql;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Fixtures;

namespace Raffa.Api.Fixtures;

/// <summary>What the last reset seeded (in memory: a restarted API reports no scenario).</summary>
internal sealed class FixtureSessionState
{
    private string? _scenario;

    public string? Scenario => Volatile.Read(ref _scenario);

    public void Set(string scenario) => Volatile.Write(ref _scenario, scenario);
}

/// <summary>
/// <c>POST /api/fixtures/reset</c>: every seeder's wipe (descending order), a sweep of every row of
/// the fixture tenant in every <c>tenant_id</c> table of every configured database, then every
/// seeder (ascending order). One reset at a time.
/// </summary>
internal sealed class FixtureResetService(
    IEnumerable<IFixtureScenarioSeeder> seeders,
    IConfiguration configuration,
    IHostEnvironment environment,
    FixtureOptions options,
    FixtureSessionState state,
    IClock clock,
    ILogger<FixtureResetService> logger)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Connection strings that are not a tenant database.</summary>
    private static readonly HashSet<string> NotTenantDatabases = new(StringComparer.OrdinalIgnoreCase) { "Storage", "Market" };

    public FixtureSeedContext CreateContext() =>
        new(FixtureTenant.TenantId, FixtureTenant.Name, FixtureTenant.Users, clock.UtcNow,
            options.ResolveDataPath(environment.ContentRootPath));

    public async Task ResetAsync(string scenario, CancellationToken cancellationToken)
    {
        if (!FixtureScenarios.IsKnown(scenario))
        {
            throw new ArgumentException($"Unknown fixture scenario '{scenario}'.", nameof(scenario));
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var context = CreateContext();
            var ordered = seeders.OrderBy(s => s.Order).ToList();

            for (var i = ordered.Count - 1; i >= 0; i--)
            {
                await ordered[i].WipeAsync(context, cancellationToken).ConfigureAwait(false);
            }

            foreach (var connectionString in TenantConnectionStrings())
            {
                await SweepTenantAsync(connectionString, context.TenantId, cancellationToken).ConfigureAwait(false);
            }

            foreach (var seeder in ordered)
            {
                await seeder.SeedAsync(context, scenario, cancellationToken).ConfigureAwait(false);
            }

            state.Set(scenario);
            logger.LogInformation("Fixture tenant reset to scenario {Scenario} by {SeederCount} seeders.", scenario, ordered.Count);
        }
        finally
        {
            Gate.Release();
        }
    }

    private IEnumerable<string> TenantConnectionStrings() =>
        configuration.GetSection("ConnectionStrings").GetChildren()
            .Where(c => !NotTenantDatabases.Contains(c.Key) && !string.IsNullOrWhiteSpace(c.Value))
            .Select(c => c.Value!)
            .Distinct(StringComparer.Ordinal);

    /// <summary>
    /// Deletes the tenant's rows from every table with a <c>tenant_id</c> column, inside one
    /// transaction with the RLS tenant claim set (never a bypass). Tables are retried in passes
    /// until foreign keys allow every delete.
    /// </summary>
    private static async Task SweepTenantAsync(string connectionString, TenantId tenantId, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        await using (var set = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, true)", connection, transaction))
        {
            set.Parameters.AddWithValue("t", tenantId.Value.ToString("D"));
            await set.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        var tables = new List<string>();
        await using (var list = new NpgsqlCommand(
            "SELECT c.table_name FROM information_schema.columns c JOIN information_schema.tables t " +
            "ON t.table_schema = c.table_schema AND t.table_name = c.table_name " +
            "WHERE c.table_schema = current_schema() AND c.column_name = 'tenant_id' AND t.table_type = 'BASE TABLE' " +
            "ORDER BY c.table_name", connection, transaction))
        await using (var reader = await list.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var remaining = tables;
        while (remaining.Count > 0)
        {
            var blocked = new List<string>();
            foreach (var table in remaining)
            {
                await using var savepoint = new NpgsqlCommand("SAVEPOINT sweep", connection, transaction);
                await savepoint.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await using var delete = new NpgsqlCommand(
                        $"DELETE FROM \"{table.Replace("\"", "\"\"", StringComparison.Ordinal)}\" WHERE tenant_id = @t", connection, transaction);
                    delete.Parameters.AddWithValue("t", tenantId.Value);
                    await delete.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    await using var release = new NpgsqlCommand("RELEASE SAVEPOINT sweep", connection, transaction);
                    await release.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.ForeignKeyViolation)
                {
                    await using var rollback = new NpgsqlCommand("ROLLBACK TO SAVEPOINT sweep", connection, transaction);
                    await rollback.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    blocked.Add(table);
                }
            }

            if (blocked.Count == remaining.Count)
            {
                throw new InvalidOperationException(
                    "Fixture sweep cannot delete the tenant's rows from: " + string.Join(", ", blocked) +
                    " (a foreign key from a table without tenant_id holds them).");
            }

            remaining = blocked;
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
