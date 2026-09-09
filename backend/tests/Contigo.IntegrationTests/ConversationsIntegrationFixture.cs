using Contigo.Audit.Infrastructure;
using Contigo.Chat.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Contigo.IntegrationTests;

/// <summary>
/// Dedicated fixture for task E13/F05/US01/T02 (story us-01-conversations): one real, migrated
/// Postgres (via Testcontainers) standing behind the real `Program` composition root, wired for
/// `Contigo.Chat`'s own tables (<c>Conversation</c>/<c>ConversationMessage</c>) **and**
/// `Contigo.Audit`'s (<c>audit_event</c>) — `ConversationService` writes a real
/// `conversation.created`/`conversation.message.appended` row through `IAuditWriter` on every
/// mutation (ADR-011), so a working, migrated Audit schema is load-bearing for this fixture even
/// though the endpoints under test never call `GET /api/audit` themselves. Every *other* required
/// `ConnectionStrings:*` key Program.cs's startup check demands (Identity/Workspace,
/// Documents/Contracts, Renewals, Savings, Quotes, Storage) falls back to
/// `appsettings.Development.json`'s own syntactically-valid (never actually dialed) default — the
/// same "registration is lazy, only a real query would fail" reasoning
/// <c>Contigo.Api.Tests.ChatEndpointTests</c>'s own doc comment already relies on; this fixture
/// does not migrate any of those the way <see cref="R0IntegrationFixture"/> does for the R0 path.
///
/// Runs every conversations request through a dedicated, deliberately unprivileged Postgres role
/// (mirrors <see cref="R0IntegrationFixture"/> and
/// <c>Contigo.Chat.Tests.Conversations.ConversationServiceTests</c>: the Testcontainers bootstrap
/// role is always a superuser, and superusers unconditionally bypass row security, so this task's
/// own cross-tenant RLS proof would otherwise be vacuous).
/// </summary>
public sealed class ConversationsIntegrationFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string AppRoleName = "contigo_chat_http_app";
    private const string AppRolePassword = "contigo_chat_http_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var superuserConnectionString = _postgres.GetConnectionString();

        var chatOptions = new DbContextOptionsBuilder<ChatDbContext>();
        ChatDbContextOptions.Configure(chatOptions, superuserConnectionString);
        await using (var db = new ChatDbContext(chatOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity for Contigo.Chat's own tables.
            await db.Database.MigrateAsync();
        }

        // ConversationService writes conversation.created/conversation.message.appended through
        // IAuditWriter on every mutation (ADR-011) — the audit_event table must exist on this
        // same Testcontainer, or every POST /api/conversations call 500s trying to audit-write
        // into a table that was never migrated (caught by this fixture's own first test run).
        var auditOptions = new DbContextOptionsBuilder<AuditDbContext>();
        AuditDbContextOptions.Configure(auditOptions, superuserConnectionString);
        await using (var db = new AuditDbContext(auditOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity + AddAppendOnlyEnforcement for
            // Contigo.Audit's own table.
            await db.Database.MigrateAsync();

            // Granted once, after both modules' tables exist, covers every table regardless of
            // which module's migration created it — same "one role, granted last" shape
            // R0IntegrationFixture already uses.
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
    // returns Task, so this must be explicit to disambiguate the two same-named methods — same
    // shape as R0IntegrationFixture's own explicit implementation.
    Task IAsyncLifetime.DisposeAsync() => _postgres.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Chat", _appConnectionString);
        builder.UseSetting("ConnectionStrings:Audit", _appConnectionString);
    }
}
