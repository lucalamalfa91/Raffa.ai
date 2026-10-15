using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Application.Feedback;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain.Conversations;
using Raffa.Chat.Domain.Feedback;
using Raffa.Chat.Infrastructure;
using Raffa.Chat.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Testcontainers.PostgreSql;

namespace Raffa.Chat.Tests.Feedback;

/// <summary>F4-T04: the GitHub outbox is idempotent per (tenant, gapKey) — many offers about the
/// same gap, even submitted at once, open one issue (needs Docker: Testcontainers).</summary>
public sealed class FeedbackServiceDedupTests : IAsyncLifetime
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

    private async Task<(EntityId ConversationId, List<EntityId> MessageIds)> SeedOffersAsync(
        TenantId tenantId, string userId, string gapKey, int count)
    {
        var (_, conversations, db) = Create(new CountingPublisher(configured: true));
        await using (db)
        {
            var conversationId = (await conversations.CreateAsync(tenantId, userId, null)).ConversationId;
            var messages = new List<EntityId>();
            for (var i = 0; i < count; i++)
            {
                var payload = new ReplyPayload(
                    Gap: new GapInfo(gapKey, "Export contracts to a file", "en"),
                    FeedbackOffer: new FeedbackOffer(
                        "Report?", "Yes", "No", "Next", "Back", "Send", "Sending", "Thanks", "Failed", "Public",
                        [new FeedbackQuestion(FeedbackQuestions.WhatKey, FeedbackQuestions.TextKind, "What?", "prefill", null)]));
                var appended = await conversations.AppendMessageAsync(
                    tenantId, userId, conversationId,
                    new AppendConversationMessageRequest(
                        ConversationRole.Raffa, ConversationMessageKind.Answer, "I cannot export yet", "[]", "[]",
                        PayloadJson: ReplyPayloadJson.Serialize(payload)));
                messages.Add(appended!.MessageId);
            }

            return (conversationId, messages);
        }
    }

    private static FeedbackAnswers Answers => new("Export every quarter", FeedbackQuestions.FrequencySometimes, FeedbackQuestions.ImportanceVeryUseful);

    [Fact]
    public async Task Concurrent_offers_about_the_same_gap_open_one_issue_and_link_the_rest_to_it()
    {
        var tenantId = TenantId.New();
        const string userId = "alice@example.com";
        var publisher = new CountingPublisher(configured: true);
        var (conversationId, messageIds) = await SeedOffersAsync(tenantId, userId, "export-file", 6);
        var results = new ConcurrentBag<FeedbackSubmitResult>();

        await Task.WhenAll(messageIds.Select(async messageId =>
        {
            var (service, _, db) = Create(publisher);
            await using (db)
            {
                results.Add(await service.SubmitAsync(tenantId, userId, conversationId, messageId, Answers));
            }
        }));

        Assert.Equal(1, publisher.Published);
        Assert.All(results, r =>
        {
            Assert.Equal(FeedbackSubmitStatus.Success, r.Status);
            Assert.Equal(FeatureRequestStatus.IssueOpened, r.Request!.Status);
            Assert.Equal(1, r.Request.IssueNumber);
        });
    }

    [Fact]
    public async Task A_different_gap_still_opens_its_own_issue()
    {
        var tenantId = TenantId.New();
        const string userId = "alice@example.com";
        var publisher = new CountingPublisher(configured: true);
        var (conversationId, first) = await SeedOffersAsync(tenantId, userId, "export-file", 1);
        var (conversationId2, second) = await SeedOffersAsync(tenantId, userId, "reminder", 1);

        var (service, _, db) = Create(publisher);
        await using (db)
        {
            await service.SubmitAsync(tenantId, userId, conversationId, first[0], Answers);
            await service.SubmitAsync(tenantId, userId, conversationId2, second[0], Answers);
        }

        Assert.Equal(2, publisher.Published);
    }

    [Fact]
    public async Task A_failed_publish_does_not_block_a_later_one_and_an_unconfigured_publisher_is_untouched()
    {
        var tenantId = TenantId.New();
        const string userId = "alice@example.com";
        var (conversationId, ids) = await SeedOffersAsync(tenantId, userId, "export-file", 2);

        var failing = new CountingPublisher(configured: true, failFirst: true);
        var (service, _, db) = Create(failing);
        await using (db)
        {
            var first = await service.SubmitAsync(tenantId, userId, conversationId, ids[0], Answers);
            Assert.Equal(FeatureRequestStatus.Recorded, first.Request!.Status);
            var second = await service.SubmitAsync(tenantId, userId, conversationId, ids[1], Answers);
            Assert.Equal(FeatureRequestStatus.IssueOpened, second.Request!.Status);
        }

        var tenant2 = TenantId.New();
        var (conversation2, ids2) = await SeedOffersAsync(tenant2, userId, "export-file", 2);
        var off = new CountingPublisher(configured: false);
        var (service2, _, db2) = Create(off);
        await using (db2)
        {
            Assert.Equal(FeatureRequestStatus.Recorded, (await service2.SubmitAsync(tenant2, userId, conversation2, ids2[0], Answers)).Request!.Status);
            Assert.Equal(FeatureRequestStatus.Recorded, (await service2.SubmitAsync(tenant2, userId, conversation2, ids2[1], Answers)).Request!.Status);
        }

        Assert.Equal(0, off.Published);
    }

    private sealed class CountingPublisher(bool configured, bool failFirst = false) : IFeatureRequestPublisher
    {
        private int _published;
        private int _calls;

        public int Published => Volatile.Read(ref _published);

        public bool IsConfigured => configured;

        public Task<FeatureRequestPublishResult> TryPublishAsync(FeatureRequestIssue issue, CancellationToken cancellationToken = default)
        {
            if (failFirst && Interlocked.Increment(ref _calls) == 1)
            {
                return Task.FromResult(FeatureRequestPublishResult.Failed("boom"));
            }

            var number = Interlocked.Increment(ref _published);
            return Task.FromResult(FeatureRequestPublishResult.Opened(number, $"https://example.test/issues/{number}"));
        }
    }
}
