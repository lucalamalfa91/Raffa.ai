using Raffa.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Raffa.Chat.Tests;

/// <summary>
/// ADR-021 (same shape as <c>Raffa.Savings.Tests.SavingsMigrationScriptStaleCheckTests</c>): fails
/// the build if the checked-in <c>Migrations/Scripts/chat.sql</c> is missing, or stale versus the
/// compiled <c>Migrations/</c>. Asks the compiled model itself for a fresh idempotent script — no
/// Docker, no connection — so a migration added without regenerating the script (the exact way
/// ADR-030's <c>feature_request</c> table could have shipped unapplied) fails here, anywhere
/// `dotnet test` runs.
/// </summary>
public sealed class ChatMigrationScriptStaleCheckTests
{
    private const string UnusedConnectionString =
        "Host=localhost;Port=5432;Database=raffa_stale_check;Username=raffa;Password=raffa";

    private static string ScriptPath(
        [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(here)!, "..", "..",
            "src", "Raffa.Chat", "Migrations", "Scripts",
            "chat.sql"));

    [Fact]
    public async Task Checked_in_script_exists_and_matches_a_fresh_idempotent_generate()
    {
        var path = ScriptPath();
        Assert.True(
            File.Exists(path),
            $"Migration script not found at {path}. Generate it with `dotnet ef migrations script --idempotent` from src/Raffa.Chat.");
        var checkedIn = await File.ReadAllTextAsync(path);

        var optionsBuilder = new DbContextOptionsBuilder<ChatDbContext>();
        ChatDbContextOptions.Configure(optionsBuilder, UnusedConnectionString);
        using var db = new ChatDbContext(optionsBuilder.Options);

        var fresh = db.GetService<IMigrator>()
            .GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);

        Assert.Equal(Normalize(fresh), Normalize(checkedIn));
    }

    private static string Normalize(string sql) => sql.Replace("\r\n", "\n").TrimEnd('\n');
}
