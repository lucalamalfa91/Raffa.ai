using System.Collections.Concurrent;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Application.Feedback;
using Raffa.Chat.Application.Gaps;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain.Conversations;
using Raffa.Chat.Infrastructure;
using Raffa.Chat.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Chat.Tests.Feedback;

/// <summary>
/// F4-D02: one offer, one report. The unique index on <c>(tenant, message)</c> is the guard; the
/// service turns the loser of a race into <see cref="FeedbackSubmitStatus.AlreadySubmitted"/> (the
/// endpoint's 409) instead of a 500, and publishes exactly one issue.
/// </summary>
public sealed class FeedbackServiceTests
{
    [Fact]
    public void A_Postgres_unique_violation_is_recognised_however_deep_it_is_wrapped()
    {
        var unique = new PostgresException("duplicate key", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation);
        var other = new PostgresException("deadlock", "ERROR", "ERROR", PostgresErrorCodes.DeadlockDetected);

        Assert.True(FeedbackService.IsUniqueViolation(new DbUpdateException("save failed", unique)));
        Assert.True(FeedbackService.IsUniqueViolation(new DbUpdateException("outer", new InvalidOperationException("middle", unique))));
        Assert.False(FeedbackService.IsUniqueViolation(new DbUpdateException("save failed", other)));
        Assert.False(FeedbackService.IsUniqueViolation(new DbUpdateException("save failed")));
    }

    [Fact]
    public async Task The_null_name_source_knows_no_supplier()
    {
        Assert.Empty(await new NullFeedbackNameSource().GetSupplierNamesAsync(TenantId.New()));
    }
}

/// <summary>The race itself, against a real Postgres (needs Docker: Testcontainers).</summary>
public sealed class FeedbackServiceRaceTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_chat_app";
    private const string AppRolePassword = "raffa_chat_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<ChatDbContext>();
        ChatDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using var adminDb = new ChatDbContext(adminOptions.Options);
        await adminDb.Database.MigrateAsync();
        await adminDb.Database.ExecuteSqlRawAsync(
            $"""
            CREATE ROLE {AppRoleName} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS;
            GRANT USAGE ON SCHEMA public TO {AppRoleName};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRoleName};
            """);

        _appConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = AppRoleName,
            Password = AppRolePassword,
        }.ConnectionString;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private (FeedbackService Service, ConversationService Conversations, ChatDbContext Db) Create(IFeatureRequestPublisher publisher)
    {
        var tenantContext = new TenantContext();
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero));
        var audit = new RecordingAuditWriter();

        var options = new DbContextOptionsBuilder<ChatDbContext>();
        ChatDbContextOptions.Configure(options, _appConnectionString, tenantContext);
        var db = new ChatDbContext(options.Options);

        var conversations = new ConversationService(db, tenantContext, clock, audit);
        var service = new FeedbackService(
            db, conversations, publisher, new FeedbackOptions { Environment = "test" }, tenantContext, clock, audit, new NullFeedbackNameSource());
        return (service, conversations, db);
    }

    [Fact]
    public async Task Concurrent_submissions_of_one_offer_store_one_row_publish_one_issue_and_answer_409_to_the_rest()
    {
        var tenantId = TenantId.New();
        const string userId = "alice@example.com";
        var publisher = new CountingPublisher();

        // A Raffa turn that carries the feedback offer.
        var gap = CapabilityGapCatalog.Find(CapabilityGapCatalog.ExportFileKey)!;
        EntityId conversationId;
        EntityId messageId;
        var (_, seedConversations, seedDb) = Create(publisher);
        await using (seedDb)
        {
            conversationId = (await seedConversations.CreateAsync(tenantId, userId, null)).ConversationId;
            var payload = new ReplyPayload(
                Gap: new GapInfo(gap.Key, gap.TitleEn, "en"),
                FeedbackOffer: CapabilityGapCopy.FeedbackOfferFor(gap, "en"));
            var appended = await seedConversations.AppendMessageAsync(
                tenantId, userId, conversationId,
                new AppendConversationMessageRequest(
                    ConversationRole.Raffa, ConversationMessageKind.Answer, "I cannot export yet", "[]", "[]",
                    PayloadJson: ReplyPayloadJson.Serialize(payload)));
            messageId = appended!.MessageId;
        }

        var answers = new FeedbackAnswers("Export every quarter", FeedbackQuestions.FrequencySometimes, FeedbackQuestions.ImportanceVeryUseful);
        var results = new ConcurrentBag<FeedbackSubmitResult>();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            var (service, _, db) = Create(publisher);
            await using (db)
            {
                results.Add(await service.SubmitAsync(tenantId, userId, conversationId, messageId, answers));
            }
        }));

        Assert.Equal(1, results.Count(r => r.Status == FeedbackSubmitStatus.Success));
        Assert.Equal(7, results.Count(r => r.Status == FeedbackSubmitStatus.AlreadySubmitted));
        Assert.All(results.Where(r => r.Status == FeedbackSubmitStatus.AlreadySubmitted), r => Assert.NotNull(r.Request));
        Assert.Equal(1, publisher.Published);

        // Counted through the bootstrap (superuser) connection: row security would hide the rows
        // from a connection that has no tenant scope.
        var adminOptions = new DbContextOptionsBuilder<ChatDbContext>();
        ChatDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());
        await using var adminDb = new ChatDbContext(adminOptions.Options);
        Assert.Equal(1, await adminDb.FeatureRequests.CountAsync(r => r.MessageId == messageId));
    }

    private sealed class CountingPublisher : IFeatureRequestPublisher
    {
        private int _published;

        public int Published => Volatile.Read(ref _published);

        public bool IsConfigured => true;

        public Task<FeatureRequestPublishResult> TryPublishAsync(FeatureRequestIssue issue, CancellationToken cancellationToken = default)
        {
            var number = Interlocked.Increment(ref _published);
            return Task.FromResult(FeatureRequestPublishResult.Opened(number, $"https://example.test/issues/{number}"));
        }
    }
}
