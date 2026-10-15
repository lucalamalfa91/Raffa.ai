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
using Raffa.AiFlows.WebResearch.Orchestration;
using Raffa.Benchmark.Contracts;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain;
using Raffa.Insights.Contracts;
using Raffa.Insights.Savings;
using Raffa.Market.Contracts;
using Raffa.Documents.Contracts.Application;
using Raffa.Insights.Application;
using Raffa.SharedKernel;
using static Raffa.AiFlows.Ask.Packs.ContractResolution;

namespace Raffa.AiFlows.Ask.Interview;

/// <summary>
/// The display facts the interview planner phrases a "which contract?" question from. Moved
/// verbatim from <see cref="AskCopilotService"/> (F1 Ask split, PR-1).
/// </summary>
internal static class InterviewInputsFactory
{
    /// <summary>ADR-030: the display facts the interview planner phrases a "which contract?"
    /// question from -- supplier names, never guids in a label (R-ASK-08); the id rides in the
    /// option's resolution.</summary>
    internal static InterviewInputs BuildInterviewInputs(
        IReadOnlyList<PortfolioListItem> supplierMatches,
        PortfolioPage portfolio,
        IReadOnlyDictionary<EntityId, string> supplierNames)
    {
        var supplierContracts = supplierMatches.Select(item => ToInterviewChoice(item, supplierNames)).ToList();
        var noticeCandidates = portfolio.Items
            .Where(item => item.CancellationDeadline is not null)
            .OrderBy(item => item.CancellationDeadline)
            .ThenBy(item => item.ContractId)
            .Select(item => ToInterviewChoice(item, supplierNames))
            .ToList();

        return new InterviewInputs(supplierContracts, noticeCandidates);
    }

    private static InterviewContractChoice ToInterviewChoice(
        PortfolioListItem item, IReadOnlyDictionary<EntityId, string> supplierNames) =>
        new(
            item.ContractId.ToString(),
            DisplayNameFor(item, supplierNames),
            item.Type.ToString(),
            item.RenewalDate,
            item.CancellationDeadline,
            item.EndDate);
}
