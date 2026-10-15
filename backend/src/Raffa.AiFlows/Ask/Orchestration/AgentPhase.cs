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

/// <summary>What the in-domain caller (from <see cref="IntentPolicy"/>) or the draft caller (its literals) asks of the agent phase.</summary>
internal sealed record AgentPhaseRequest(
    IReadOnlyList<PortfolioListItem> DataCheckContracts,
    bool RunMarketResearch,
    bool ConveneCouncil,
    bool AppendCalendarItem);

internal sealed record AgentPhaseResult(
    IReadOnlyList<PackItem> BoundedPack,
    IReadOnlyList<MarketSafetyNet.ContractCheck> DataChecks,
    AskFlowOutcome Flow);

/// <summary>
/// Ask's agentic flow between the intent pack and the answer: data check, researcher, council, then
/// the pack budget and the calendar item. Moved verbatim from <see cref="AskCopilotService"/>
/// (F1 Ask split, PR-4).
/// </summary>
public sealed class AgentPhase(AskAgentFlow askAgentFlow, MarketDataCheck marketDataCheck, PackBudget packBudget, IClock clock)
{
    internal async Task<AgentPhaseResult> RunAsync(
        string question,
        SavingsGoal? goal,
        IReadOnlyList<PackItem> packItems,
        AgentPhaseRequest request,
        CancellationToken cancellationToken)
    {
        // Ask's agentic flow (Raffa.AiFlows.Negotiation.Orchestration.AskAgentFlow), one coordinated sequence
        // before the answer role writes (persona v2.5: say what is missing, answer from the market):
        //   1. market data check (deterministic, below): for the contract the turn is about -- or,
        //      on a multi-contract turn (a quarter, savings across contracts), the ones the pack is
        //      about -- which fields are missing and what the market data says in their place;
        //   2. market researcher (agent): queries the market RAG for what is still missing, the
        //      same supplier first, then similar or related contracts;
        //   3. negotiation council (agents), on a savings or negotiation turn: two analysts over the
        //      enriched pack, then the strategist's ranked plays.
        // Steps 1-2 append market items after the intent's own (so those keep their priority under
        // the budget); step 3's plays go right after the target verdict. Not a clause or
        // document-status turn's data check: the market holds no clause text to stand in for the
        // contract's own. A failed agent degrades the flow, never the turn.
        var dataChecks = new List<MarketSafetyNet.ContractCheck>();
        var dataCheckContracts = request.DataCheckContracts;

        var flow = await askAgentFlow.RunAsync(
                new AskFlowRequest(
                    question,
                    packItems,
                    goal,
                    dataCheckContracts.Count > 0 ? ct => marketDataCheck.RunAsync(dataCheckContracts, dataChecks, ct) : null,
                    RunMarketResearch: request.RunMarketResearch,
                    ConveneCouncil: request.ConveneCouncil),
                cancellationToken)
            .ConfigureAwait(false);

        // F2-T01: the flow's steps, failures and market queries (names and counts) ride on the turn's one audit row.
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

        // A question about a period ("this quarter", "fine trimestre") gets today's date and the
        // calendar quarter as one more citable item -- appended after the budget (it is a few
        // dozen tokens) so the model can state the quarter's end as a pack value instead of
        // declining or computing a date NumericGuard would reject.
        if (request.AppendCalendarItem && boundedPack.Count > 0 && CalendarPackItem.IsPeriodQuestion(question))
        {
            boundedPack = [.. boundedPack, CalendarPackItem.Build(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime))];
        }


        return new AgentPhaseResult(boundedPack, dataChecks, flow);
    }

    /// <summary>Council plays go right after the calculators' target verdict (or the first item
    /// when there is none), ahead of the raw evidence they summarize.</summary>
    private static IReadOnlyList<PackItem> InsertCouncilItems(IReadOnlyList<PackItem> packItems, IReadOnlyList<PackItem> councilItems)
    {
        var list = packItems.ToList();
        var anchor = list.FindIndex(i => i.CitationKey is "calc:savings-target" or "calc:portfolio-target");
        var insertAt = anchor >= 0 ? anchor + 1 : Math.Min(1, list.Count);
        list.InsertRange(insertAt, councilItems);
        return list;
    }
}
