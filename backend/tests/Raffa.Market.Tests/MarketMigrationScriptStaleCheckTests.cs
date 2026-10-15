using Raffa.Market.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Raffa.Market.Tests;

/// <summary>
/// Fails the build if the checked-in <c>Migrations/Scripts/market.sql</c> is stale versus the
/// compiled <c>Migrations/</c> (same guard the Savings/Quotes/Audit modules have). The EF-generated
/// part of the file -- from the <c>__EFMigrationsHistory</c> bootstrap to the last <c>COMMIT;</c> --
/// must equal a fresh idempotent script; the hand-written header and the grant-model footer are
/// outside that window by design. No database is opened.
/// </summary>
public sealed class MarketMigrationScriptStaleCheckTests
{
    private const string UnusedConnectionString =
        "Host=localhost;Port=5432;Database=raffa_stale_check;Username=raffa;Password=raffa";

    private const string BodyStart = "CREATE TABLE IF NOT EXISTS \"__EFMigrationsHistory\"";

    private static string ScriptPath(
        [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(here)!, "..", "..",
            "src", "Raffa.Market", "Migrations", "Scripts", "market.sql"));

    [Fact]
    public async Task Checked_in_script_matches_a_fresh_idempotent_generate()
    {
        var path = ScriptPath();
        Assert.True(File.Exists(path), $"Migration script not found at {path}.");
        var checkedIn = await File.ReadAllTextAsync(path);

        var optionsBuilder = new DbContextOptionsBuilder<MarketDbContext>();
        MarketDbContextOptions.Configure(optionsBuilder, UnusedConnectionString);
        using var db = new MarketDbContext(optionsBuilder.Options);

        var fresh = db.GetService<IMigrator>()
            .GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Equal(EfBody(fresh), EfBody(checkedIn));
    }

    private static string EfBody(string sql)
    {
        var normalized = sql.Replace("\r\n", "\n").TrimStart('﻿');
        var start = normalized.IndexOf(BodyStart, StringComparison.Ordinal);
        Assert.True(start >= 0, "The script has no __EFMigrationsHistory bootstrap.");
        var end = normalized.LastIndexOf("COMMIT;", StringComparison.Ordinal);
        Assert.True(end > start, "The script has no COMMIT.");
        return normalized[start..(end + "COMMIT;".Length)];
    }
}
