using Raffa.Savings.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Raffa.Savings.Tests;

/// <summary>
/// Task E09/F01/US01/T01 (story us-01-idempotent-scripts, AC-2): fails the build if the checked-in
/// <c>Migrations/Scripts/savings.sql</c> is missing, or stale versus the compiled
/// <c>Migrations/</c> — the exact failure mode <c>Raffa.Tenancy.Tests
/// .TenantRlsDeployableScriptCheckTests</c>'s own doc comment describes actually happening once for
/// Documents/Contracts (a migration was added and proved via `MigrateAsync`, but the checked-in
/// script was never regenerated, so a `psql -f` deploy would have shipped a stale schema despite
/// every other test passing). This test asks the compiled EF Core model itself
/// (<see cref="IMigrator.GenerateScript"/>, the same in-process call `dotnet ef migrations script
/// --idempotent` makes under the hood — see <c>Infrastructure.SavingsDbContextFactory</c>, the
/// design-time factory that CLI invocation would use) for a fresh idempotent script and compares
/// it against the checked-in file.
///
/// Deliberately does not spin up Testcontainers/Docker: generating migration SQL only reads the
/// compiled model and migration metadata, it never opens a connection, so this check is fast and
/// runs anywhere `dotnet test` does, independent of <see cref="SavingsMigrationScriptTests"/>'s
/// own Postgres+pgvector container.
/// </summary>
public sealed class SavingsMigrationScriptStaleCheckTests
{
    // Never opened -- constructing a DbContext/IMigrator to generate script text does not connect
    // to a database, so this only has to be a syntactically valid Npgsql connection string.
    private const string UnusedConnectionString =
        "Host=localhost;Port=5432;Database=raffa_stale_check;Username=raffa;Password=raffa";

    private static string ScriptPath(
        [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(here)!, "..", "..",
            "src", "Raffa.Savings", "Migrations", "Scripts",
            "savings.sql"));

    [Fact]
    public async Task Checked_in_script_exists_and_matches_a_fresh_idempotent_generate()
    {
        var path = ScriptPath();
        Assert.True(
            File.Exists(path),
            $"[AC-2] Migration script not found at {path}. Generate it with `dotnet ef " +
            "migrations script --idempotent` from src/Raffa.Savings (task E09/F01/US01/T01).");
        var checkedIn = await File.ReadAllTextAsync(path);

        var optionsBuilder = new DbContextOptionsBuilder<SavingsDbContext>();
        SavingsDbContextOptions.Configure(optionsBuilder, UnusedConnectionString);
        using var db = new SavingsDbContext(optionsBuilder.Options);

        var fresh = db.GetService<IMigrator>()
            .GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Equal(
            Normalize(fresh),
            Normalize(checkedIn));
    }

    /// <summary>
    /// Line-ending-only normalization so a checkout's `core.autocrlf` setting can never fail this
    /// comparison for a reason unrelated to AC-2's actual "stale vs Migrations/" question.
    /// </summary>
    private static string Normalize(string sql) => sql.Replace("\r\n", "\n").TrimEnd('\n');
}
