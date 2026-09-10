using Raffa.Audit.Infrastructure;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.Renewals.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;
using Raffa.Suppliers.Products.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.IntegrationTests;

/// <summary>
/// Shared fixture for task E03/F04/US01/T01 (r2-integration) — proves the parent story
/// us-01-final-integration's R2 definition of success ("dates + alerts + prioritized pipeline")
/// against a real, migrated Postgres+pgvector+RLS database and the real <c>Raffa.Api</c>
/// composition root, the same "one real host, no hand-rolled container" shape
/// <see cref="R0IntegrationFixture"/>/<see cref="R1IntegrationFixture"/> already established. A
/// distinct type rather than a reuse of either (both this task's own scope leaves untouched — they
/// are different, already-merged tasks' artifacts): this fixture is the first to actually migrate
/// <c>Raffa.Renewals.Infrastructure.RenewalsDbContext</c> — R0/R1 only ever pointed
/// <c>ConnectionStrings:Renewals</c> at their own Testcontainers instance so <c>Program.cs</c>'s
/// fail-fast config check would pass (see <see cref="R1IntegrationFixture"/>'s own remark on that
/// line); neither exercises a Renewals table. This fixture also does not need R1's
/// <c>ScriptedR1AiGateway</c>/OCR fixtures at all: R2's own leaf artifacts (this task's
/// <c>depends_on</c> — renewal-opportunity/-priority-explain/-alerts/-action) all take
/// already-validated contract data as an input, never produce it (Documents/Contracts' own
/// extraction pipeline is R1's proof, not this one's) — so every contract this fixture's tests need
/// is seeded directly via <see cref="SeedContractAsync"/>, the same direct-seed boundary
/// <c>Raffa.Documents.Contracts.Tests.PortfolioQueryServiceTests</c> already draws for the
/// identical "no ContractUploadService exists" reason.
///
/// Runs every test request through a dedicated, deliberately unprivileged Postgres role (mirrors
/// <see cref="R0IntegrationFixture"/>/<see cref="R1IntegrationFixture"/>): the Testcontainers
/// bootstrap role is always a superuser, and superusers unconditionally bypass row security, so
/// this task's own AC-3 ("tenant isolation") proof would otherwise be vacuous — this is also what
/// makes <c>R2EndToEndTests</c>' threshold-scheduler assertion a real proof of the RLS-scope fix
/// that same task made to <c>RenewalThresholdScheduler</c>, not one that would pass even against a
/// broken tenant claim.
/// </summary>
public sealed class R2IntegrationFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string AppRoleName = "raffa_r2_app";
    private const string AppRolePassword = "raffa_r2_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    private string _appConnectionString = string.Empty;

    /// <summary>Fake <see cref="IDocumentStorage"/> the test host is wired with — never dialled by
    /// any R2 test (no R2 test uploads a document), but <c>Program.cs</c> constructs the real Azure
    /// Blob adapter unconditionally at startup, so this override exists for the same reason
    /// <see cref="R0IntegrationFixture.DocumentStorage"/>/<see cref="R1IntegrationFixture.DocumentStorage"/>
    /// do (ADR-005/ADR-011: domain code only ever sees the interface).</summary>
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

        // First integration fixture that actually needs the Renewals schema (see the type doc
        // comment) — applies Initial + AddTenantRowLevelSecurity, covering renewal_action.
        var renewalsOptions = new DbContextOptionsBuilder<RenewalsDbContext>();
        RenewalsDbContextOptions.Configure(renewalsOptions, superuserConnectionString);
        await using (var db = new RenewalsDbContext(renewalsOptions.Options))
        {
            await db.Database.MigrateAsync();
        }

        // Task E13/F03/US01/T02 (supplier-extraction): `GET /api/contracts` and `GET /api/renewals`
        // now resolve every row's supplier *name* through ISupplierNameLookup (R-SUP-04), which
        // reads Raffa.Suppliers.Products' own table — and this fixture's SeedContractAsync is the
        // only one in this project that seeds a Contract with a non-null SupplierId, so it is the
        // only pre-existing fixture whose requests actually reach that query. Migrating the table
        // here (and routing ConnectionStrings:Suppliers at this same container below) mirrors the
        // identical block R0IntegrationFixture/R1IntegrationFixture already carry; without it the
        // lookup would dial appsettings.Development.json's static local-dev string instead.
        var suppliersOptions = new DbContextOptionsBuilder<SuppliersDbContext>();
        SuppliersDbContextOptions.Configure(suppliersOptions, superuserConnectionString);
        await using (var db = new SuppliersDbContext(suppliersOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity for Raffa.Suppliers.Products' own table.
            await db.Database.MigrateAsync();
        }

        var auditOptions = new DbContextOptionsBuilder<AuditDbContext>();
        AuditDbContextOptions.Configure(auditOptions, superuserConnectionString);
        await using (var db = new AuditDbContext(auditOptions.Options))
        {
            await db.Database.MigrateAsync();

            // One unprivileged app role, granted after every module's tables exist (now five,
            // including Renewals and Suppliers) — same shape as R0IntegrationFixture/R1IntegrationFixture.
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
        // Task E04/F02/US02/T01 (savings-opportunity) gave Raffa.Savings its first DbContext, so
        // Program.cs now requires this key too (same fail-fast guard as the four above). No R2
        // scenario exercises the Savings module yet, but pointing it at this run's own Postgres
        // container (rather than a bare syntactically-valid placeholder) costs nothing and keeps
        // every module's connection string uniformly "this run's own database" should a future R3+
        // fixture extend this one instead of adding a new type.
        builder.UseSetting("ConnectionStrings:Savings", _appConnectionString);
        // Task E13/F03/US01/T02: really dialled, by every `GET /api/contracts` / `GET /api/renewals`
        // request whose page has at least one contract with a SupplierId — see the migration block
        // in InitializeAsync above.
        builder.UseSetting("ConnectionStrings:Suppliers", _appConnectionString);
        // Never actually dialled — IDocumentStorage is replaced with an in-memory fake below —
        // but Program.cs's own startup check requires a non-null configuration value to be
        // present (same syntactically-valid-value approach R0IntegrationFixture/R1IntegrationFixture
        // already use).
        builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IDocumentStorage>(DocumentStorage);
        });
    }

    /// <summary>
    /// Seeds one <see cref="Contract"/> (and optionally one <see cref="Risk"/> for it) directly via
    /// the app-role <see cref="DocumentsContractsDbContext"/>, inside its own tenant scope, so RLS's
    /// `WITH CHECK` is satisfied on insert — same rationale as
    /// <c>Raffa.Documents.Contracts.Tests.PortfolioQueryServiceTests.SeedContractAsync</c>. There
    /// is still no <c>ContractUploadService</c>-equivalent write path a test could go through
    /// instead (see that type's own doc comment): R2's own leaf artifacts all take already-validated
    /// contract data as an input, never produce it, so seeding directly is the correct boundary for
    /// this fixture's tests, not a shortcut around a real write path that exists.
    /// </summary>
    public async Task<Contract> SeedContractAsync(
        TenantId tenantId,
        EntityId? supplierId = null,
        decimal? annualSpend = null,
        DateOnly? endDate = null,
        DateOnly? cancellationDeadline = null,
        bool autoRenewal = true,
        RiskSeverity? risk = null)
    {
        var tenantContext = new TenantContext();
        var contract = new Contract
        {
            TenantId = tenantId,
            SupplierId = supplierId,
            Type = ContractDocumentType.Msa,
            Status = "Active",
            Currency = "USD",
            AnnualSpend = annualSpend,
            EndDate = endDate,
            CancellationDeadline = cancellationDeadline,
            AutoRenewal = autoRenewal,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        await using var db = new DocumentsContractsDbContext(optionsBuilder.Options);
        using var scope = tenantContext.BeginScope(tenantId);

        db.Contracts.Add(contract);

        if (risk is { } severity)
        {
            db.Risks.Add(new Risk
            {
                TenantId = tenantId,
                ContractId = contract.Id,
                RiskType = "test-risk",
                Severity = severity,
                Description = "seeded for R2 integration test",
                IdentifiedAt = DateTimeOffset.UtcNow,
            });
        }

        await db.SaveChangesAsync();
        return contract;
    }
}
