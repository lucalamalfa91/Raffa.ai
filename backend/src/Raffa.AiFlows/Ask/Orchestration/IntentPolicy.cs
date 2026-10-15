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

/// <summary>The per-intent pure rules of the Ask pipeline, one place (F1 Ask split, PR-4).</summary>
internal static class IntentPolicy
{
    public static bool IsCouncilIntent(AskIntent intent, PortfolioListItem? namedContractItem) =>
        intent is AskIntent.Savings or AskIntent.PortfolioSavingsTarget ||
        (intent == AskIntent.RenewalStrategy && namedContractItem is not null);

    /// <summary>Not a clause or document-status turn's data check: the market holds no clause text
    /// to stand in for the contract's own.</summary>
    public static bool SkipsDataCheck(AskIntent intent) => intent is AskIntent.DocumentStatus or AskIntent.Clause;

    public static bool RunsMarketResearch(AskIntent intent, PortfolioPage portfolio) =>
        portfolio.Items.Count > 0 && intent != AskIntent.DocumentStatus;

    /// <summary>Exactly the condition the pack switch uses to pick the live Q3 composition
    /// (renewal strategy with evidence, <c>persistTodos: true</c>).</summary>
    public static bool IsQ3PersistTurn(AskIntent intent, PortfolioListItem? namedContractItem) =>
        intent == AskIntent.RenewalStrategy && namedContractItem is not null;

    public static bool InjectsSavingsActions(AskIntent intent) =>
        intent is AskIntent.Savings or AskIntent.PortfolioSavingsTarget;
}
