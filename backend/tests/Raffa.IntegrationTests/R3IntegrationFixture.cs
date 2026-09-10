using Raffa.Audit.Infrastructure;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.Renewals.Infrastructure;
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
/// Shared fixture for task E04/F04/US01/T01 (r3-integration) — proves the parent story
/// us-01-final-integration's R3 definition of success ("credit, provenance'd savings from fixture
/// benchmark") against a real, migrated Postgres+pgvector+RLS database and the real
/// <c>Raffa.Api</c> composition root, the same "one real host, no hand-rolled container" shape
/// <see cref="R0IntegrationFixture"/>/<see cref="R1IntegrationFixture"/>/<see cref="R2IntegrationFixture"/>
/// already established. A distinct type rather than a reuse of any of them (each is a different,
/// already-merged task's own artifact, left untouched per this task's own "do not touch unrelated
/// wave artifacts" instruction): this fixture is the first to run with
/// <c>Raffa.Savings.Infrastructure.ServiceCollectionExtensions.AddSavingsModule</c>'s own
/// <c>AddBenchmarkModule()</c> call (this task's own production-code change) actually live, so
/// <see cref="Raffa.Benchmark.IBenchmarkService"/> is resolvable from <see cref="Services"/> for the
/// first time in this test assembly.
///
/// R3's own leaf artifacts (this task's <c>depends_on</c>: benchmark-registry, fixture-confidence,
/// savings-provenance, realized-savings, savings-list) do not need a real, extracted
/// <c>Raffa.Documents.Contracts.Domain.Contract</c> at all — nothing in this codebase yet maps a
/// real contract's line items into a <c>Raffa.Benchmark.Contracts.BenchmarkQuery</c> (no
/// supplier-name or geography field exists on <c>Contract</c> today; `backend/README.md`'s own
/// "Savings Intelligence" section documents this as a still-open, later follow-up), so this fixture
/// carries no contract-seeding helper (unlike <see cref="R2IntegrationFixture.SeedContractAsync"/>) —
/// <see cref="R3EndToEndTests"/> builds a <see cref="Raffa.Benchmark.Contracts.BenchmarkQuery"/> by
/// hand instead, matching one of <c>Raffa.Benchmark.Fixtures.FixtureBenchmarkAdapter</c>'s own
/// catalog rows, and resolves <see cref="Raffa.Savings.Application.SavingsOpportunityService"/>
/// directly from <see cref="Services"/> to identify an opportunity — the same "no dedicated route
/// exists yet, exercise the service the host resolves" convention
/// <see cref="R2EndToEndTests"/>/<see cref="R2CrossTenantIsolationTests"/> already established for
/// <c>Raffa.Renewals.Application.RenewalActionService.GetActionAsync</c>.
///
/// Runs every test request through a dedicated, deliberately unprivileged Postgres role (mirrors
/// every prior R*IntegrationFixture): the Testcontainers bootstrap role is always a superuser, and
/// superusers unconditionally bypass row security, so <see cref="R3CrossTenantIsolationTests"/>'s own
/// proof would otherwise be vacuous.
/// </summary>
public sealed class R3IntegrationFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string AppRoleName = "raffa_r3_app";
    private const string AppRolePassword = "raffa_r3_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    private string _appConnectionString = string.Empty;

    /// <summary>Fake <see cref="IDocumentStorage"/> the test host is wired with — never dialled by
    /// any R3 test (no R3 test uploads a document), but <c>Program.cs</c> constructs the real Azure
    /// Blob adapter unconditionally at startup, so this override exists for the same reason
    /// <see cref="R0IntegrationFixture.DocumentStorage"/>/<see cref="R1IntegrationFixture.DocumentStorage"/>/
    /// <see cref="R2IntegrationFixture.DocumentStorage"/> do (ADR-005/ADR-011: domain code only ever
    /// sees the interface).</summary>
    public RecordingDocumentStorage DocumentStorage { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var superuserConnectionString = _postgres.GetConnectionString();

        var identityOptions = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(identityOptions, superuserConnectionString);
        await using (var db = new IdentityWorkspaceDbContext(identityOptions.Options))
        {
            await db.Database.MigrateAsync();
        }

        var documentsOptions = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(documentsOptions, superuserConnectionString);
        await using (var db = new DocumentsContractsDbContext(documentsOptions.Options))
        {
            await db.Database.MigrateAsync();
        }

        // No R3 scenario exercises the Renewals module, but Program.cs's own fail-fast connection
        // string check requires it regardless (same "point every module at this run's own
        // Testcontainers instance" rationale R1IntegrationFixture/R2IntegrationFixture already give).
        var renewalsOptions = new DbContextOptionsBuilder<RenewalsDbContext>();
        RenewalsDbContextOptions.Configure(renewalsOptions, superuserConnectionString);
        await using (var db = new RenewalsDbContext(renewalsOptions.Options))
        {
            await db.Database.MigrateAsync();
        }

        // First integration fixture that actually needs the Savings schema migrated for real: R0/R1/R2
        // only ever pointed ConnectionStrings:Savings at their own Testcontainers instance so
        // Program.cs's fail-fast config check would pass (see R2IntegrationFixture's own remark on
        // that line) — none of them exercises a Savings table. Applies Initial +
        // AddTenantRowLevelSecurity + AddRealizedSavings + AddRealizedSavingsRowLevelSecurity.
        var savingsOptions = new DbContextOptionsBuilder<Raffa.Savings.Infrastructure.SavingsDbContext>();
        Raffa.Savings.Infrastructure.SavingsDbContextOptions.Configure(savingsOptions, superuserConnectionString);
        await using (var db = new Raffa.Savings.Infrastructure.SavingsDbContext(savingsOptions.Options))
        {
            await db.Database.MigrateAsync();
        }

        var auditOptions = new DbContextOptionsBuilder<AuditDbContext>();
        AuditDbContextOptions.Configure(auditOptions, superuserConnectionString);
        await using (var db = new AuditDbContext(auditOptions.Options))
        {
            await db.Database.MigrateAsync();

            // One unprivileged app role, granted after every module's tables exist (now five,
            // including Savings) — same shape as every prior R*IntegrationFixture.
            await db.Database.ExecuteSqlRawAsync(
                $"""
                CREATE ROLE {AppRoleName} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS;
                GRANT USAGE ON SCHEMA public TO {AppRoleName};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRoleName};
                """);
        }

        _appConnectionString = new NpgsqlConnectionStringBuilder(superuserConnectionString)
        {
            Username = AppRoleName,
            Password = AppRolePassword,
        }.ConnectionString;
    }

    // Explicit implementation — see R0IntegrationFixture's own doc comment on why (xunit's
    // IAsyncLifetime.DisposeAsync and WebApplicationFactory<T>'s own IAsyncDisposable.DisposeAsync
    // must be disambiguated).
    Task IAsyncLifetime.DisposeAsync() => _postgres.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:IdentityWorkspace", _appConnectionString);
        builder.UseSetting("ConnectionStrings:DocumentsContracts", _appConnectionString);
        builder.UseSetting("ConnectionStrings:Audit", _appConnectionString);
        builder.UseSetting("ConnectionStrings:Renewals", _appConnectionString);
        builder.UseSetting("ConnectionStrings:Savings", _appConnectionString);
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
