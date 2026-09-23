using System.Collections.Concurrent;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Conversations;
using Raffa.Chat.Application.Gaps;
using Raffa.Chat.Application.Language;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain.Conversations;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Api;

/// <summary>
/// Everything a capability check needs from the turn that started it (ADR-031), captured by
/// <see cref="AskCopilotService.AskAsync"/> while the request's own data is at hand — the check
/// outlives the request, so it never reads the request's scope again.
/// </summary>
/// <param name="SupplierNames">Every supplier the tenant has on file: scrubbed from the feature
/// texts, and the chips of an email follow-up.</param>
/// <param name="ValidatedContractCount">The portfolio size the routing context needs (an empty
/// portfolio swaps a greyed screen for the Documents upload).</param>
/// <param name="NamedSupplier">The known supplier the turn named or was scoped to, if any.</param>
/// <param name="NamedContractId">That supplier's contract, when one resolved — Renewals'
/// <c>?select=</c> and Contract 360's link.</param>
internal sealed record CapabilityCheckRequest(
    TenantId TenantId,
    string Question,
    IReadOnlyList<string> SupplierNames,
    int ValidatedContractCount,
    string? NamedSupplier,
    Guid? NamedContractId);

/// <summary>The one-turn hand-off between <see cref="AskCopilotService.AskAsync"/>, which decides
/// whether a turn is checked and starts the check, and the endpoint, which persists the answer and
/// then the follow-up. Null <see cref="FollowUp"/>: no check was started.</summary>
internal sealed class CapabilityCheckSlot
{
    /// <summary>Completes with the follow-up to append after the answer, or null when the check
    /// found nothing to propose (or failed — the check is fail-open).</summary>
    public Task<CopilotReply?>? FollowUp { get; set; }
}

/// <summary>
/// ADR-031, without latency: the capability investigator runs <b>beside</b> the answer, never in
/// front of it. <see cref="Start"/> launches the check in its own DI scope (the request's scoped
/// <c>IAiGateway</c> writes audit rows through a scoped DbContext that is gone once the response
/// is sent) and returns at its first I/O, so the answer is computed while the model thinks. When
/// the check finds an operation Raffa cannot perform, the endpoint appends the follow-up as a
/// separate Raffa message right after the answer: in the same response when the check finished
/// first, otherwise from the background (<see cref="AppendWhenDone"/>) while the client polls.
/// </summary>
internal sealed class CapabilityCheckDispatcher(
    IServiceScopeFactory scopeFactory,
    GapInvestigationOptions options,
    ILogger<CapabilityCheckDispatcher> logger)
{
    public const string AuditAction = "ask.capability_follow_up";
    public const string AuditResourceType = "capability_follow_up";

    /// <summary>How many validated suppliers an email follow-up offers as chips.</summary>
    private const int MaxDraftSupplierFollowUps = 5;

    private readonly ConcurrentDictionary<Task, byte> _background = new();

    public bool Enabled => options.Enabled;

    /// <summary>Starts the check for one turn. Never throws, never cancels with the request.</summary>
    public Task<CopilotReply?> Start(CapabilityCheckRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RunAsync(request);
    }

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
                        AuditAction,
                        AuditResourceType,
                        appended.MessageId.Value.ToString(),
                        clock.UtcNow,
                        $"conversationId={conversationId} answeredMessageId={answeredMessageId} " +
                        $"gapKey={stamped.Payload?.Gap?.Key ?? "none"}"),
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return appended;
    }

    /// <summary>The check is still running when the answer is persisted: append its follow-up from
    /// the background once it completes. Failures are logged, never surfaced.</summary>
    public void AppendWhenDone(
        Task<CopilotReply?> check, TenantId tenantId, string userId, EntityId conversationId, EntityId answeredMessageId)
    {
        ArgumentNullException.ThrowIfNull(check);

        var work = Task.Run(async () =>
        {
            try
            {
                var followUp = await check.ConfigureAwait(false);
                if (followUp is not null)
                {
                    await AppendAsync(tenantId, userId, conversationId, answeredMessageId, followUp).ConfigureAwait(false);
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

    private async Task<CopilotReply?> RunAsync(CapabilityCheckRequest request)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var tenantScope = tenantContext.BeginScope(request.TenantId);

            var investigator = scope.ServiceProvider.GetRequiredService<CapabilityInvestigator>();
            var investigation = await investigator
                .InvestigateAsync(request.Question, request.SupplierNames, CancellationToken.None)
                .ConfigureAwait(false);

            logger.LogInformation(
                "Capability check finished: {Outcome}{Failure}",
                investigation.Outcome,
                investigation.Failure is null ? string.Empty : " (" + investigation.Failure + ")");

            return investigation.Gap is { } gap
                ? BuildFollowUp(gap, investigation.AlternativeQuestions, request, scope.ServiceProvider.GetRequiredService<CapabilityRouting>())
                : null;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Capability check failed; no follow-up");
            return null;
        }
    }

    /// <summary>
    /// The follow-up message: "I checked what Raffa.ai can do for your request." then the gap's
    /// honest preface and its alternative —
    /// a discovered gap: the nearest screen (none when it is Ask itself), the lead-in that says a
    /// person approves a proposal before it is built, the investigator's questions as chips;
    /// the email gaps: "which contract?" with one chip per supplier (the named one alone when the
    /// turn named one), each re-entering the drafted-email path;
    /// the other catalog gaps: the screen that already holds the answer.
    /// Every one carries the feedback offer.
    /// </summary>
    internal static CopilotReply BuildFollowUp(
        CapabilityGap gap, IReadOnlyList<string> alternativeQuestions, CapabilityCheckRequest request, CapabilityRouting routing)
    {
        var language = QuestionLanguage.Detect(request.Question);
        var opening = CapabilityGapCopy.CheckedOpening(language);
        var contractId = request.NamedContractId is { } id ? new EntityId(id) : (EntityId?)null;
        var context = new RoutingContext(request.ValidatedContractCount, CapabilityCallerRole.Standard, contractId);
        var portfolioIsEmpty = request.ValidatedContractCount == 0;

        switch (gap.Alternative)
        {
            case GapAlternative.NearestCapability:
            {
                var linkKey = gap.NearestCapabilityKey switch
                {
                    null or CapabilityCatalog.AskKey => null,
                    // Both patterns need an object id; the list they belong to does not.
                    CapabilityCatalog.ContractDetailKey when contractId is null => CapabilityCatalog.PortfolioKey,
                    CapabilityCatalog.DocumentsReviewKey => CapabilityCatalog.DocumentsAttentionKey,
                    var key => key,
                };

                IReadOnlyList<CopilotAction> actions = linkKey is null
                    ? []
                    : routing.ResolveActions([CapabilityIntent.HowTo(linkKey)], context);

                var markdown = opening + " " + CapabilityGapCopy.Preface(gap, language) + " " + CapabilityGapCopy.DiscoveredLeadIn(language);
                return CapabilityGapReplyBuilder.Redirect(gap, language, markdown, actions, alternativeQuestions);
            }

            case GapAlternative.DraftEmail:
            {
                var suppliers = request.NamedSupplier is { } named
                    ? [named]
                    : request.SupplierNames
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                        .Take(MaxDraftSupplierFollowUps)
                        .ToList();

                var actions = portfolioIsEmpty
                    ? routing.ResolveActions([CapabilityIntent.UnknownSupplier], context)
                    : routing.ResolveActions([CapabilityIntent.HowTo(CapabilityCatalog.PortfolioKey)], context);

                var markdown = opening + " " + CapabilityGapCopy.AskWhichContract(gap, language, null, portfolioIsEmpty);
                return CapabilityGapReplyBuilder.Redirect(
                    gap, language, markdown, actions, suppliers.Select(name => CapabilityGapCopy.DraftFollowUp(language, name)).ToList());
            }

            default:
            {
                var capabilityKey = gap.Alternative switch
                {
                    GapAlternative.Renewals => CapabilityCatalog.RenewalsKey,
                    GapAlternative.ContractDetail when contractId is not null => CapabilityCatalog.ContractDetailKey,
                    _ => CapabilityCatalog.PortfolioKey,
                };

                var actions = routing.ResolveActions([CapabilityIntent.HowTo(capabilityKey)], context);
                var markdown = opening + " " + CapabilityGapCopy.Preface(gap, language);
                return CapabilityGapReplyBuilder.Redirect(gap, language, markdown, actions, []);
            }
        }
    }
}
