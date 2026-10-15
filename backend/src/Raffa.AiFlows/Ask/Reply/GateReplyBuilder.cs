using Raffa.AiFlows.Shared.Audit;
using System.Globalization;
using Raffa.AiFlows.Ask.Answering;
using Raffa.AiFlows.Ask.Gate;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Negotiation.Verdict;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiFlows.Shared.Reply;
using Raffa.AiFlows.Shared.Routing;
using Raffa.Benchmark.Contracts;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain;
using Raffa.Insights.Savings;
using Raffa.Market.Contracts;
using Raffa.Documents.Contracts.Application;
using Raffa.Insights.Application;
using Raffa.SharedKernel;
using static Raffa.AiFlows.Ask.Packs.ContractResolution;
using static Raffa.AiFlows.Ask.Packs.PackItemFactory;

namespace Raffa.AiFlows.Ask.Reply;

/// <summary>
/// The deterministic replies for the domain gate's labels (R-ASK-02: greeting / off-domain, legal,
/// capability, needs-document), the routing-only reply of the Navigate/QuoteRoute intents and the
/// unseen-scope refusal. Moved verbatim from <see cref="AskCopilotService"/> (F1 Ask split, PR-1):
/// no model call happens in any of them.
/// </summary>
public sealed class GateReplyBuilder(CapabilityRouting capabilityRouting, AbstainRecovery abstainRecovery)
{
    internal CopilotReply BuildGreetingReply(
        PortfolioPage portfolio, IReadOnlyDictionary<EntityId, string> supplierNames)
    {
        var (hookSentence, hookContractId) = BuildPortfolioHook(portfolio, supplierNames);
        var routingContext = new RoutingContext(portfolio.TotalCount, CapabilityCallerRole.Standard, hookContractId);

        var actions = hookContractId is not null
            ? capabilityRouting.ResolveActions([CapabilityIntent.HowTo(CapabilityCatalog.ContractDetailKey)], routingContext)
            : capabilityRouting.ResolveActions([CapabilityIntent.HowTo(CapabilityCatalog.DocumentsKey)], routingContext);

        return RedirectReplyBuilder.GreetingOrOffDomain(hookSentence, actions);
    }

    internal CopilotReply BuildLegalReply(
        PortfolioPage portfolio, IReadOnlyDictionary<EntityId, string> supplierNames, string? namedSupplier)
    {
        var contractId = namedSupplier is null ? null : FindContractIdBySupplier(portfolio, supplierNames, namedSupplier);
        var routingContext = new RoutingContext(portfolio.TotalCount, CapabilityCallerRole.Standard, contractId);

        var actions = contractId is not null
            ? capabilityRouting.ResolveActions([CapabilityIntent.HowTo(CapabilityCatalog.ContractDetailKey)], routingContext)
            : capabilityRouting.ResolveActions([CapabilityIntent.HowTo(CapabilityCatalog.DocumentsKey)], routingContext);

        return RedirectReplyBuilder.Legal(actions);
    }

    internal CopilotReply BuildCapabilityReply(int validatedContractCount)
    {
        var routingContext = new RoutingContext(validatedContractCount, CapabilityCallerRole.Standard);
        var actions = capabilityRouting.ResolveActions([CapabilityIntent.CapabilityList], routingContext);

        var citations = BuildFeatureCitations(
            [CapabilityCatalog.DocumentsKey, CapabilityCatalog.PortfolioKey, CapabilityCatalog.RenewalsKey, CapabilityCatalog.QuoteCheckKey]);

        const string markdown =
            "I answer from your validated contracts and route you to the right part of Raffa:\n" +
            "- Documents [1] — upload contracts, review weak facts.\n" +
            "- Portfolio [2] — every contract, spend, liability and risk in one table.\n" +
            "- Renewals [3] — deadlines and the action for each.\n" +
            "- Quote check [4] — benchmark a new quote against the market and your history.\n" +
            "Ask me about dates, spend, notice periods, clauses — or which of these to open.";

        return RedirectReplyBuilder.Capability(markdown, citations, actions);
    }

    internal CopilotReply BuildNeedsDocumentReply(string namedSupplier, int validatedContractCount)
    {
        var routingContext = new RoutingContext(validatedContractCount, CapabilityCallerRole.Standard);
        var actions = capabilityRouting.ResolveActions([CapabilityIntent.UnknownSupplier], routingContext);
        return RedirectReplyBuilder.NeedsDocument(namedSupplier, actions);
    }

    internal CopilotReply BuildRoutingOnlyReply(IntentPlanResult plan, RoutingContext routingContext)
    {
        var intents = plan.Intent == AskIntent.QuoteRoute
            ? new List<CapabilityIntent> { CapabilityIntent.Benchmark }
            : new List<CapabilityIntent> { CapabilityIntent.HowTo(CapabilityCatalog.DocumentsKey) };

        var actions = capabilityRouting.ResolveActions(intents, routingContext);

        var markdown = plan.Intent == AskIntent.QuoteRoute
            ? "That is a job for Quote check: it compares a quote or contract with market " +
              "benchmarks and with what you already pay."
            : "Here you go.";

        return new CopilotReply(ReplyKind.Answer, markdown, [], actions, ReplyProvenance.NoModelCall(["raffa"]), []);
    }

    /// <summary>
    /// AC-3 (task E27/F02/US01/T01, NW-76; ADR-024 w19 cl. 12): the reply for a
    /// <c>scopeContractId</c> that does not resolve to a row in this turn's own freshly-fetched
    /// portfolio. <see cref="ReplyKind.Refusal"/>, not <see cref="ReplyKind.Abstain"/> — this is not
    /// "a real question with no groundable evidence", it is "the caller named a contract they
    /// cannot see", the same distinction <see cref="RedirectReplyBuilder.Legal"/> already draws for
    /// a different refusal. Reuses <see cref="AbstainRecovery.ResolveAbstainRecoveryActions"/> for the same
    /// upload-or-ask-again next step every other "cannot proceed for this contract" reply in this
    /// method already offers, rather than inventing a third recovery shape — a bare
    /// <see cref="RoutingContext"/> with no <c>ContractId</c>, since the one contract this turn
    /// named is exactly the one that is not available to route to.
    /// </summary>
    internal CopilotReply BuildUnseenScopeRefusal(PortfolioPage portfolio)
    {
        var routingContext = new RoutingContext(portfolio.TotalCount, CapabilityCallerRole.Standard);

        return new CopilotReply(
            ReplyKind.Refusal,
            "The contract this conversation is scoped to is no longer available to you. Open " +
            "Portfolio to pick a contract, or ask a portfolio-wide question instead.",
            [],
            abstainRecovery.ResolveAbstainRecoveryActions(portfolio, routingContext),
            ReplyProvenance.NoModelCall([]),
            []);
    }
}
