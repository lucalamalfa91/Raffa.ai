using Raffa.AiFlows.Ask.Routing;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiFlows.Ask.Gate;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Shared.Audit;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;
using static Raffa.AiFlows.Ask.Packs.ContractResolution;

namespace Raffa.AiFlows.Ask.Orchestration;

/// <summary>
/// INV-02/INV-03: whether and when a turn starts the capability check (ADR-031), and the
/// <c>ask.capability_trigger</c> audit row. Moved verbatim from <see cref="AskCopilotService"/>
/// (F1 Ask split, PR-6): same T1/T2/T3 decisions, same order (the check starts before the reply for
/// T3 and Always, after it for T1/T2), same fail-open audit.
/// </summary>
public sealed class CapabilityCheckCoordinator(
    CapabilityCheckRunner capabilityCheckRunner,
    IAuditWriter auditWriter,
    IClock clock,
    InvestigatorTrigger? investigatorTriggerOverride = null)
{
    /// <summary>INV-01: the deterministic trigger of the capability investigator (the shared
    /// instance over the embedded lexicon unless a test supplies its own).</summary>
    private readonly InvestigatorTrigger investigatorTrigger = investigatorTriggerOverride ?? InvestigatorTrigger.Default;

    /// <summary>The five locals the turn used to carry for the check, plus what the lazy start needs.</summary>
    internal sealed class CheckState
    {
        public string GapInvestigation { get; set; } = "skipped";

        public bool InvestigatorTurn { get; set; }

        public bool Eligible { get; set; }

        public bool Started { get; set; }

        public GapInvestigationMode Mode { get; set; } = GapInvestigationMode.Triggered;

        public TriggerVerdict Verdict { get; set; } = TriggerVerdict.None;

        public required string TurnId { get; init; }

        public required Action Start { get; init; }
    }

    /// <summary>Decides, before the reply, whether the check starts now (Always, T3) or waits for
    /// the reply (T1/T2). <c>TurnId</c> on the slot is set before the check can start.</summary>
    internal CheckState Begin(
        TenantId tenantId,
        string question,
        DomainGateResult gate,
        PortfolioPage portfolio,
        IReadOnlyDictionary<EntityId, string> supplierNames,
        PortfolioListItem? scopedContractItem,
        IReadOnlyList<(string Role, string Markdown)> recentTurns,
        string actor,
        AskTurnHints hints,
        CapabilityCheckSlot? slot,
        string turnId)
    {
        // The named contract and supplier are resolved when the check starts (T3 now, T1/T2 after
        // the reply), as the former local function did.
        CheckState? state = null;
        void StartInvestigator()
        {
            var (namedItem, _, _) = ResolveNamedContractItem(scopedContractItem, gate.NamedSupplier, portfolio, supplierNames);
            var namedSupplier = namedItem?.SupplierId is { } namedSupplierId &&
                supplierNames.TryGetValue(new EntityId(namedSupplierId), out var namedSupplierName)
                    ? namedSupplierName
                    : null;

            slot!.FollowUp = capabilityCheckRunner.Start(new CapabilityCheckRequest(
                tenantId,
                question,
                supplierNames.Values.ToList(),
                portfolio.TotalCount,
                namedSupplier,
                namedItem?.ContractId,
                turnId,
                actor,
                // INV-06: the conversation's earlier user questions, oldest first. The investigator
                // reads them only while Chat:GapInvestigation:ContextTurns is above zero (default 0).
                UserQuestions(recentTurns)));
            state!.Started = true;
            state.GapInvestigation = "started";
        }

        state = new CheckState { TurnId = turnId, Start = StartInvestigator };

        state.InvestigatorTurn = slot is not null && gate.Label == GateLabel.InDomain && IsFreshTurn(hints);
        if (state.InvestigatorTurn)
        {
            slot!.TurnId = turnId;

            if (!capabilityCheckRunner.Enabled)
            {
                state.GapInvestigation = "off";
                state.Verdict = new TriggerVerdict(false, false, false, InvestigatorTrigger.ReasonKillSwitch);
            }
            else
            {
                state.Eligible = true;
                state.Mode = capabilityCheckRunner.Mode;

                // T3 reads the question alone: it (and Always mode) starts the check now, in
                // parallel with the answer. T1/T2 are decided after the reply, in Complete.
                if (state.Mode == GapInvestigationMode.Always ||
                    investigatorTrigger.Evaluate(plan: null, replyOutcome: null, question).ShouldRun)
                {
                    state.Start();
                }
            }
        }

        return state;
    }

    /// <summary>INV-02: T1 (the planner found no intent) and T2 (Raffa could not answer) are known
    /// only now. The check starts after the reply is built, never before it, so the answer's
    /// latency is unchanged.</summary>
    internal void Complete(
        CheckState state,
        InDomainTurnTrace trace,
        string question,
        CopilotReply reply,
        bool guardIntervened,
        bool fallbackUsed)
    {
        if (!state.Eligible)
        {
            return;
        }

        // The full verdict (T3 again, now with what T1 and T2 found), also in Always mode: the
        // audit then shows what Triggered would have done, which is how the reduction in
        // gaps-v1 calls is measured (plan 5.4).
        state.Verdict = investigatorTrigger.Evaluate(
            trace.Plan, new ReplyOutcome(reply.Kind, guardIntervened, fallbackUsed), question);

        if (!state.Started)
        {
            if (state.Verdict.ShouldRun)
            {
                state.Start();
            }
            else
            {
                state.GapInvestigation = "not-triggered";
            }
        }
    }

    /// <summary>The trigger audit row of a checked turn, written after the turn row.</summary>
    internal Task WriteTriggerAuditAsync(
        CheckState state, TenantId tenantId, string actor, CancellationToken cancellationToken) =>
        WriteTriggerAuditAsync(
            tenantId, actor, state.TurnId, state.Eligible ? state.Mode.ToString() : "off",
            state.Verdict, state.Started, cancellationToken);

    /// <summary>
    /// INV-03: the turn's <c>ask.capability_trigger</c> audit row — the mode, T1/T2/T3, whether the
    /// check ran and why. Written for every fresh in-domain turn the check could look at (also when
    /// the kill switch is off, and in Always mode, where the flags say what Triggered would have
    /// done). Never the question, never any text; the matching <c>ask.capability_outcome</c> row
    /// carries the verdict once the check finishes (<see cref="CapabilityCheckRunner"/>). Telemetry
    /// never fails a turn.
    /// </summary>
    private async Task WriteTriggerAuditAsync(
        TenantId tenantId,
        string actor,
        string turnId,
        string mode,
        TriggerVerdict verdict,
        bool ran,
        CancellationToken cancellationToken)
    {
        try
        {
            await auditWriter.WriteAsync(
                new AuditEntry(
                    tenantId,
                    actor,
                    CapabilityCheckRunner.TriggerAuditAction,
                    CapabilityCheckRunner.TriggerAuditResourceType,
                    turnId,
                    clock.UtcNow,
                    $"turnId={turnId} mode={mode} t1={verdict.T1} t2={verdict.T2} t3={verdict.T3} " +
                    $"ran={ran} reason={verdict.Reason} t3Language={verdict.T3Language ?? "none"} " +
                    $"lexicon={investigatorTrigger.LexiconVersion}"),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Fail-open: the answer is already built; a missing telemetry row must not lose it.
        }
    }

    /// <summary>The user's earlier questions of the conversation, oldest first (the roles the
    /// planner already reads as the user's: "you" and "user").</summary>
    internal static IReadOnlyList<string> UserQuestions(IReadOnlyList<(string Role, string Markdown)> recentTurns) =>
        recentTurns
            .Where(t => string.Equals(t.Role, "you", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(t.Role, "user", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Markdown)
            .ToList();

    /// <summary>A turn the capability investigator may look at (ADR-031): typed by the user, not
    /// resolved by key from an earlier turn (an interview option, a web consent or a decline),
    /// which continues a turn that was already investigated.</summary>
    internal static bool IsFreshTurn(AskTurnHints hints) =>
        hints.ForcedIntent is null &&
        hints.ForcedContractId is null &&
        hints.ForcedSupplierName is null &&
        hints.AuthorizedWebResearch is null &&
        !hints.DeclinedWebResearch;
}
