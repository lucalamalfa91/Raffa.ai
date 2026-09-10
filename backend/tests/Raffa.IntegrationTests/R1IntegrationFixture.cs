using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Audit.Infrastructure;
using Raffa.Chat.Infrastructure;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;
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
/// Shared fixture for task E02/F06/US01/T01 (r1-integration) — proves the parent story
/// us-01-final-integration's R1 definition of success ("upload -&gt; parse/OCR -&gt; classify -&gt;
/// extract -&gt; portfolio -&gt; 360 -&gt; Ask Raffa (with citations) -&gt; correction") against a
/// real, migrated Postgres+pgvector+RLS database and the real <c>Raffa.Api</c> composition root,
/// the same "one real host, no hand-rolled container" shape <see cref="R0IntegrationFixture"/>
/// already established for R0. A distinct type rather than a reuse/refactor of
/// <see cref="R0IntegrationFixture"/> (which this task's own scope leaves untouched — it is a
/// different, already-merged task's artifact and other tests still depend on its exact shape):
/// this fixture additionally swaps in <see cref="ScriptedR1AiGateway"/> so
/// <c>Raffa.Documents.Contracts.Application.Extraction.StagedExtractionService</c> has real,
/// schema-shaped facts to extract end-to-end — <see cref="R0IntegrationFixture"/> only ever needed
/// the real <c>FixtureAiGateway</c>'s always-empty <c>ExtractAsync</c>, since no R0 task exercised
/// staged extraction over HTTP.
///
/// Runs every test request through a dedicated, deliberately unprivileged Postgres role (mirrors
/// <see cref="R0IntegrationFixture"/> and every per-module RLS proof in this solution): the
/// Testcontainers bootstrap role is always a superuser, and superusers unconditionally bypass row
/// security, so AC-3 ("cross-tenant isolation holds across the whole path") would otherwise pass
/// vacuously.
/// </summary>
public sealed class R1IntegrationFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string AppRoleName = "raffa_r1_app";
    private const string AppRolePassword = "raffa_r1_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    private string _appConnectionString = string.Empty;

    /// <summary>Fake <see cref="IDocumentStorage"/> the test host is wired with — see
    /// <see cref="R0IntegrationFixture.DocumentStorage"/>'s own doc comment for why (ADR-005/
    /// ADR-011: domain code only ever sees the interface). Reused as-is from
    /// <c>Raffa.IntegrationTests</c>'s own sibling type rather than duplicated.</summary>
    public RecordingDocumentStorage DocumentStorage { get; } = new();

    /// <summary>The scripted gateway this fixture's host resolves <see cref="IAiGateway"/> as —
    /// see <see cref="ScriptedR1AiGateway"/>'s own doc comment. Exposed so a test can assert on
    /// <see cref="ScriptedR1AiGateway.OcrCallCount"/> (AC-4) without re-resolving it from the host's
    /// service provider.</summary>
    public ScriptedR1AiGateway AiGateway { get; } = new(
        new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()),
        R1ExtractionFixtures.PayloadsByStage);

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

        // Task E13/F06/US01/T01 (ask-engine): AskCopilotService (behind both POST /api/chat/query
        // and POST /api/conversations/{id}/messages) resolves supplier names via
        // ISupplierNameLookup for every turn whose portfolio has at least one contract with a
        // SupplierId, and every turn persists through ConversationService — so this fixture must
        // also migrate Raffa.Chat's and Raffa.Suppliers.Products' own tables and route their
        // connection strings at this same Testcontainer (see ConfigureWebHost below). Mirrors the
        // identical fix already applied to R0IntegrationFixture.InitializeAsync for the same
        // reason (see that method's own remarks) — R1's own R1EndToEndTests/R1CrossTenantIsolationTests
        // reach `/api/chat/query` too, and without this block ChatEndpointExtensions.PostChatQueryAsync's
        // unconditional conversationService.CreateAsync(...) call resolves ChatDbContext against
        // appsettings.Development.json's static placeholder connection string instead of this run's
        // own Testcontainer, which previously failed both tests with a 500.
        var chatOptions = new DbContextOptionsBuilder<ChatDbContext>();
        ChatDbContextOptions.Configure(chatOptions, superuserConnectionString);
        await using (var db = new ChatDbContext(chatOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity for Raffa.Chat's own tables
            // (Conversation, ConversationMessage).
            await db.Database.MigrateAsync();
        }

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

            // One unprivileged app role, granted after every module's tables exist — same shape
            // as R0IntegrationFixture.InitializeAsync (see that method's own remarks).
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
        // Task E03/F03/US01/T02 (renewal-action) gave Raffa.Renewals its first DbContext, so
        // Program.cs now requires this key too (same fail-fast guard as the three above). No R1
        // scenario exercises the Renewals module yet, but pointing it at this run's own
        // Testcontainers instance — rather than leaving it to fall back to
        // appsettings.Development.json's static local-dev string — is what every other
        // DbContext-backed connection string on this line already does; a future renewal
        // integration test should not have to discover this fixture never wired it.
        builder.UseSetting("ConnectionStrings:Renewals", _appConnectionString);
        // Task E04/F02/US02/T01 (savings-opportunity) gave Raffa.Savings its first DbContext, so
        // Program.cs now requires this key too (same fail-fast guard as the four above). No R1
        // scenario exercises the Savings module yet — same "point at this run's own Testcontainers
        // instance rather than the static appsettings.Development.json default" rationale as the
        // Renewals line just above.
        builder.UseSetting("ConnectionStrings:Savings", _appConnectionString);
        // Task E13/F06/US01/T01 (ask-engine): Program.cs now also requires ConnectionStrings:Chat
        // (already true since task E13/F05/US01/T02) and ConnectionStrings:Suppliers (new, this
        // task) — both point at this same migrated Testcontainer, not
        // appsettings.Development.json's static (never-dialled-here) default, since
        // AskCopilotService and ConversationService both genuinely query them once an R1 test
        // reaches `/api/chat/query` (R1EndToEndTests, R1CrossTenantIsolationTests) — same rationale
        // as R0IntegrationFixture.ConfigureWebHost's own identical two lines.
        builder.UseSetting("ConnectionStrings:Chat", _appConnectionString);
        builder.UseSetting("ConnectionStrings:Suppliers", _appConnectionString);
        // Never actually dialled — IDocumentStorage is replaced with an in-memory fake below —
        // but Program.cs's own startup check requires a non-null configuration value (same
        // syntactically-valid-value approach R0IntegrationFixture/Raffa.Api.Tests already use).
        builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IDocumentStorage>(DocumentStorage);

            // Overrides AddAiGatewayModule's own TryAddSingleton<IAiGateway, FixtureAiGateway>()
            // (Program.cs runs first, building up the service collection; this callback appends a
            // second IAiGateway registration afterward, and ASP.NET Core's container resolves the
            // last one registered for a single GetRequiredService<T> call) — the same "append an
            // AddSingleton override after the host's own registration" shape
            // R0IntegrationFixture already uses for IDocumentStorage above.
            services.AddSingleton<IAiGateway>(AiGateway);
        });
    }
}
