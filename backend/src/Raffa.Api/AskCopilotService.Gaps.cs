using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Drafting;
using Raffa.Chat.Application.Gaps;
using Raffa.Chat.Application.Gate;
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
/// facts, lever calculations, council plays, clause evidence, playbook) by
/// <see cref="NegotiationDraftingWorkflow"/>; for the other gaps it is a deep link into the screen
/// that already holds the answer.
/// </summary>
internal sealed partial class AskCopilotService
{
    private const string AuditDraftedAction = "chat.drafted";

    /// <summary>How many validated suppliers the "which contract?" reply offers as follow-ups.</summary>
    private const int MaxDraftSupplierFollowUps = 5;

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

        var (namedContractItem, disambiguationItem) = ResolveNamedContractItem(
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

        var markdown = CapabilityGapCopy.AskWhichContract(gap, language, unknownSupplier, portfolioIsEmpty);

        var actions = portfolioIsEmpty
            ? capabilityRouting.ResolveActions([CapabilityIntent.UnknownSupplier], routingContext)
            : capabilityRouting.ResolveActions([CapabilityIntent.HowTo(CapabilityCatalog.PortfolioKey)], routingContext);

        var followUps = portfolio.Items
            .Where(item => item.SupplierId is not null)
            .Select(item => supplierNames.TryGetValue(new EntityId(item.SupplierId!.Value), out var name) ? name : null)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxDraftSupplierFollowUps)
            .Select(name => CapabilityGapCopy.DraftFollowUp(language, name!))
            .ToList();

        return CapabilityGapReplyBuilder.Redirect(gap, language, markdown, actions, followUps);
    }

    /// <summary>
    /// The drafted email: the Q3 renewal-strategy pack with evidence (<c>persistTodos: true</c> —
    /// the user is negotiating this renewal, so the Renewals link the reply offers is true after
    /// the turn), the council's plays inserted exactly as <see cref="BuildInDomainReplyAsync"/>
    /// does, the pack budget, then <see cref="NegotiationDraftingWorkflow.DraftAsync"/>. A
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
            await BuildRenewalStrategyWithEvidenceAsync(tenantId, question, namedContractItem, actor, cancellationToken, goal)
                .ConfigureAwait(false);

        if (disambiguationItem is not null)
        {
            packItems = packItems.Prepend(disambiguationItem).ToList();
        }

        var council = await negotiationCouncil.RunAsync(question, packItems, goal, cancellationToken).ConfigureAwait(false);
        if (council.Items.Count > 0)
        {
            packItems = InsertCouncilItems(packItems, council.Items);
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
