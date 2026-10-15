using Raffa.AiFlows.Shared.ContractContext;
using static Raffa.AiFlows.Ask.Interview.InterviewInputsFactory;
using static Raffa.AiFlows.Ask.Packs.ContractResolution;
using static Raffa.AiFlows.Ask.Packs.PackItemFactory;
using Raffa.AiFlows.Shared.Audit;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiFlows.Negotiation.DataCheck;
using Raffa.AiFlows.WebResearch.Orchestration;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Raffa.AiFlows.Ask.Answering;
using Raffa.AiFlows.Ask.Gate;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.Ask.Reply;
using Raffa.AiFlows.Ask.Routing;
using Raffa.AiFlows.CapabilityGaps.Drafting;
using Raffa.AiFlows.Negotiation.Orchestration;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiFlows.Shared.Reply;
using Raffa.AiFlows.Shared.Routing;
using Raffa.AiGateway.Telemetry;
using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Application.WebResearch;
using Raffa.Chat.Domain;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Insights.Application;
using Raffa.Insights.Contracts;
using Raffa.Insights.Criticality;
using Raffa.Insights.Strategy;
using Raffa.Market.Contracts;
using Raffa.Market.Retrieval;
using Raffa.Renewals.Application;
using Raffa.Savings.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Raffa.Suppliers.Products.Application;
using Raffa.AiFlows.Ask.Packs;
using Raffa.AiFlows.Ask.Orchestration;

using Raffa.AiFlows.CapabilityGaps.Catalog;
using Raffa.AiFlows.CapabilityGaps.Copy;
using Raffa.AiFlows.CapabilityGaps.Replies;
using Raffa.Chat.Application.Language;

namespace Raffa.AiFlows.Ask.Orchestration;

/// <summary>
/// The <see cref="GateLabel.CapabilityGap"/> branch of the composition root (ADR-030 D1–D3): a
/// turn that asks Raffa to perform an operation it cannot do yet leaves with an honest preface in
/// the question's language, the nearest real alternative and an offer to report the gap. For the
/// email/letter gaps the alternative is a drafted negotiation email written from the same Q3 pack a
/// renewal-strategy turn gets by <see cref="NegotiationDraftingWorkflow"/>. Moved verbatim from
/// <see cref="AskCopilotService"/> (F1 Ask split, PR-4).
/// </summary>
public sealed class CapabilityGapTurnHandler(
    RenewalStrategyPackBuilder renewalStrategy,
    AgentPhase agentPhase,
    ContractContextReader context,
    NegotiationDraftingWorkflow negotiationDraftingWorkflow,
    CapabilityRouting capabilityRouting,
    GateReplyBuilder gateReplies)
{
    internal async Task<(CopilotReply Reply, bool GuardIntervened, bool FallbackUsed)> BuildCapabilityGapReplyAsync(
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
            return (gateReplies.BuildUnseenScopeRefusal(portfolio), false, false);
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
    /// Ask's agentic flow exactly as <see cref="InDomainReplyPipeline"/> runs it (the
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
        Raffa.AiFlows.Shared.Pack.PackItem? disambiguationItem,
        RoutingContext routingContext,
        string actor,
        CancellationToken cancellationToken)
    {
        var goal = SavingsGoalParser.Parse(question);

        IReadOnlyList<Raffa.AiFlows.Shared.Pack.PackItem> packItems =
            await renewalStrategy.BuildRenewalStrategyWithEvidenceAsync(tenantId, question, namedContractItem, actor, cancellationToken, goal, persistTodos: false)
                .ConfigureAwait(false);

        if (disambiguationItem is not null)
        {
            packItems = packItems.Prepend(disambiguationItem).ToList();
        }

        // Ask's agentic flow, as on an in-domain turn: the market data check for this contract (what
        // it is missing and what the market says in its place), the market researcher's RAG notes,
        // then the council's plays — so the email's asks can lean on market figures where the
        // contract has none, labelled as estimates. (AgentPhase also appends the flow's audit detail, applies the pack budget.)
        var agents = await agentPhase.RunAsync(
                question,
                goal,
                packItems,
                new AgentPhaseRequest(
                    [namedContractItem], RunMarketResearch: true, ConveneCouncil: true, AppendCalendarItem: false),
                cancellationToken)
            .ConfigureAwait(false);
        var boundedPack = agents.BoundedPack;
        var supplierName = await context.ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false);

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
