using Raffa.Chat.Application.Council;
using Raffa.Chat.Application.Pack;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary>
/// Step 1 of Ask's agentic flow (<see cref="AskAgentFlow"/>), the deterministic market data check,
/// as <see cref="AskCopilotService"/> runs it (it alone can read the contracts, their benchmarks
/// and the market deals). For each contract the turn is about — the named one, or on a
/// multi-contract turn (a quarter, savings across contracts) the ones the pack is about — it
/// finds which fields the contract is missing and what the market data says in their place: a
/// narrow annual-value estimate, the notice deadline comparable customers' notice implies, the
/// terms comparable customers negotiated, the closest comparable deals. Which of those gaps the
/// question needs is for the market researcher and the answer to judge; the figures are
/// <see cref="MarketSafetyNet"/>'s, computed in code, and the answer narrates them as market
/// estimates, never as the contract's own data. Querying the market RAG is the researcher's job
/// (step 2), not this step's.
/// </summary>
internal sealed partial class AskCopilotService
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
    private async Task<MarketDataCheckResult> RunMarketDataCheckAsync(
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
    private static IReadOnlyList<PortfolioListItem> ContractsForDataCheck(IReadOnlyList<PackItem> pack, PortfolioPage portfolio)
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
        // The same priced lines (stored market comparison included) every other Ask pack reads.
        var pricedLines = await ResolvePricedLinesAsync(contract360, benchmarkSupplierName, geography, asOfDate, cancellationToken)
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
