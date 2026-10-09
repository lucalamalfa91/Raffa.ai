using Raffa.AiFlows.CapabilityGaps.Investigation;
using System.Collections.Concurrent;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain.Conversations;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Api;

/// <summary>
/// ADR-031: the second half of the capability check — putting its follow-up in the conversation.
/// <see cref="CapabilityCheckRunner"/> runs the check beside the answer; once it has a follow-up the
/// endpoint appends it as a separate Raffa message right after the answer: in the same response
/// when the check finished first (<see cref="AppendAsync"/>), otherwise from the background
/// (<see cref="AppendWhenDone"/>) while the client polls. This class owns the background tracking
/// (<see cref="WhenIdleAsync"/>) and talks to the conversation store, which is host code
/// (<see cref="ConversationsEndpointExtensions.ToAppendRequest"/>), so it stays in the host.
/// </summary>
internal sealed class CapabilityFollowUpAppender(
    IServiceScopeFactory scopeFactory,
    CapabilityCheckRunner runner,
    ILogger<CapabilityFollowUpAppender> logger)
{
    private readonly ConcurrentDictionary<Task, byte> _background = new();

    /// <summary>Appends <paramref name="followUp"/> after the answer <paramref name="answeredMessageId"/>
    /// — only while that answer is still the conversation's last message: when the user has
    /// already moved on, a late proposal would land in the middle of another exchange, so it is
    /// dropped (and logged). Writes one audit row with the gap key; never the question.</summary>
    public async Task<ConversationMessageResult?> AppendAsync(
        TenantId tenantId,
        string userId,
        EntityId conversationId,
        EntityId answeredMessageId,
        CopilotReply followUp,
        string? turnId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(followUp);

        await using var scope = scopeFactory.CreateAsyncScope();
        var conversations = scope.ServiceProvider.GetRequiredService<ConversationService>();

        var conversation = await conversations.GetAsync(tenantId, userId, conversationId, cancellationToken).ConfigureAwait(false);
        if (conversation is null || conversation.Messages.Count == 0 || conversation.Messages[^1].MessageId != answeredMessageId)
        {
            logger.LogInformation(
                "Capability follow-up for message {MessageId} dropped: the conversation has moved on", answeredMessageId);
            await runner.TryAuditOutcomeAsync(
                    scope.ServiceProvider, tenantId, userId, turnId, CapabilityCheckRunner.OutcomeDrop, followUp.Payload?.Gap?.Discovery?.Confidence, followUp.Payload?.Gap?.Key)
                .ConfigureAwait(false);
            return null;
        }

        var stamped = followUp with
        {
            Payload = (followUp.Payload ?? new ReplyPayload()) with
            {
                FollowUps = followUp.FollowUps.Count > 0 ? followUp.FollowUps : null,
                CapabilityCheckFor = answeredMessageId.Value.ToString(),
            },
        };

        var appended = await conversations
            .AppendMessageAsync(tenantId, userId, conversationId, ConversationsEndpointExtensions.ToAppendRequest(stamped), cancellationToken)
            .ConfigureAwait(false);

        if (appended is not null)
        {
            var auditWriter = scope.ServiceProvider.GetRequiredService<IAuditWriter>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            await auditWriter.WriteAsync(
                    new AuditEntry(
                        tenantId,
                        userId,
                        CapabilityCheckRunner.AuditAction,
                        CapabilityCheckRunner.AuditResourceType,
                        appended.MessageId.Value.ToString(),
                        clock.UtcNow,
                        $"conversationId={conversationId} answeredMessageId={answeredMessageId} " +
                        $"gapKey={stamped.Payload?.Gap?.Key ?? "none"} turnId={CapabilityCheckRunner.FormatTurnId(turnId)}"),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return appended;
    }

    /// <summary>The check is still running when the answer is persisted: append its follow-up from
    /// the background once it completes. Failures are logged, never surfaced.</summary>
    public void AppendWhenDone(
        Task<CopilotReply?> check,
        TenantId tenantId,
        string userId,
        EntityId conversationId,
        EntityId answeredMessageId,
        string? turnId = null)
    {
        ArgumentNullException.ThrowIfNull(check);

        var work = Task.Run(async () =>
        {
            try
            {
                var followUp = await check.ConfigureAwait(false);
                if (followUp is not null)
                {
                    await AppendAsync(tenantId, userId, conversationId, answeredMessageId, followUp, turnId).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Capability follow-up for message {MessageId} could not be appended", answeredMessageId);
            }
        });

        _background.TryAdd(work, 0);
        _ = work.ContinueWith(done => _background.TryRemove(done, out _), TaskScheduler.Default);
    }

    /// <summary>Completes when every background append started so far has finished — for tests
    /// and a graceful shutdown.</summary>
    public Task WhenIdleAsync() => Task.WhenAll(_background.Keys.ToList());
}
