using Raffa.AiFlows.Negotiation.Orchestration;
using Raffa.AiGateway.Telemetry;
using System.Globalization;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Drafting;
using Raffa.Chat.Application.Gaps;
using Raffa.Chat.Application.Gate;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.Language;
using Raffa.Chat.Application.Planning;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary>
/// The <see cref="GateLabel.CapabilityGap"/> branch of the composition root (ADR-030 D1–D3): a
/// turn that asks Raffa to perform an operation it cannot do yet leaves with an honest preface in
/// the question's language, the nearest real alternative and an offer to report the gap — never a
/// retrieval-then-abstain, never the feature tour. For the email/letter gaps the alternative is a
/// drafted negotiation email written from the same Q3 pack a renewal-strategy turn gets (contract
/// facts, lever calculations, clause evidence, playbook, plus what Ask's agentic flow adds: the
/// market data check, the market researcher's notes and the council plays) by
/// <see cref="NegotiationDraftingWorkflow"/>; for the other gaps it is a deep link into the screen
/// that already holds the answer. (A gap the capability investigator finds, ADR-031, is never this
/// turn's reply: it follows the answer as a separate message — <see cref="CapabilityCheckRunner"/>.)
/// </summary>
internal sealed partial class AskCopilotService
{
    private const string AuditDraftedAction = "chat.drafted";

    private async Task<(CopilotReply Reply, bool GuardIntervened, bool FallbackUsed)> BuildCapabilityGapReplyAsync(
        TenantId tenantId,
        string question,
        DomainGateResult gate,
        PortfolioPage portfolio,
        IReadOnlyDictionary<EntityId, string> supplierNames,
        EntityId? scopeContractId,
        PortfolioListItem? scopedContractItem,
        string actor,
        CancellationToken cancellationToken)
    {
        var gap = gate.Gap ?? throw new ArgumentException("A capability-gap turn must carry its catalog entry.", nameof(gate));
        var language = QuestionLanguage.Detect(question);

        // Same lock as the in-domain path (task E27/F02/US01/T01, NW-76): a scope id this caller can
        // no longer see refuses outright, never a silent fall-through to another contract.
        if (scopeContractId is not null && scopedContractItem is null)
        {
            return (BuildUnseenScopeRefusal(portfolio), false, false);
        }

        var (namedContractItem, disambiguationItem, _) = ResolveNamedContractItem(
            scopedContractItem, gate.NamedSupplier, portfolio, supplierNames);

        var contractIdForActions = namedContractItem is not null ? new EntityId(namedContractItem.ContractId) : (EntityId?)null;
        var routingContext = new RoutingContext(portfolio.TotalCount, CapabilityCallerRole.Standard, contractIdForActions);

        if (gap.Alternative != GapAlternative.DraftEmail)
        {
            return (BuildAlternativeRedirect(gap, language, namedContractItem, routingContext), false, false);
        }

        if (namedContractItem is null)
        {
            return (BuildWhichContractRedirect(gap, language, gate.NamedSupplier, portfolio, supplierNames, routingContext), false, false);
        }

        return await BuildDraftReplyAsync(
                tenantId, question, gap, language, namedContractItem, disambiguationItem, routingContext, actor, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Reminder → Renewals (with <c>?select=</c> when a contract resolved), export →
    /// Portfolio, purchase order → Contract 360 (or Portfolio when no contract resolved).
    /// <see cref="CapabilityRouting.ResolveActions"/> already swaps a greyed capability for the
    /// Documents upload action on an empty portfolio (R-SYS-04).</summary>
    private CopilotReply BuildAlternativeRedirect(
        CapabilityGap gap, string language, PortfolioListItem? namedContractItem, RoutingContext routingContext)
    {
        var capabilityKey = gap.Alternative switch
        {
            GapAlternative.Renewals => CapabilityCatalog.RenewalsKey,
            GapAlternative.Portfolio => CapabilityCatalog.PortfolioKey,
            GapAlternative.ContractDetail => namedContractItem is null ? CapabilityCatalog.PortfolioKey : CapabilityCatalog.ContractDetailKey,
            _ => CapabilityCatalog.AskKey,
        };

        var actions = capabilityRouting.ResolveActions([CapabilityIntent.HowTo(capabilityKey)], routingContext);

        return CapabilityGapReplyBuilder.Redirect(gap, language, CapabilityGapCopy.Preface(gap, language), actions, []);
    }

    /// <summary>What an in-domain turn tells the caller about itself while it is built: the plan
    /// (trigger T1 reads its basis). Filled by <see cref="BuildInDomainReplyAsync"/>; stays empty
    /// for a turn that never reached the planner.</summary>
    private sealed class InDomainTurnTrace
    {
        public IntentPlanResult? Plan { get; set; }
    }

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

    /// <summary>A turn the capability investigator may look at (ADR-031): typed by the user, not
    /// resolved by key from an earlier turn (an interview option, a web consent or a decline),
    /// which continues a turn that was already investigated.</summary>
    private static bool IsFreshTurn(AskTurnHints hints) =>
        hints.ForcedIntent is null &&
        hints.ForcedContractId is null &&
        hints.ForcedSupplierName is null &&
        hints.AuthorizedWebResearch is null &&
        !hints.DeclinedWebResearch;

    /// <summary>The draft alternative with no contract to draft for: the preface plus "which
    /// contract?", one follow-up chip per supplier on file (each re-enters this same gap with the
    /// name resolved), Portfolio as the action — or the Documents upload when nothing is on file.</summary>
    private CopilotReply BuildWhichContractRedirect(
        CapabilityGap gap,
        string language,
        string? namedSupplier,
        PortfolioPage portfolio,
        IReadOnlyDictionary<EntityId, string> supplierNames,
        RoutingContext routingContext)
    {
        var portfolioIsEmpty = portfolio.Items.Count == 0;

        // A typed name that resolved to no contract of this tenant is named back ("no validated X
        // contract"); a name that is known but somehow matched no row is not treated as unknown.
        var unknownSupplier = namedSupplier is not null &&
            !supplierNames.Values.Any(name => string.Equals(name, namedSupplier, StringComparison.OrdinalIgnoreCase))
                ? namedSupplier
                : null;

        var suppliersOnFile = portfolio.Items
            .Where(item => item.SupplierId is not null)
            .Select(item => supplierNames.TryGetValue(new EntityId(item.SupplierId!.Value), out var name) ? name : null);

        return CapabilityGapReplyBuilder.WhichContract(
            gap, language, opening: null, unknownSupplier, portfolioIsEmpty, suppliersOnFile, capabilityRouting, routingContext);
    }

    /// <summary>
    /// The drafted email: the Q3 renewal-strategy pack with evidence (<c>persistTodos: false</c> —
    /// F1-D07, decision D7: a draft is a copyable text with no side effect, so building its pack
    /// writes no negotiation todo; the Renewals link the reply offers opens the screen, whose
    /// todos the user creates by asking the renewal-strategy question or from the screen itself),
    /// Ask's agentic flow exactly as <see cref="BuildInDomainReplyAsync"/> runs it (the
    /// market data check and the market researcher's items appended, the council's plays inserted),
    /// the pack budget, then <see cref="NegotiationDraftingWorkflow.DraftAsync"/>. A
    /// template fallback or a retried writer is audited as a guard intervention
    /// (<c>abstainGuardIntervened=True</c>), the same channel the answer role's regenerate-once
    /// policy uses — which is what lets the golden set catch a fixture or prompt regression. The
    /// template alone also sets <c>fallbackUsed=True</c>, the same audit key the answer role's
    /// grounded fallback writes, so one filter finds every turn Raffa answered without the model.
    /// </summary>
    private async Task<(CopilotReply Reply, bool GuardIntervened, bool FallbackUsed)> BuildDraftReplyAsync(
        TenantId tenantId,
        string question,
        CapabilityGap gap,
        string language,
        PortfolioListItem namedContractItem,
        Raffa.Chat.Application.Pack.PackItem? disambiguationItem,
        RoutingContext routingContext,
        string actor,
        CancellationToken cancellationToken)
    {
        var goal = SavingsGoalParser.Parse(question);

        IReadOnlyList<Raffa.Chat.Application.Pack.PackItem> packItems =
            await BuildRenewalStrategyWithEvidenceAsync(tenantId, question, namedContractItem, actor, cancellationToken, goal, persistTodos: false)
                .ConfigureAwait(false);

        if (disambiguationItem is not null)
        {
            packItems = packItems.Prepend(disambiguationItem).ToList();
        }

        // Ask's agentic flow, as on an in-domain turn: the market data check for this contract (what
        // it is missing and what the market says in its place), the market researcher's RAG notes,
        // then the council's plays — so the email's asks can lean on market figures where the
        // contract has none, labelled as estimates.
        var flow = await askAgentFlow.RunAsync(
                new AskFlowRequest(
                    question,
                    packItems,
                    goal,
                    ct => RunMarketDataCheckAsync([namedContractItem], [], ct),
                    RunMarketResearch: true,
                    ConveneCouncil: true),
                cancellationToken)
            .ConfigureAwait(false);

        // F2-T01: see the in-domain turn; the draft turn's one audit row carries the flow's outcome too.
        RunContext.AddTurnDetail(flow.ToAuditDetail());

        if (flow.MarketItems.Count > 0)
        {
            packItems = [.. packItems, .. flow.MarketItems];
        }

        if (flow.CouncilItems.Count > 0)
        {
            packItems = InsertCouncilItems(packItems, flow.CouncilItems);
        }

        var boundedPack = packBudget.Apply(packItems);
        var supplierName = await ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false);

        var draft = await negotiationDraftingWorkflow
            .DraftAsync(new DraftRequest(question, language, supplierName, boundedPack, goal), cancellationToken)
            .ConfigureAwait(false);

        var actions = capabilityRouting.ResolveActions(
            [CapabilityIntent.HowTo(CapabilityCatalog.RenewalsKey), CapabilityIntent.HowTo(CapabilityCatalog.ContractDetailKey)],
            routingContext);

        var markdown = CapabilityGapCopy.Preface(gap, language) + " " + CapabilityGapCopy.DraftLeadIn(language, supplierName);

        var reply = CapabilityGapReplyBuilder.Draft(
            gap, language, markdown, draft, boundedPack, actions, CapabilityGapCopy.AfterDraftFollowUps(language, supplierName));

        var fallbackUsed = draft.Source == DraftSource.Template;
        var intervened = draft.Attempts > 1 || fallbackUsed;
        return (reply, intervened, fallbackUsed);
    }
}
