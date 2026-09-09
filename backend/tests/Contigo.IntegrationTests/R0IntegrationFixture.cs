using Contigo.Audit.Infrastructure;
using Contigo.Chat.Infrastructure;
using Contigo.Documents.Contracts.Infrastructure;
using Contigo.Identity.Workspace.Infrastructure;
using Contigo.SharedKernel.Storage;
using Contigo.Suppliers.Products.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Contigo.IntegrationTests;

/// <summary>
/// Shared fixture for task E01/F09/US01/T01 (r0-integration): one real, migrated Postgres (via
/// Testcontainers) standing in for the single "system of record" ADR-003 describes, backing all
/// three bounded contexts the R0 path touches (Identity/Workspace, Documents/Contracts, Audit) at
/// once — the same shared-database shape <c>appsettings.Development.json</c> already uses for
/// DocumentsContracts+Audit, extended here to include IdentityWorkspace now that
/// <c>Contigo.Api.Program</c> wires all three (this task). One
/// <see cref="WebApplicationFactory{TEntryPoint}"/> around the *real* <c>Program</c> composition
/// root drives every HTTP call the tests make — nothing here hand-rolls a parallel container.
///
/// Runs every test request through a dedicated, deliberately unprivileged Postgres role (mirrors
/// every per-module RLS proof in this solution — e.g.
/// <c>Contigo.Identity.Workspace.Tests.WorkspaceRlsCrossTenantIsolationTests</c> — the
/// Testcontainers bootstrap role is always a superuser, and superusers unconditionally bypass row
/// security, so this task's own cross-tenant proof would otherwise be vacuous).
/// </summary>
public sealed class R0IntegrationFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string AppRoleName = "contigo_r0_app";
    private const string AppRolePassword = "contigo_r0_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    private string _appConnectionString = string.Empty;

    /// <summary>
    /// The fake <see cref="IDocumentStorage"/> the test host is wired with (see
    /// <see cref="ConfigureWebHost"/>) — lets a test assert what actually reached "storage"
    /// without a real Azure Blob/Azurite dependency (ADR-005/ADR-011: domain code only ever sees
    /// the interface; the host is free to substitute the adapter).
    /// </summary>
    public RecordingDocumentStorage DocumentStorage { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var superuserConnectionString = _postgres.GetConnectionString();

        var identityOptions = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(identityOptions, superuserConnectionString);
        await using (var db = new IdentityWorkspaceDbContext(identityOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity for Identity/Workspace's own tables.
            await db.Database.MigrateAsync();
        }

        var documentsOptions = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(documentsOptions, superuserConnectionString);
        await using (var db = new DocumentsContractsDbContext(documentsOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity for Documents/Contracts' own tables.
            await db.Database.MigrateAsync();
        }

        // Task E13/F06/US01/T01 (ask-engine): AskCopilotService (behind both POST /api/chat/query
        // and POST /api/conversations/{id}/messages, which AskContigoRagCrossTenantIsolationTests
        // now exercises for its own AC-9 proof) resolves supplier names via ISupplierNameLookup for
        // every turn whose portfolio has at least one contract with a SupplierId, and every turn
        // persists through ConversationService — so this fixture, previously scoped to the R0 path
        // only, must also migrate Contigo.Chat's and Contigo.Suppliers.Products' own tables and
        // route their connection strings at this same Testcontainer (see ConfigureWebHost below),
        // the same "a later task's new required ConnectionStrings key needs a matching migration
        // block here" pattern this fixture already applies for Renewals/Savings above.
        var chatOptions = new DbContextOptionsBuilder<ChatDbContext>();
        ChatDbContextOptions.Configure(chatOptions, superuserConnectionString);
        await using (var db = new ChatDbContext(chatOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity for Contigo.Chat's own tables
            // (Conversation, ConversationMessage).
            await db.Database.MigrateAsync();
        }

        var suppliersOptions = new DbContextOptionsBuilder<SuppliersDbContext>();
        SuppliersDbContextOptions.Configure(suppliersOptions, superuserConnectionString);
        await using (var db = new SuppliersDbContext(suppliersOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity for Contigo.Suppliers.Products' own table.
            await db.Database.MigrateAsync();
        }

        var auditOptions = new DbContextOptionsBuilder<AuditDbContext>();
        AuditDbContextOptions.Configure(auditOptions, superuserConnectionString);
        await using (var db = new AuditDbContext(auditOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity + AddAppendOnlyEnforcement for Audit's
            // own tables. All three modules' migrations share one physical database (and,
            // absent any per-context override, one __EFMigrationsHistory table) — safe because
            // every migration id across the three modules is a distinct timestamp; this is the
            // same assumption appsettings.Development.json's shared `contigo_dev` connection
            // string already relies on for DocumentsContracts+Audit today.
            await db.Database.MigrateAsync();

            // One unprivileged app role, granted after all three modules' tables exist, covers
            // every table regardless of which module's migration created it.
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

    // Explicit implementation: WebApplicationFactory<T> already exposes a public
    // ValueTask DisposeAsync() (System.IAsyncDisposable); xunit's own IAsyncLifetime.DisposeAsync
    // returns Task, so this must be explicit to disambiguate the two same-named methods. xunit
    // and the base class each separately drive their own interface, so both the container
    // (below) and the test server (WebApplicationFactory's own IDisposable/IAsyncDisposable,
    // untouched here) get torn down.
    Task IAsyncLifetime.DisposeAsync() => _postgres.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:IdentityWorkspace", _appConnectionString);
        builder.UseSetting("ConnectionStrings:DocumentsContracts", _appConnectionString);
        builder.UseSetting("ConnectionStrings:Audit", _appConnectionString);
        // Task E03/F03/US01/T02 (renewal-action) gave Contigo.Renewals its first DbContext, so
        // Program.cs now requires this key too (same fail-fast guard as the three above) — see
        // R1IntegrationFixture's own doc comment on this same line for why it points at this run's
        // own Testcontainers instance rather than appsettings.Development.json's static default.
        builder.UseSetting("ConnectionStrings:Renewals", _appConnectionString);
        // Task E04/F02/US02/T01 (savings-opportunity) gave Contigo.Savings its first DbContext, so
        // Program.cs now requires this key too (same fail-fast guard as the four above) — see
        // R1IntegrationFixture's own doc comment on this same line for why it points at this run's
        // own Testcontainers instance rather than appsettings.Development.json's static default.
        builder.UseSetting("ConnectionStrings:Savings", _appConnectionString);
        // Task E13/F06/US01/T01 (ask-engine): Program.cs now also requires ConnectionStrings:Chat
        // (already true since task E13/F05/US01/T02) and ConnectionStrings:Suppliers (new, this
        // task) — both point at this same migrated Testcontainer, not
        // appsettings.Development.json's static (never-dialled-here) default, since
        // AskCopilotService and ConversationService both genuinely query them for this fixture's
        // own tests.
        builder.UseSetting("ConnectionStrings:Chat", _appConnectionString);
        builder.UseSetting("ConnectionStrings:Suppliers", _appConnectionString);
        // Never actually dialled — IDocumentStorage is replaced with an in-memory fake below —
        // but Program.cs's own startup check requires a non-null configuration value to be
        // present (same syntactically-valid-value approach Contigo.Api.Tests already uses).
        builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IDocumentStorage>(DocumentStorage);

            // Test-only principal simulation (ADR-010 is deliberately not wired into
            // Program.cs by this task — see WorkspacePrincipalAuthorization's own doc comment:
            // "A real JWT bearer handler later (or a test today) both work the same way"). An
            // IStartupFilter wraps the pipeline the host itself builds, so production
            // composition in Program.cs stays exactly as ADR-010-deferred as it already
            // documents itself to be — this is test-host-only wiring.
            services.AddSingleton<IStartupFilter, TestPrincipalStartupFilter>();
        });
    }
}
