using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.WebResearch;
using Raffa.Chat.Domain.Conversations;
using Raffa.Chat.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Chat.Tests.Conversations;

/// <summary>
/// F3-T04 / F3-D19 — a web-research consent is single-use <em>atomically</em>: of any number of
/// concurrent taps on the same consent exactly one is <see cref="InterviewConsumeOutcome.Consumed"/>
/// (so exactly one research call is authorised), every other is
/// <see cref="InterviewConsumeOutcome.AlreadyConsumed"/>. Runs against a real Postgres with RLS
/// through an unprivileged role — the conditional UPDATE is Postgres-only. Needs Docker (or
/// <c>RAFFA_TEST_POSTGRES_ADMIN</c>, see <see cref="ChatPostgres"/>).
/// </summary>
public sealed class InterviewConsentAtomicityTests : IAsyncLifetime
{
    private const string User = "alice@example.com";
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private ChatPostgres _postgres = null!;

    public async Task InitializeAsync() => _postgres = await ChatPostgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private ConversationService NewService(ITenantContext tenantContext, out Raffa.Chat.Infrastructure.ChatDbContext db)
    {
        db = _postgres.CreateDbContext(tenantContext);
        return new ConversationService(db, tenantContext, new FixedClock(Now), new RecordingAuditWriter());
    }

    private async Task<(TenantId Tenant, EntityId Conversation, EntityId Message)> SeedConsentAsync(TenantContext tenantContext)
    {
        var tenant = TenantId.New();
        var service = NewService(tenantContext, out var db);
        await using var _ = db;

        var conversation = await service.CreateAsync(tenant, User, null);
        var turn = WebConsentInterview.Build(
            "search the web for typical uplift caps on saas renewals",
            new WebResearchRequest("typical uplift caps saas renewals", "MarketPractice"));
        var message = await service.AppendMessageAsync(
            tenant,
            User,
            conversation.ConversationId,
            new AppendConversationMessageRequest(
                ConversationRole.Raffa,
                ConversationMessageKind.Interview,
                turn.Prompt,
                "[]",
                "[]",
                InterviewJson: InterviewJsonCodec.SerializeTurn(turn)));

        return (tenant, conversation.ConversationId, message!.MessageId);
    }

    [Fact]
    public async Task Concurrent_taps_on_one_consent_consume_it_exactly_once()
    {
        const int taps = 24;
        var tenantContext = new TenantContext();
        var (tenant, conversation, message) = await SeedConsentAsync(tenantContext);

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, taps)
            .Select(i => Task.Run(async () =>
            {
                var service = NewService(tenantContext, out var db);
                await using var _ = db;
                await start.Task;
                return (Index: i, Outcome: await service.MarkInterviewConsumedAsync(tenant, User, conversation, message, $"allow-{i}"));
            }))
            .ToArray();

        start.SetResult();
        var results = await Task.WhenAll(attempts);

        var winner = Assert.Single(results, r => r.Outcome == InterviewConsumeOutcome.Consumed);
        Assert.Equal(taps - 1, results.Count(r => r.Outcome == InterviewConsumeOutcome.AlreadyConsumed));

        // The stored stamp is the winner's, and only one stamp was ever written.
        var readService = NewService(tenantContext, out var readDb);
        await using var __ = readDb;
        var stored = await readService.GetMessageAsync(tenant, User, conversation, message);
        var record = InterviewJsonCodec.TryDecodeTurn(stored!.InterviewJson);
        Assert.NotNull(record);
        Assert.Equal(Now, record.ConsumedAt);
        Assert.Equal($"allow-{winner.Index}", record.ConsumedOptionKey);
    }

    [Fact]
    public async Task A_replay_after_the_first_tap_is_already_consumed_and_keeps_the_first_stamp()
    {
        var tenantContext = new TenantContext();
        var (tenant, conversation, message) = await SeedConsentAsync(tenantContext);

        var service = NewService(tenantContext, out var db);
        await using var _ = db;
        Assert.Equal(InterviewConsumeOutcome.Consumed, await service.MarkInterviewConsumedAsync(tenant, User, conversation, message, "allow"));

        var again = NewService(tenantContext, out var db2);
        await using var __ = db2;
        Assert.Equal(InterviewConsumeOutcome.AlreadyConsumed, await again.MarkInterviewConsumedAsync(tenant, User, conversation, message, "decline"));

        var stored = await again.GetMessageAsync(tenant, User, conversation, message);
        Assert.Equal("allow", InterviewJsonCodec.TryDecodeTurn(stored!.InterviewJson)!.ConsumedOptionKey);
    }

    [Fact]
    public async Task Another_tenant_or_another_user_cannot_consume_the_consent()
    {
        var tenantContext = new TenantContext();
        var (tenant, conversation, message) = await SeedConsentAsync(tenantContext);

        var service = NewService(tenantContext, out var db);
        await using var _ = db;

        Assert.Equal(InterviewConsumeOutcome.NotFound, await service.MarkInterviewConsumedAsync(TenantId.New(), User, conversation, message, "allow"));
        Assert.Equal(InterviewConsumeOutcome.NotFound, await service.MarkInterviewConsumedAsync(tenant, "mallory@example.com", conversation, message, "allow"));
        Assert.Equal(InterviewConsumeOutcome.NotFound, await service.MarkInterviewConsumedAsync(tenant, User, conversation, EntityId.New(), "allow"));

        // None of those touched the consent: the owner can still take it, once.
        Assert.Equal(InterviewConsumeOutcome.Consumed, await service.MarkInterviewConsumedAsync(tenant, User, conversation, message, "allow"));
    }
}
