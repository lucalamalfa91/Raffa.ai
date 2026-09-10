using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Domain.Conversations;
using Raffa.Chat.Infrastructure;
using Raffa.Chat.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Chat.Tests.Conversations;

/// <summary>
/// Proves the Definition of Done for task E13/F05/US01/T01 (story us-01-conversations):
/// <see cref="ConversationService"/>'s create / list-recent / get / append-message behaviour, the
/// title-derivation rule (R-CONV-01: "first question, &lt;= 48 chars"), and the two audit rows
/// the task's own coding objective names (`conversation.created`, `conversation.message.appended`)
/// — against a real Postgres+RLS database, not an in-memory provider that would silently ignore
/// the RLS policy this task's own migration adds (see `ChatMigrationScriptTests` for the RLS
/// policy proof itself).
///
/// Runs every assertion through a dedicated, deliberately unprivileged Postgres role
/// (<see cref="AppRoleName"/>: `NOSUPERUSER NOBYPASSRLS`, not the table owner), mirroring
/// <c>Raffa.Documents.Contracts.Tests.DocumentUploadServiceTests</c>/<c>DocumentQueryServiceTests</c>:
/// the Testcontainers bootstrap role is always a Postgres superuser, and superusers
/// unconditionally bypass row security, so asserting through that connection would exercise this
/// module's schema without the RLS backstop actually being live. AC-1's own cross-tenant proof
/// (another tenant denied even with a guessed id) is task T02's own integration test — this class
/// proves the RLS-active connection still behaves correctly for the same-tenant path T01 owns, and
/// that the *application-level* tenant+user filter (see <see cref="ConversationService"/>'s own
/// doc comment) independently denies another workspace member's conversation — RLS has no
/// per-user predicate, so that half is proven here, not deferred to T02.
/// </summary>
public sealed class ConversationServiceTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_chat_app";
    private const string AppRolePassword = "raffa_chat_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<ChatDbContext>();
        ChatDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using (var adminDb = new ChatDbContext(adminOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity.
            await adminDb.Database.MigrateAsync();

            await adminDb.Database.ExecuteSqlRawAsync(
                $"""
                CREATE ROLE {AppRoleName} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS;
                GRANT USAGE ON SCHEMA public TO {AppRoleName};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRoleName};
                """);
        }

        _appConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = AppRoleName,
            Password = AppRolePassword,
        }.ConnectionString;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private ConversationService CreateService(
        ITenantContext tenantContext, IClock clock, IAuditWriter auditWriter, out ChatDbContext db)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ChatDbContext>();
        ChatDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        db = new ChatDbContext(optionsBuilder.Options);
        return new ConversationService(db, tenantContext, clock, auditWriter);
    }

    private static AppendConversationMessageRequest YouMessage(string markdown) =>
        new(ConversationRole.You, ConversationMessageKind.Answer, markdown, "[]", "[]");

    private static AppendConversationMessageRequest RaffaAnswer(
        string markdown, string citationsJson = "[{\"n\":1}]", string modelId = "fixture-answer-v1") =>
        new(ConversationRole.Raffa, ConversationMessageKind.Answer, markdown, citationsJson, "[]", modelId, "answer-v1", "hash-abc");

    [Fact]
    public async Task CreateAsync_persists_a_conversation_with_the_default_title_and_writes_an_audit_row()
    {
        var tenantId = TenantId.New();
        var scopeContractId = EntityId.New();
        var now = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
        var auditWriter = new RecordingAuditWriter();
        var service = CreateService(new TenantContext(), new FixedClock(now), auditWriter, out var db);
        await using var _ = db;

        var created = await service.CreateAsync(tenantId, "alice@example.com", scopeContractId);

        Assert.Equal(ConversationService.DefaultTitle, created.Title);
        Assert.Equal(scopeContractId, created.ScopeContractId);
        Assert.Equal(now, created.UpdatedAt);

        var auditEntry = Assert.Single(auditWriter.Written);
        Assert.Equal(tenantId, auditEntry.TenantId);
        Assert.Equal("alice@example.com", auditEntry.Actor);
        Assert.Equal("conversation.created", auditEntry.Action);
        Assert.Equal("conversation", auditEntry.ResourceType);
        Assert.Equal(created.ConversationId.Value.ToString(), auditEntry.ResourceId);
    }

    [Fact]
    public async Task ListRecentAsync_returns_only_the_callers_own_conversations_most_recently_updated_first()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        var t0 = new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

        // Three conversations for "alice": created in order, so UpdatedAt orders oldest -> newest.
        for (var i = 0; i < 3; i++)
        {
            var service = CreateService(tenantContext, new FixedClock(t0.AddMinutes(i)), auditWriter, out var db);
            await using var _ = db;
            await service.CreateAsync(tenantId, "alice@example.com", null);
        }

        // A conversation for a different user in the SAME tenant -- must never appear in alice's list.
        {
            var service = CreateService(tenantContext, new FixedClock(t0), auditWriter, out var db);
            await using var _ = db;
            await service.CreateAsync(tenantId, "bob@example.com", null);
        }

        var queryService = CreateService(tenantContext, new FixedClock(t0), auditWriter, out var queryDb);
        await using var __ = queryDb;

        var recent = await queryService.ListRecentAsync(tenantId, "alice@example.com", limit: 2);

        Assert.Equal(2, recent.Count);
        // Most recently updated first.
        Assert.True(recent[0].UpdatedAt > recent[1].UpdatedAt);
        Assert.All(recent, r => Assert.Equal(ConversationService.DefaultTitle, r.Title));
    }

    [Fact]
    public async Task GetAsync_returns_the_conversation_with_its_messages_in_creation_order()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        var t0 = new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

        ConversationSummaryResult created;
        {
            var service = CreateService(tenantContext, new FixedClock(t0), auditWriter, out var db);
            await using var _ = db;
            created = await service.CreateAsync(tenantId, "alice@example.com", null);
        }
        {
            var service = CreateService(tenantContext, new FixedClock(t0.AddSeconds(1)), auditWriter, out var db);
            await using var _ = db;
            await service.AppendMessageAsync(
                tenantId, "alice@example.com", created.ConversationId, YouMessage("When does Salesforce end?"));
        }
        {
            var service = CreateService(tenantContext, new FixedClock(t0.AddSeconds(2)), auditWriter, out var db);
            await using var _ = db;
            await service.AppendMessageAsync(
                tenantId, "alice@example.com", created.ConversationId,
                RaffaAnswer("Salesforce ends on **15 January 2027** [1]"));
        }

        var readService = CreateService(tenantContext, new FixedClock(t0), auditWriter, out var readDb);
        await using var ___ = readDb;
        var detail = await readService.GetAsync(tenantId, "alice@example.com", created.ConversationId);

        Assert.NotNull(detail);
        Assert.Equal(2, detail!.Messages.Count);
        Assert.Equal(ConversationRole.You, detail.Messages[0].Role);
        Assert.Equal(ConversationRole.Raffa, detail.Messages[1].Role);
        Assert.Equal("fixture-answer-v1", detail.Messages[1].ModelId);
        // R-CONV-01: title derived from the first (You) question.
        Assert.Equal("When does Salesforce end?", detail.Title);
    }

    [Fact]
    public async Task GetAsync_returns_null_when_the_conversation_belongs_to_a_different_user_in_the_same_tenant()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        var now = new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

        ConversationSummaryResult created;
        {
            var service = CreateService(tenantContext, new FixedClock(now), auditWriter, out var db);
            await using var _ = db;
            created = await service.CreateAsync(tenantId, "alice@example.com", null);
        }

        var readService = CreateService(tenantContext, new FixedClock(now), auditWriter, out var readDb);
        await using var __ = readDb;

        // Same tenant, genuinely existing row -- but a different user (R-CONV-01 AC-1).
        var detail = await readService.GetAsync(tenantId, "bob@example.com", created.ConversationId);

        Assert.Null(detail);
    }

    [Fact]
    public async Task AppendMessageAsync_derives_the_title_from_the_first_you_message_truncated_to_48_chars()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        var now = new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

        ConversationSummaryResult created;
        {
            var service = CreateService(tenantContext, new FixedClock(now), auditWriter, out var db);
            await using var _ = db;
            created = await service.CreateAsync(tenantId, "alice@example.com", null);
        }

        var longQuestion =
            "Does our Salesforce Sales Cloud Enterprise agreement include an uplift cap on renewal, and if so what is it?";
        Assert.True(longQuestion.Length > ConversationService.TitleMaxLength);

        ConversationMessageResult? appended;
        {
            var service = CreateService(tenantContext, new FixedClock(now.AddSeconds(1)), auditWriter, out var db);
            await using var _ = db;
            appended = await service.AppendMessageAsync(
                tenantId, "alice@example.com", created.ConversationId, YouMessage(longQuestion));
        }

        Assert.NotNull(appended);

        var readService = CreateService(tenantContext, new FixedClock(now), auditWriter, out var readDb);
        await using var __ = readDb;
        var detail = await readService.GetAsync(tenantId, "alice@example.com", created.ConversationId);

        Assert.NotNull(detail);
        Assert.True(detail!.Title.Length <= ConversationService.TitleMaxLength);
        Assert.Equal(longQuestion[..ConversationService.TitleMaxLength], detail.Title);
    }

    [Fact]
    public async Task AppendMessageAsync_does_not_change_the_title_once_a_first_question_already_set_it()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        var now = new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);

        ConversationSummaryResult created;
        {
            var service = CreateService(tenantContext, new FixedClock(now), auditWriter, out var db);
            await using var _ = db;
            created = await service.CreateAsync(tenantId, "alice@example.com", null);
        }
        {
            var service = CreateService(tenantContext, new FixedClock(now.AddSeconds(1)), auditWriter, out var db);
            await using var _ = db;
            await service.AppendMessageAsync(
                tenantId, "alice@example.com", created.ConversationId, YouMessage("When does Salesforce end?"));
        }
        {
            var service = CreateService(tenantContext, new FixedClock(now.AddSeconds(2)), auditWriter, out var db);
            await using var _ = db;
            await service.AppendMessageAsync(
                tenantId, "alice@example.com", created.ConversationId,
                YouMessage("What about the notice period?"));
        }

        var readService = CreateService(tenantContext, new FixedClock(now), auditWriter, out var readDb);
        await using var __ = readDb;
        var detail = await readService.GetAsync(tenantId, "alice@example.com", created.ConversationId);

        Assert.NotNull(detail);
        Assert.Equal("When does Salesforce end?", detail!.Title);
        Assert.Equal(2, detail.Messages.Count);
    }

    [Fact]
    public async Task AppendMessageAsync_bumps_updated_at_and_writes_a_message_appended_audit_row()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        var createdAt = new DateTimeOffset(2026, 9, 8, 9, 0, 0, TimeSpan.Zero);
        var appendedAt = createdAt.AddMinutes(5);

        ConversationSummaryResult created;
        {
            var service = CreateService(tenantContext, new FixedClock(createdAt), auditWriter, out var db);
            await using var _ = db;
            created = await service.CreateAsync(tenantId, "alice@example.com", null);
        }

        ConversationMessageResult? appended;
        {
            var service = CreateService(tenantContext, new FixedClock(appendedAt), auditWriter, out var db);
            await using var _ = db;
            appended = await service.AppendMessageAsync(
                tenantId, "alice@example.com", created.ConversationId, YouMessage("When does Salesforce end?"));
        }

        Assert.NotNull(appended);
        Assert.Equal(appendedAt, appended!.CreatedAt);

        var readService = CreateService(tenantContext, new FixedClock(createdAt), auditWriter, out var readDb);
        await using var __ = readDb;
        var detail = await readService.GetAsync(tenantId, "alice@example.com", created.ConversationId);

        Assert.NotNull(detail);
        Assert.Equal(appendedAt, detail!.UpdatedAt);

        var appendEntry = Assert.Single(auditWriter.Written, e => e.Action == "conversation.message.appended");
        Assert.Equal("alice@example.com", appendEntry.Actor);
        Assert.Equal("conversation_message", appendEntry.ResourceType);
        Assert.Equal(appended.MessageId.Value.ToString(), appendEntry.ResourceId);
        Assert.DoesNotContain("Salesforce", appendEntry.Detail); // ADR-011: never raw content in audit rows.
    }

    [Fact]
    public async Task AppendMessageAsync_returns_null_for_a_conversation_that_does_not_exist()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        var auditWriter = new RecordingAuditWriter();
        var service = CreateService(tenantContext, new FixedClock(DateTimeOffset.UtcNow), auditWriter, out var db);
        await using var _ = db;

        var appended = await service.AppendMessageAsync(
            tenantId, "alice@example.com", EntityId.New(), YouMessage("Hello?"));

        Assert.Null(appended);
        Assert.Empty(auditWriter.Written);
    }
}
