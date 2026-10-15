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

namespace Raffa.AiFlows.Ask.Orchestration;

/// <summary>
/// The planning half of an in-domain turn: the intent plan, the scope and forced-contract
/// resolution (both "unseen scope" refusals, before any pack is built), the named contract and the
/// routing context. Moved verbatim from <see cref="AskCopilotService"/> (F1 Ask split, PR-4).
/// </summary>
public sealed class InDomainTurnPlanner(IntentPlanner intentPlanner, GateReplyBuilder gateReplies)
{
    /// <summary>The planned turn, or <see langword="null"/> with the refusal in <paramref name="refusal"/>.</summary>
    internal InDomainTurn? Plan(AskTurnScope scope, InDomainTurnTrace? trace, out CopilotReply? refusal)
    {
        refusal = null;
        var question = scope.Question;
        var namedSupplier = scope.NamedSupplier;
        var portfolio = scope.Portfolio;
        var supplierNames = scope.SupplierNames;
        var recentTurns = scope.RecentTurns;
        var scopeContractId = scope.ScopeContractId;
        var scopedContractItem = scope.ScopedContractItem;
        var hints = scope.Hints;

        // A bare follow-up ("non mi hai risposto", "e quindi?") is planned on the previous user
        // question too, so it inherits that turn's intent and saving goal instead of falling
        // through to the StructuredFact default (see IntentPlanner.Plan's own doc comment).
        var previousUserQuestion = recentTurns
            .LastOrDefault(t => string.Equals(t.Role, "you", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(t.Role, "user", StringComparison.OrdinalIgnoreCase))
            .Markdown;

        var plan = hints.ForcedIntent is { } forcedIntent
            ? intentPlanner.Plan(question, namedSupplier, previousUserQuestion, forcedIntent)
            : intentPlanner.Plan(question, namedSupplier, previousUserQuestion);

        // INV-02: hand the plan to the caller, which decides trigger T1 (no intent recognised)
        // from it once the reply exists. Observation only: nothing below reads the trace.
        if (trace is not null)
        {
            trace.Plan = plan;
        }

        // ADR-030: an interview option that named one contract narrows this turn to it exactly as
        // a scoped conversation would; an id this tenant cannot see refuses (the NW-76 posture),
        // never silently widens into a portfolio-wide answer.
        PortfolioListItem? forcedContractItem = null;
        if (hints.ForcedContractId is { } forcedContractId)
        {
            forcedContractItem = portfolio.Items.FirstOrDefault(item => item.ContractId == forcedContractId.Value);
            if (forcedContractItem is null && plan.Intent != AskIntent.PortfolioMarketPosition)
            {
                refusal = gateReplies.BuildUnseenScopeRefusal(portfolio);
            return null;
            }
        }

        // AC-3, lock 4 (task E27/F02/US01/T01, NW-76): a scope id present on this conversation but
        // absent from this turn's own freshly-fetched portfolio refuses outright, before a single
        // BuildXxxPackAsync call below ever runs (no pack leak) -- never a silent fall-through to
        // an unscoped answer for a caller who explicitly named a contract they can no longer see.
        // PortfolioMarketPosition is exempt: lock 4 already makes it portfolio-wide regardless of
        // scope, so it never depends on the scoped contract resolving at all.
        if (scopeContractId is not null && scopedContractItem is null && plan.Intent != AskIntent.PortfolioMarketPosition)
        {
            refusal = gateReplies.BuildUnseenScopeRefusal(portfolio);
            return null;
        }

        // AC-2 (NW-76) / NW-80 (task E27/F05/US01/T01): a resolved, visible scope id wins outright
        // over a same-name portfolio lookup -- two rows can share one supplier's display name, and
        // picking whichever one a name match happens to hit first would silently answer about the
        // wrong contract. Lock 4: PortfolioMarketPosition is never narrowed to the scoped contract,
        // so it alone passes null through here and falls back to ResolveNamedContractItem's own
        // name-based, soonest-cancellation-deadline lookup below (which still applies for every
        // other, unscoped turn, and for every turn once no scope resolved at all).
        var (namedContractItem, disambiguationItem, supplierMatches) = ResolveNamedContractItem(
            plan.Intent == AskIntent.PortfolioMarketPosition ? null : (scopedContractItem ?? forcedContractItem),
            plan.NamedSupplier, portfolio, supplierNames);

        // AC-2: follows namedContractItem above, so a scoped turn's follow-up actions (e.g. "open
        // this contract") also target the real scoped id, never a same-name-but-wrong contract.
        var contractIdForActions = namedContractItem is not null ? new EntityId(namedContractItem.ContractId) : (EntityId?)null;
        var routingContext = new RoutingContext(portfolio.TotalCount, CapabilityCallerRole.Standard, contractIdForActions);

        return new InDomainTurn(scope, plan, forcedContractItem, namedContractItem, disambiguationItem, supplierMatches, routingContext);
    }
}
