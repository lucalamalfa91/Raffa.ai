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

namespace Raffa.AiFlows.Ask.Packs;

/// <summary>
/// The PortfolioStrategy intent's criticality-ranking pack. Moved verbatim from
/// <see cref="Raffa.AiFlows.Ask.Orchestration.AskCopilotService"/> (F1 Ask split, PR-3).
/// </summary>
public sealed class PortfolioStrategyPackBuilder(
    ContractContextReader context,
    RenewalEngine renewalEngine,
    PriorityScoreCalculator priorityScoreCalculator,
    CriticalityScoreCalculator criticalityScoreCalculator,
    SavingsOpportunityService savingsOpportunityService,
    ITenantContext tenantContext)
{
    /// <summary>The tenant scope <c>AskAsync</c> already opened for this call
    /// (<see cref="ITenantContext.BeginScope"/>). Falls back to <see langword="default"/> only
    /// outside that scope (fail-closed: <see langword="default"/> resolves to no rows under RLS).</summary>
    private TenantId CurrentTenantId => tenantContext.Current ?? default;

    private const int PortfolioStrategyTopN = 5;

    internal async Task<IReadOnlyList<PackItem>> BuildPortfolioStrategyPackAsync(
        PortfolioPage portfolio, IReadOnlyDictionary<EntityId, string> supplierNames, CancellationToken cancellationToken)
    {
        if (portfolio.Items.Count == 0)
        {
            return [];
        }

        var allSavings = await savingsOpportunityService.ListAsync(CurrentTenantId, cancellationToken).ConfigureAwait(false);

        var contracts = new List<Contract360Result>();
        foreach (var item in portfolio.Items)
        {
            var contract360 = await context.GetContract360Async(item.ContractId, cancellationToken)
                .ConfigureAwait(false);

            if (contract360 is not null)
            {
                contracts.Add(contract360);
            }
        }

        var portfolioAnnualSpendByCurrency = ContractInsightsMapper.ComputePortfolioAnnualSpendByCurrency(contracts);

        var inputs = contracts
            .Select(contract => ContractInsightsMapper.ToCriticalityInputs(
                contract,
                ContractInsightsMapper.ComputePriority(contract.Header, renewalEngine, priorityScoreCalculator),
                portfolioAnnualSpendByCurrency,
                allSavings))
            .ToList();

        var scores = criticalityScoreCalculator.CalculateMany(inputs);

        var items = new List<PackItem>();
        foreach (var score in scores.Take(PortfolioStrategyTopN))
        {
            var contractItem = portfolio.Items.First(item => item.ContractId == score.ContractId.Value);
            var name = contractItem.SupplierId is { } sid && supplierNames.TryGetValue(new EntityId(sid), out var resolvedName)
                ? resolvedName
                : contractItem.Type.ToString();

            var topComponent = new[]
            {
                ("renewal urgency", score.RenewalUrgency),
                ("risk severity", score.RiskSeverity),
                ("spend weight", score.SpendWeight),
                ("savings potential", score.SavingsPotential),
                ("open critical facts", score.OpenCriticalFacts),
            }.MaxBy(c => c.Item2.Score);

            items.Add(new PackItem(
                InsightsCitationKeys.Calc($"criticality[{score.ContractId}]"),
                PackCorpus.Calc,
                $"{name} — criticality {score.TotalScore:0.#}/100",
                $"driven by {topComponent.Item1}",
                null, null,
                topComponent.Item2.Explanation,
                $"/contracts/{contractItem.ContractId}", null, null,
                "deterministic calculator",
                [new PackValue("totalScore", score.TotalScore.ToString(CultureInfo.InvariantCulture), PackValueKind.Number)],
                contractItem.ContractId.ToString()));
        }

        return items;
    }
}
