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
/// Step 1 of Ask's agentic flow (AskAgentFlow), the deterministic market data check. Moved verbatim from
/// <see cref="Raffa.AiFlows.Ask.Orchestration.AskCopilotService"/> (F1 Ask split, PR-3).
/// </summary>
public sealed class MarketDataCheck(
    ContractContextReader context,
    IClock clock)
{
    private const int SafetyNetDealsTopK = 2;

    /// <summary>Contracts checked on a multi-contract turn — the first ones the pack is about.</summary>
    private const int MaxContractsChecked = 4;

    /// <summary>One contract's check: the pack items and the facts the deterministic lead reuses.</summary>
    private sealed record MarketSafetyNetResult(IReadOnlyList<PackItem> Items, MarketSafetyNet.ContractCheck? Check)
    {
        public static readonly MarketSafetyNetResult None = new([], null);
    }

    /// <summary>
    /// The market data check over <paramref name="contracts"/>, in order; <paramref name="checks"/>
    /// collects each contract's check for the deterministic lead. On more than one contract a
    /// coverage item says which contracts lack which data and what the market covers.
    /// </summary>
    internal async Task<MarketDataCheckResult> RunAsync(
        IReadOnlyList<PortfolioListItem> contracts,
        List<MarketSafetyNet.ContractCheck> checks,
        CancellationToken cancellationToken)
    {
        var items = new List<PackItem>();
        var missing = new List<string>();

        foreach (var contract in contracts.Take(MaxContractsChecked))
        {
            var result = await BuildMarketSafetyNetAsync(contract, cancellationToken).ConfigureAwait(false);
            items.AddRange(result.Items);
            if (result.Check is { } check)
            {
                checks.Add(check);
                missing.AddRange(check.Missing.Select(field => $"{check.SupplierName}: {field}"));
            }
        }

        if (MarketSafetyNet.CoverageItem(checks) is { } coverage)
        {
            items.Insert(0, coverage);
        }

        return new MarketDataCheckResult(DistinctByCitationKey(items), missing);
    }

    /// <summary>The contracts a multi-contract turn's data check covers: the ones the pack's items
    /// are about, in pack order (a quarter's candidates, the contracts a savings target ranks) —
    /// never contracts the turn is not about.</summary>
    internal static IReadOnlyList<PortfolioListItem> ContractsForDataCheck(IReadOnlyList<PackItem> pack, PortfolioPage portfolio)
    {
        var byId = portfolio.Items.ToDictionary(i => i.ContractId);
        return pack
            .Select(i => Guid.TryParse(i.ContractId, out var id) && byId.TryGetValue(id, out var item) ? item : null)
            .OfType<PortfolioListItem>()
            .DistinctBy(i => i.ContractId)
            .Take(MaxContractsChecked)
            .ToList();
    }

    private async Task<MarketSafetyNetResult> BuildMarketSafetyNetAsync(
        PortfolioListItem namedContractItem, CancellationToken cancellationToken)
    {
        var contract360 = await context.GetContract360Async(namedContractItem.ContractId, cancellationToken)
            .ConfigureAwait(false);
        if (contract360 is null)
        {
            return MarketSafetyNetResult.None;
        }

        var supplierName = await context.ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false);
        var currency = contract360.Overview.Currency;
        var asOfDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        var (benchmarkSupplierName, geography) = await context.ResolveBenchmarkKeyAsync(contract360.Header.SupplierId, cancellationToken)
            .ConfigureAwait(false);
        // The same priced lines (stored market comparison included) every other Ask pack reads.
        var pricedLines = await context.ResolvePricedLinesAsync(contract360, benchmarkSupplierName, geography, asOfDate, cancellationToken)
            .ConfigureAwait(false);
        var allDeals = await context
            .GetDealsBySupplierAsync(benchmarkSupplierName ?? supplierName, cancellationToken)
            .ConfigureAwait(false);
        var deals = MarketSafetyNet.MatchingProducts(NarrowDealsToContract(allDeals, currency, geography), pricedLines);

        var contractId = namedContractItem.ContractId;
        var missing = MarketSafetyNet.MissingFields(contract360);

        // The contract's own facts first (dates, spend, renewal): the market only fills what they lack.
        var items = new List<PackItem> { BuildContractFactItem(namedContractItem, supplierName) };

        if (MarketSafetyNet.GapsItem(contractId, supplierName, missing) is { } gaps)
        {
            items.Add(gaps);
        }

        MarketSafetyNet.AnnualEstimate? estimate = null;
        if (missing.Contains(MarketSafetyNet.Field.AnnualSpend))
        {
            estimate = MarketSafetyNet.EstimateAnnualValue(currency, pricedLines, deals);
            if (estimate is not null)
            {
                items.Add(MarketSafetyNet.EstimateItem(contractId, supplierName, estimate));
            }
        }

        var typicalNotice = MarketSafetyNet.TypicalNoticeDays(deals);
        DateOnly? estimatedNoticeDeadline = null;
        if (missing.Contains(MarketSafetyNet.Field.NoticeDeadline))
        {
            var endDate = contract360.Header.EndDate ?? contract360.Header.RenewalDate;
            estimatedNoticeDeadline = MarketSafetyNet.EstimatedNoticeDeadline(endDate, typicalNotice);
            if (MarketSafetyNet.NoticeEstimateItem(contractId, supplierName, endDate, typicalNotice) is { } notice)
            {
                items.Add(notice);
            }
        }

        if (MarketSafetyNet.TermsItem(contractId, supplierName, deals) is { } terms)
        {
            items.Add(terms);
        }

        items.AddRange(deals.Take(SafetyNetDealsTopK).Select(BuildMarketDealItem));

        return new MarketSafetyNetResult(
            items,
            new MarketSafetyNet.ContractCheck(supplierName, missing, estimate, typicalNotice, estimatedNoticeDeadline));
    }
}
