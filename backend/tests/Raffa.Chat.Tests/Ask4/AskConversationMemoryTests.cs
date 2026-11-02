using Raffa.Chat.Application.Ask4;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Domain.Conversations;
using Raffa.Chat.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Chat.Tests.Ask4;

/// <summary>The Chiedi chat keeps its memory: a follow-up is answered with the turns so far and in
/// the contract the conversation is about, not from zero.</summary>
public sealed class AskConversationMemoryTests : IAsyncLifetime
{
    private ChatPostgres _postgres = null!;

    public async Task InitializeAsync() => _postgres = await ChatPostgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private sealed class RecordingAnswers(EntityId contractId) : IAskAnswerService
    {
        public List<(EntityId? ContractId, IReadOnlyList<(string Role, string Markdown)>? History)> Calls { get; } = [];

        public Task<AskAnswer> AnswerAsync(
            TenantId tenantId, string userId, string question, EntityId? contract, string lang,
            IReadOnlyList<(string Role, string Markdown)>? recentTurns = null, CancellationToken cancellationToken = default)
        {
            Calls.Add((contract, recentTurns));
            return Task.FromResult(new AskAnswer(false, "Risposta a: " + question, "Contratto.pdf", null, contract ?? contractId, "Acme"));
        }
    }

    [Fact]
    public async Task A_follow_up_carries_the_history_and_the_conversation_contract()
    {
        var tenantId = TenantId.New();
        var contractId = EntityId.New();
        var tenantContext = new TenantContext();
        await using var db = _postgres.CreateDbContext(tenantContext);
        var conversations = new ConversationService(db, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());
        var answers = new RecordingAnswers(contractId);
        var ask = new AskConversationService(conversations, answers);

        var first = await ask.AskAsync(tenantId, "alice", new(null, "Quando scade Acme?", contractId, false, "it"));
        var second = await ask.AskAsync(tenantId, "alice", new(first.Conversation!.ConversationId, "E la penale?", null, false, "it"));

        Assert.True(second.Found);
        Assert.Null(answers.Calls[0].History);
        var (scope, history) = answers.Calls[1];
        Assert.Equal(contractId, scope);
        Assert.NotNull(history);
        Assert.Equal(
            [("you", "Quando scade Acme?"), ("raffa", "Risposta a: Quando scade Acme?")],
            history!.ToList());
        Assert.Equal(4, second.Conversation!.Messages.Count);
    }
}
