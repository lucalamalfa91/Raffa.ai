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

/// <summary>What an in-domain turn is about, as <see cref="AskCopilotService.AskAsync"/> hands it
/// down: exactly the values the former <c>BuildInDomainReplyAsync</c> parameters carried (F1 Ask split, PR-4).</summary>
internal sealed record AskTurnScope(
    TenantId TenantId,
    string Question,
    string? NamedSupplier,
    IReadOnlyList<(string Role, string Markdown)> RecentTurns,
    string Actor,
    PortfolioPage Portfolio,
    IReadOnlyDictionary<EntityId, string> SupplierNames,
    EntityId? ScopeContractId,
    PortfolioListItem? ScopedContractItem,
    AskTurnHints Hints,
    bool PreviousRaffaTurnWasInterview);

/// <summary>What the former <c>BuildInDomainReplyAsync</c> computed before any branch: the plan, the
/// forced and named contract, the disambiguation item and the routing context.</summary>
internal sealed record InDomainTurn(
    AskTurnScope Scope,
    IntentPlanResult Plan,
    PortfolioListItem? ForcedContractItem,
    PortfolioListItem? NamedContractItem,
    PackItem? DisambiguationItem,
    IReadOnlyList<PortfolioListItem> SupplierMatches,
    RoutingContext RoutingContext)
{
    public string Question => Scope.Question;

    public AskIntent Intent => Plan.Intent;
}

/// <summary>What an in-domain turn tells the caller about itself while it is built: the plan
/// (trigger T1 reads its basis). Filled by the planner; stays empty for a turn that never reached it.</summary>
internal sealed class InDomainTurnTrace
{
    public IntentPlanResult? Plan { get; set; }
}
