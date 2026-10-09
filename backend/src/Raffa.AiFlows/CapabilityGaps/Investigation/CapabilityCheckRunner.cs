using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Raffa.AiFlows.CapabilityGaps.Catalog;
using Raffa.AiFlows.CapabilityGaps.Copy;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiFlows.CapabilityGaps.Replies;
using Raffa.AiFlows.Shared.Routing;
using Raffa.AiGateway.Telemetry;
using System.Globalization;
using Microsoft.Extensions.Options;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Language;
using Raffa.Chat.Application.Reply;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.CapabilityGaps.Investigation;

/// <summary>
/// Everything a capability check needs from the turn that started it (ADR-031), captured by
/// <c>AskCopilotService.AskAsync</c> while the request's own data is at hand — the check
/// outlives the request, so it never reads the request's scope again.
/// </summary>
/// <param name="SupplierNames">Every supplier the tenant has on file: scrubbed from the feature
/// texts, and the chips of an email follow-up.</param>
/// <param name="ValidatedContractCount">The portfolio size the routing context needs (an empty
/// portfolio swaps a greyed screen for the Documents upload).</param>
/// <param name="NamedSupplier">The known supplier the turn named or was scoped to, if any.</param>
/// <param name="NamedContractId">That supplier's contract, when one resolved — Renewals'
/// <c>?select=</c> and Contract 360's link.</param>
/// <param name="TurnId">The id that ties the turn's audit rows together (<c>ask.capability_trigger</c>,
/// <c>ask.capability_outcome</c>, <c>ask.capability_follow_up</c>) — not the message id, which
/// does not exist yet when the check starts.</param>
/// <param name="Actor">The caller's token subject, recorded on the outcome audit row.</param>
public sealed record CapabilityCheckRequest(
    TenantId TenantId,
    string Question,
    IReadOnlyList<string> SupplierNames,
    int ValidatedContractCount,
    string? NamedSupplier,
    Guid? NamedContractId,
    string? TurnId = null,
    string Actor = "system");

/// <summary>The one-turn hand-off between <c>AskCopilotService.AskAsync</c>, which decides
/// whether a turn is checked and starts the check, and the endpoint, which persists the answer and
/// then the follow-up. Null <see cref="FollowUp"/>: no check was started.</summary>
public sealed class CapabilityCheckSlot
{
    /// <summary>Completes with the follow-up to append after the answer, or null when the check
    /// found nothing to propose (or failed — the check is fail-open).</summary>
    public Task<CopilotReply?>? FollowUp { get; set; }

    /// <summary>Set by <c>AskCopilotService.AskAsync</c> for every checked turn (started or
    /// not): the id of the turn's <c>ask.capability_*</c> audit rows. Empty when the turn was not
    /// eligible for a check at all.</summary>
    public string? TurnId { get; set; }
}

/// <summary>
/// ADR-031, without latency: the capability investigator runs <b>beside</b> the answer, never in
/// front of it. <see cref="Start"/> launches the check in its own DI scope (the request's scoped
/// <c>IAiGateway</c> writes audit rows through a scoped DbContext that is gone once the response
/// is sent) and returns at its first I/O, so the answer is computed while the model thinks. When
/// the check finds an operation Raffa cannot perform, the endpoint appends the follow-up as a
/// separate Raffa message right after the answer — that half (persisting the message, the
/// background append, <c>WhenIdleAsync</c>) is <c>CapabilityFollowUpAppender</c>.
///
/// <para>
/// <b>Whether</b> a check starts is decided by the caller (INV-02): in
/// <see cref="GapInvestigationMode.Triggered"/> mode only a turn the
/// <see cref="InvestigatorTrigger"/> flags, in <see cref="GapInvestigationMode.Always"/> every
/// fresh in-domain turn. This class owns the kill switch and the mode (read through
/// <c>IOptionsMonitor</c>, so a change applies to the next turn without a restart) and the
/// telemetry of every outcome (INV-05): one <c>ask.capability_outcome</c> audit row per started
/// check — question, supported, known-gap, gap, low-confidence, unusable, failed, timeout — plus a
/// <c>drop</c> row when a proposal was found but the conversation had moved on. The rows carry the
/// verdict, its confidence, the gap key and the turn id; never the question or any model text.
/// </para>
/// </summary>
public sealed class CapabilityCheckRunner(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<GapInvestigationOptions> optionsMonitor,
    ILogger<CapabilityCheckRunner> logger)
{
    public const string AuditAction = "ask.capability_follow_up";
    public const string AuditResourceType = "capability_follow_up";

    /// <summary>INV-03: one row per eligible turn — the mode, T1/T2/T3, whether the check ran, why.</summary>
    public const string TriggerAuditAction = "ask.capability_trigger";
    public const string TriggerAuditResourceType = "capability_trigger";

    /// <summary>INV-05: one row per started check — its outcome, confidence and gap key.</summary>
    public const string OutcomeAuditAction = "ask.capability_outcome";
    public const string OutcomeAuditResourceType = "capability_outcome";

    /// <summary>INV-05: a proposal the check found but the conversation had already moved past.</summary>
    public const string OutcomeDrop = "drop";

    /// <summary>INV-05: the check ended on its time budget.</summary>
    public const string OutcomeTimeout = "timeout";

    /// <summary>How many validated suppliers an email follow-up offers as chips.</summary>
    private const int MaxDraftSupplierFollowUps = 5;

    private GapInvestigationOptions _lastGood = new();

    /// <summary>The live options. A configuration reload that no longer binds (an unknown
    /// <c>Mode</c> word) must never take Ask down: the last good value stays in force.</summary>
    private GapInvestigationOptions Current
    {
        get
        {
            try
            {
                return _lastGood = optionsMonitor.CurrentValue;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                logger.LogWarning(ex, "Chat:GapInvestigation could not be read; the last good configuration stays in force");
                return _lastGood;
            }
        }
    }

    /// <summary>The kill switch (<c>Chat:GapInvestigation:Enabled</c>), re-read on every call.</summary>
    public bool Enabled => Current.Enabled;

    /// <summary><c>Chat:GapInvestigation:Mode</c>, re-read on every call.</summary>
    public GapInvestigationMode Mode => Current.Mode;

    /// <summary>Starts the check for one turn. Never throws, never cancels with the request.</summary>
    public Task<CopilotReply?> Start(CapabilityCheckRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RunAsync(request);
    }

    private async Task<CopilotReply?> RunAsync(CapabilityCheckRequest request)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var tenantScope = tenantContext.BeginScope(request.TenantId);

            // Its own run inside the turn that started it (the turn id flows in from AskAsync), so
            // the investigator's ai.* row reads run=<id> step=capability-investigator.
            using var run = RunContext.BeginRun();
            using var step = RunContext.BeginStep("capability-investigator");

            string outcome;
            string? confidence = null;
            string? gapKey = null;
            try
            {
                var investigator = scope.ServiceProvider.GetRequiredService<CapabilityInvestigator>();
                var investigation = await investigator
                    .InvestigateAsync(request.Question, request.SupplierNames, CancellationToken.None)
                    .ConfigureAwait(false);

                logger.LogInformation(
                    "Capability check finished: {Outcome}{Failure}",
                    investigation.Outcome,
                    investigation.Failure is null ? string.Empty : " (" + investigation.Failure + ")");

                outcome = investigation.TimedOut ? OutcomeTimeout : investigation.Outcome;
                confidence = investigation.Confidence;
                gapKey = investigation.Gap?.Key;

                // The verdict is audited before the follow-up is built: a failure while building
                // the message must not lose the outcome row (INV-05: 100% of outcomes).
                await TryAuditOutcomeAsync(scope.ServiceProvider, request.TenantId, request.Actor, request.TurnId, outcome, confidence, gapKey)
                    .ConfigureAwait(false);

                return investigation.Gap is { } gap
                    ? BuildFollowUp(gap, investigation.AlternativeQuestions, request, scope.ServiceProvider.GetRequiredService<CapabilityRouting>())
                    : null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Capability check failed; no follow-up");
                await TryAuditOutcomeAsync(scope.ServiceProvider, request.TenantId, request.Actor, request.TurnId, "failed", null, null)
                    .ConfigureAwait(false);
                return null;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Capability check failed; no follow-up");
            return null;
        }
    }

    /// <summary>
    /// INV-05: one <c>ask.capability_outcome</c> audit row — the verdict, its confidence, the gap
    /// key and the turn id; never the question, a feature text or any model output. Telemetry
    /// never fails a check: a writer that throws is logged and swallowed.
    /// </summary>
    public async Task TryAuditOutcomeAsync(
        IServiceProvider services, TenantId tenantId, string actor, string? turnId, string outcome, string? confidence, string? gapKey)
    {
        try
        {
            var auditWriter = services.GetRequiredService<IAuditWriter>();
            var clock = services.GetRequiredService<IClock>();
            await auditWriter.WriteAsync(
                    new AuditEntry(
                        tenantId,
                        actor,
                        OutcomeAuditAction,
                        OutcomeAuditResourceType,
                        FormatTurnId(turnId),
                        clock.UtcNow,
                        $"turnId={FormatTurnId(turnId)} outcome={outcome} confidence={confidence ?? "none"} gapKey={gapKey ?? "none"}"),
                    CancellationToken.None)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The capability outcome ({Outcome}) could not be audited", outcome);
        }
    }

    public static string FormatTurnId(string? turnId) => turnId ?? "none";

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
                // The named supplier alone when the turn named one; otherwise every supplier on file
                // (the builder sorts, de-duplicates and caps them).
                IEnumerable<string?> suppliers = request.NamedSupplier is { } named ? [named] : request.SupplierNames;

                return CapabilityGapReplyBuilder.WhichContract(
                    gap, language, opening, unknownSupplier: null, portfolioIsEmpty, suppliers, routing, context);
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
