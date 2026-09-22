using Raffa.Chat.Application.Pack;
using Raffa.Documents.Contracts.Application;
using Raffa.Insights.Application;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary>
/// The market safety net half of <see cref="AskCopilotService"/> (persona v2.5): every commercial
/// turn about one contract also carries the contract's own facts, what it is missing and what the
/// market says in its place —
/// a narrow annual-value estimate when the annual spend is missing, the terms comparable customers
/// negotiated, the supplier's closest comparable deals, or, when the market feed has no deal for
/// this supplier, the market RAG's notes on similar or related contracts. The figures are
/// <see cref="MarketSafetyNet"/>'s, computed in code; the model only narrates them, as market
/// estimates, never as the contract's own data.
/// </summary>
internal sealed partial class AskCopilotService
{
    private const int SafetyNetDealsTopK = 2;
    private const int SimilarContractNotesTopK = 2;

    /// <summary>What the safety net found for one contract: the pack items (appended after the
    /// intent's own items), the contract's missing fields and the annual estimate, which the
    /// deterministic proposal reuses when the model gives no answer.</summary>
    private sealed record MarketSafetyNetResult(
        IReadOnlyList<PackItem> Items,
        string SupplierName,
        IReadOnlyList<string> Missing,
        MarketSafetyNet.AnnualEstimate? Estimate)
    {
        public static readonly MarketSafetyNetResult None = new([], string.Empty, [], null);
    }

    private async Task<MarketSafetyNetResult> BuildMarketSafetyNetAsync(
        PortfolioListItem namedContractItem, CancellationToken cancellationToken)
    {
        var contract360 = await contract360QueryService
            .GetByIdAsync(CurrentTenantId, new EntityId(namedContractItem.ContractId), cancellationToken)
            .ConfigureAwait(false);
        if (contract360 is null)
        {
            return MarketSafetyNetResult.None;
        }

        var supplierName = await ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false);
        var currency = contract360.Overview.Currency;
        var asOfDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        var (benchmarkSupplierName, geography) = await ResolveBenchmarkKeyAsync(contract360.Header.SupplierId, cancellationToken)
            .ConfigureAwait(false);
        var pricedLines = await InsightsEndpointExtensions
            .ToPricedLines(contract360, benchmarkService, benchmarkSupplierName, geography, asOfDate, cancellationToken)
            .ConfigureAwait(false);
        var allDeals = await marketDealLookup
            .GetBySupplierAsync(benchmarkSupplierName ?? supplierName, cancellationToken)
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
        if (missing.Contains("annual spend"))
        {
            estimate = MarketSafetyNet.EstimateAnnualValue(currency, pricedLines, deals);
            if (estimate is not null)
            {
                items.Add(MarketSafetyNet.EstimateItem(contractId, supplierName, estimate));
            }
        }

        if (MarketSafetyNet.TermsItem(contractId, supplierName, deals) is { } terms)
        {
            items.Add(terms);
        }

        if (deals.Count > 0)
        {
            items.AddRange(deals.Take(SafetyNetDealsTopK).Select(BuildMarketDealItem));
        }
        else
        {
            // No deal for this supplier: the market RAG's closest notes stand in as similar or
            // related contracts ("{supplier} {type}" finds the same kind of contract elsewhere).
            var notes = await marketKnowledgeRetrieval
                .SearchAsync($"{supplierName} {contract360.Header.Type}", SimilarContractNotesTopK, null, cancellationToken)
                .ConfigureAwait(false);
            if (notes.IsSuccess)
            {
                items.AddRange(notes.Value.Select(ToMarketPackItem));
            }
        }

        return new MarketSafetyNetResult(items, supplierName, missing, estimate);
    }

    /// <summary>The typical notice period comparable customers of this contract's supplier have,
    /// for the deterministic "no notice date on file" reply; <see langword="null"/> when the market
    /// records none.</summary>
    private async Task<int?> TypicalNoticeDaysAsync(Contract360Result contract360, string supplierName, CancellationToken cancellationToken)
    {
        var (benchmarkSupplierName, geography) = await ResolveBenchmarkKeyAsync(contract360.Header.SupplierId, cancellationToken)
            .ConfigureAwait(false);
        var allDeals = await marketDealLookup
            .GetBySupplierAsync(benchmarkSupplierName ?? supplierName, cancellationToken)
            .ConfigureAwait(false);

        return MarketSafetyNet.TypicalNoticeDays(NarrowDealsToContract(allDeals, contract360.Overview.Currency, geography));
    }
}
