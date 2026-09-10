using Raffa.Audit.Infrastructure;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.Quotes.Infrastructure;
using Raffa.Renewals.Infrastructure;
using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.IntegrationTests;

/// <summary>
/// Shared fixture for task E10/F01/US01/T01 (seed-demo-fixture) — proves that
/// <c>backend/scripts/demo-fixture-seed.sql</c> (the checked-in artifact
/// <c>.github/workflows/seed-demo-fixture.yml</c> actually runs against
/// `raffa_dev`/`raffa_demo`) applies cleanly to a real, migrated
/// Postgres+RLS database and that the real <c>Raffa.Api</c> composition
/// root then serves it back through `GET /api/savings` — the same "one
/// real host, no hand-rolled container" shape
/// <see cref="R3IntegrationFixture"/>/<see cref="R4IntegrationFixture"/>
/// already established. A distinct type rather than a reuse of either (each
/// is a different, already-merged task's own artifact, left untouched
/// per this task's own "do not touch unrelated wave artifacts"
/// instruction) — migrates only the three modules the seed script actually
/// writes to (Identity.Workspace, Documents.Contracts, Savings) plus Audit
/// (every prior R*IntegrationFixture's own home for the unprivileged app
/// role), and otherwise mirrors <see cref="R4IntegrationFixture"/>'s
/// complete `ConnectionStrings:*` set — <c>Program.cs</c>'s own fail-fast
/// startup check requires all seven regardless of which modules this
/// fixture's own tests exercise.
///
/// The seed script itself runs over the Testcontainers bootstrap
/// (superuser) connection, mirroring ADR-021's own "scripts run as the
/// Flexible Server administrator" convention — never the unprivileged app
/// role <see cref="R3IntegrationFixture"/>'s own doc comment explains every
/// R*IntegrationFixture creates so its RLS proof is not vacuous. That role
/// is created here only *after* the seed has already run, the same order
/// `.github/workflows/backend.yml`/`seed-demo-fixture.yml` apply things in
/// production: schema (superuser) → seed (superuser) → application traffic
/// (the non-bypass app role).
/// </summary>
public sealed class DemoFixtureSeedIntegrationFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>Same fixed id <c>backend/scripts/demo-fixture-seed.sql</c> inserts
    /// <c>workspace</c>/<c>savings_opportunity</c> rows under — the header comment on that
    /// checked-in script is the source of truth this constant mirrors, not the other way round.</summary>
    public const string DemoTenantId = "00000000-0000-0000-0000-000000000001";

    private const string AppRoleName = "raffa_e10_seed_app";
    private const string AppRolePassword = "raffa_e10_seed_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    private string _superuserConnectionString = string.Empty;
    private string _appConnectionString = string.Empty;

    /// <summary>Fake <see cref="IDocumentStorage"/> the test host is wired with — never dialled
    /// by any test in this fixture (no test here uploads a document), but <c>Program.cs</c>
    /// constructs the real Azure Blob adapter unconditionally at startup, so this override exists
    /// for the same reason every prior R*IntegrationFixture's own <c>DocumentStorage</c> does
    /// (ADR-005/ADR-011: domain code only ever sees the interface).</summary>
    public RecordingDocumentStorage DocumentStorage { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _superuserConnectionString = _postgres.GetConnectionString();

        var identityOptions = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(identityOptions, _superuserConnectionString);
        await using (var db = new IdentityWorkspaceDbContext(identityOptions.Options))
        {
            await db.Database.MigrateAsync();
        }

        var documentsOptions = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(documentsOptions, _superuserConnectionString);
        await using (var db = new DocumentsContractsDbContext(documentsOptions.Options))
        {
            await db.Database.MigrateAsync();
        }

        var savingsOptions = new DbContextOptionsBuilder<SavingsDbContext>();
        SavingsDbContextOptions.Configure(savingsOptions, _superuserConnectionString);
        await using (var db = new SavingsDbContext(savingsOptions.Options))
        {
            await db.Database.MigrateAsync();
        }

        // The artifact under test: the exact, checked-in file
        // .github/workflows/seed-demo-fixture.yml applies with `psql -f` against a real
        // raffa_dev/raffa_demo — read from disk, never re-typed into this test (so it can
        // never silently drift from what actually ships, the same "prove the deployable artifact
        // itself" discipline TenantRlsDeployableScriptCheckTests already established).
        await ApplySeedScriptAsync();

        var auditOptions = new DbContextOptionsBuilder<AuditDbContext>();
        AuditDbContextOptions.Configure(auditOptions, _superuserConnectionString);
        await using (var db = new AuditDbContext(auditOptions.Options))
        {
            await db.Database.MigrateAsync();

            // One unprivileged app role, granted after every module's tables exist (and after the
            // seed above has already run as the superuser/administrator connection) — same shape
            // as every prior R*IntegrationFixture/QuoteIntegrationFixture.
            await db.Database.ExecuteSqlRawAsync(
                $"""
                CREATE ROLE {AppRoleName} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS;
                GRANT USAGE ON SCHEMA public TO {AppRoleName};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRoleName};
                """);
        }

        _appConnectionString = new NpgsqlConnectionStringBuilder(_superuserConnectionString)
        {
            Username = AppRoleName,
            Password = AppRolePassword,
        }.ConnectionString;
    }

    /// <summary>Re-runs the checked-in seed script over the same superuser connection — the
    /// fixture's own proof surface for story us-01-seed-demo-db AC-1's "repeatable": a second
    /// apply must not throw and must not duplicate rows (every INSERT is <c>ON CONFLICT (id) DO
    /// NOTHING</c> against a fixed id).</summary>
    public Task ReapplySeedAsync() => ApplySeedScriptAsync();

    private async Task ApplySeedScriptAsync()
    {
        var script = await File.ReadAllTextAsync(SeedScriptPath());

        await using var connection = new NpgsqlConnection(_superuserConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(script, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Walks from this source file
    /// (<c>backend/tests/Raffa.IntegrationTests/</c>) to the checked-in seed script under
    /// <c>backend/scripts/</c> — same <see cref="System.Runtime.CompilerServices.CallerFilePathAttribute"/>
    /// resolve-at-compile-time-against-the-source-tree trick
    /// <c>Raffa.Tenancy.Tests.TenantRlsDeployableScriptCheckTests.ScriptPath</c> already uses, so
    /// this is stable across however <c>dotnet test</c> is invoked.</summary>
    private static string SeedScriptPath(
        [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "scripts", "demo-fixture-seed.sql"));

    // Explicit implementation — see R0IntegrationFixture's own doc comment on why (xunit's
    // IAsyncLifetime.DisposeAsync and WebApplicationFactory<T>'s own IAsyncDisposable.DisposeAsync
    // must be disambiguated).
    Task IAsyncLifetime.DisposeAsync() => _postgres.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:IdentityWorkspace", _appConnectionString);
        builder.UseSetting("ConnectionStrings:DocumentsContracts", _appConnectionString);
        builder.UseSetting("ConnectionStrings:Audit", _appConnectionString);
        builder.UseSetting("ConnectionStrings:Savings", _appConnectionString);
        // No test in this fixture exercises Renewals/Quotes, but Program.cs's own fail-fast
        // connection string check requires both regardless (same "point every module at this
        // run's own Testcontainers instance" rationale every prior R*IntegrationFixture gives) —
        // neither module's schema is migrated here since nothing queries it.
        builder.UseSetting("ConnectionStrings:Renewals", _appConnectionString);
        builder.UseSetting("ConnectionStrings:Quotes", _appConnectionString);
        // Never actually dialled — IDocumentStorage is replaced with an in-memory fake below —
        // but Program.cs's own startup check requires a non-null configuration value to be
        // present (same syntactically-valid-value approach every prior R*IntegrationFixture uses).
        builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IDocumentStorage>(DocumentStorage);
        });
    }
}
