using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Market;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// One line item's stored market comparison, as Contract 360 reads it. <see cref="Matched"/> is
/// <see langword="false"/> for a checked line with no comparable market record — every other
/// member but <see cref="Estimate"/> is then <see langword="null"/>. <see cref="Kind"/> says whether the band is the line's own
/// product, a bundle of the products it names, or only a similar product. <see cref="Estimate"/> is
/// an unmatched line's estimated band (converted, or AI) — never market data, kept apart from the
/// matched band so a reader that only knows <see cref="Matched"/> never sees it.
/// </summary>
public sealed record LineItemMarketPrice(
    EntityId LineItemId,
    bool Matched,
    string? RecordId,
    string? Product,
    string? Geography,
    string? Currency,
    int? TermMonths,
    decimal? UnitPriceP25,
    decimal? UnitPriceP50,
    decimal? UnitPriceP75,
    int? SampleSize,
    string? Provenance,
    DateTimeOffset? MarketUpdatedAt,
    DateTimeOffset CheckedAt,
    MarketMatchKind? Kind = null,
    MarketPriceEstimate? Estimate = null);

/// <summary>
/// Compares a contract's line items with the shared market corpus and keeps the result on
/// <see cref="ContractLineItemMarketPrice"/> — so the market column is filled from the moment a
/// document is extracted, and stays current afterwards:
/// <list type="bullet">
/// <item><b>At extraction</b> (<see cref="PriceContractAsync"/>): <c>DocumentProcessingPipeline</c>
/// prices every line right after the supplier is linked, on every (re)processing pass.</item>
/// <item><b>On read</b> (<see cref="GetCurrentAsync"/>): a line never priced, or last compared
/// longer ago than <see cref="RefreshAfter"/>, is re-priced before it is returned — a market feed
/// re-ingested since, or a supplier linked by a later correction, reaches the screen without a
/// cross-tenant batch job (the Worker cannot enumerate tenants).</item>
/// </list>
///
/// <para>
/// The matching itself is <see cref="IMarketPriceMatcher"/>'s (implemented in <c>Raffa.Market</c>,
/// which this module may not reference — ADR-002). Both ports are optional: a host without the
/// Market or Suppliers module composed in keeps whatever is stored and prices nothing new. A
/// matcher failure never fails the caller — extraction must not unwind a durable upload, and a
/// read falls back to the stored comparison.
/// </para>
/// </summary>
public sealed class LineItemMarketPriceService(
    DocumentsContractsDbContext dbContext,
    ITenantContext tenantContext,
    IClock clock,
    IMarketPriceMatcher? matcher = null,
    ISupplierNameLookup? supplierNames = null,
    ILogger<LineItemMarketPriceService>? logger = null,
    IMarketPriceEstimator? estimator = null)
{
    /// <summary>How long a stored comparison is trusted before a read re-prices the line.</summary>
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(6);

    private readonly ILogger _logger = logger ?? NullLogger<LineItemMarketPriceService>.Instance;

    /// <summary>Re-prices every line of <paramref name="contractId"/> now (the extraction path).</summary>
    public Task<IReadOnlyDictionary<EntityId, LineItemMarketPrice>> PriceContractAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken) =>
        RefreshAsync(tenantId, contractId, force: true, cancellationToken);

    /// <summary>The stored comparison per line item, re-pricing only lines never priced or stale.</summary>
    public Task<IReadOnlyDictionary<EntityId, LineItemMarketPrice>> GetCurrentAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken) =>
        RefreshAsync(tenantId, contractId, force: false, cancellationToken);

    private async Task<IReadOnlyDictionary<EntityId, LineItemMarketPrice>> RefreshAsync(
        TenantId tenantId, EntityId contractId, bool force, CancellationToken cancellationToken)
    {
        // Both this module's RLS-scoped context and the supplier-name lookup read the ambient
        // tenant claim; opened here like every sibling query service does (nesting is harmless).
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var stored = await dbContext.ContractLineItemMarketPrices
            .Where(p => p.TenantId == tenantId && p.ContractId == contractId)
            .ToDictionaryAsync(p => p.LineItemId, cancellationToken)
            .ConfigureAwait(false);

        if (matcher is null)
        {
            return ToResult(stored.Values);
        }

        var contract = await dbContext.Contracts
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.Id == contractId)
            .Select(c => new { c.SupplierId, c.Currency, c.RenewalTermMonths, c.AnnualSpend })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        if (contract is null)
        {
            return ToResult(stored.Values);
        }

        var lineItems = await dbContext.ContractLineItems
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.ContractId == contractId)
            .OrderBy(l => l.CreatedAt)
            .ThenBy(l => l.Id)
            .Select(l => new { l.Id, l.Description, l.Sku, l.AnnualCost })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        string? corpusVersion = null;
        try
        {
            corpusVersion = await matcher.GetCorpusVersionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Market corpus version unavailable; stored comparisons age out by time only.");
        }

        // A match stored before the match kind was recorded is re-priced at once: it may be one
        // product of a bundle line read as the whole line's price. So is any line compared against
        // an older corpus: a re-ingested feed reaches the screen on the next read.
        var staleBefore = clock.UtcNow - RefreshAfter;
        var toPrice = lineItems
            .Where(l => force
                || !stored.TryGetValue(l.Id, out var row)
                || row.CheckedAt < staleBefore
                || (row.RecordId is not null && row.MatchKind is null)
                || (corpusVersion is not null && row.CorpusVersion != corpusVersion))
            .ToList();
        if (toPrice.Count == 0)
        {
            return ToResult(stored.Values);
        }

        IReadOnlyList<MarketPriceMatch?> matches;
        MarketPriceContext priceContext;
        try
        {
            var supplierName = await ResolveSupplierNameAsync(tenantId, contract.SupplierId, cancellationToken)
                .ConfigureAwait(false);
            // The buyer's type for "customers of the same type": the contract's yearly value, or the
            // sum of its lines' own annual costs when the header carries none.
            var annualValue = contract.AnnualSpend
                ?? (lineItems.Any(l => l.AnnualCost is not null) ? lineItems.Sum(l => l.AnnualCost ?? 0m) : null);
            priceContext = new MarketPriceContext(supplierName, contract.Currency, contract.RenewalTermMonths, annualValue);
            matches = await matcher
                .MatchAsync(
                    priceContext,
                    toPrice.Select(l => new MarketPriceLine(l.Description, l.Sku)).ToList(),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Market pricing failed for contract {ContractId}; keeping the stored comparison.", contractId);
            return ToResult(stored.Values);
        }

        var now = clock.UtcNow;
        var estimates = await EstimateUnmatchedAsync(
                priceContext, toPrice.Select(l => new MarketPriceLine(l.Description, l.Sku)).ToList(), matches,
                i => force || !stored.TryGetValue(toPrice[i].Id, out var row) || row.EstimatedAt is null
                    || (corpusVersion is not null && row.CorpusVersion != corpusVersion),
                contractId, cancellationToken)
            .ConfigureAwait(false);

        for (var i = 0; i < toPrice.Count; i++)
        {
            var lineItemId = toPrice[i].Id;
            if (!stored.TryGetValue(lineItemId, out var row))
            {
                row = new ContractLineItemMarketPrice
                {
                    TenantId = tenantId,
                    LineItemId = lineItemId,
                    ContractId = contractId,
                    CheckedAt = now,
                };
                dbContext.ContractLineItemMarketPrices.Add(row);
                stored[lineItemId] = row;
            }

            var match = i < matches.Count ? matches[i] : null;
            Apply(row, match, now);
            row.CorpusVersion = corpusVersion;
            if (match is not null)
            {
                // A real match always wins: no estimate is kept beside it.
                ApplyEstimate(row, null, estimatedAt: null);
            }
            else if (estimates.TryGetValue(i, out var estimate))
            {
                ApplyEstimate(row, estimate, now);
            }
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException ex)
        {
            // Two readers re-pricing the same stale line at once: the unique (tenant, line item)
            // index keeps one row; this caller still returns the comparison it just computed.
            _logger.LogInformation(ex, "Concurrent market re-pricing of contract {ContractId}; another writer stored it first.", contractId);
            foreach (var entry in dbContext.ChangeTracker.Entries<ContractLineItemMarketPrice>().ToList())
            {
                entry.State = EntityState.Detached;
            }
        }

        return ToResult(stored.Values);
    }

    /// <summary>
    /// Estimates for the lines with no match that <paramref name="needsEstimate"/> says need a
    /// fresh one (keyed by their index in <paramref name="lines"/>; a missing key keeps the stored
    /// estimate). Only ever the unmatched lines: an estimate never sits beside a match.
    /// </summary>
    private async Task<IReadOnlyDictionary<int, MarketPriceEstimate?>> EstimateUnmatchedAsync(
        MarketPriceContext context,
        IReadOnlyList<MarketPriceLine> lines,
        IReadOnlyList<MarketPriceMatch?> matches,
        Func<int, bool> needsEstimate,
        EntityId contractId,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<int, MarketPriceEstimate?>();
        if (estimator is null)
        {
            return result;
        }

        var open = Enumerable.Range(0, lines.Count)
            .Where(i => (i >= matches.Count || matches[i] is null) && needsEstimate(i))
            .ToList();
        if (open.Count == 0)
        {
            return result;
        }

        try
        {
            var estimates = await estimator
                .EstimateAsync(context, open.Select(i => lines[i]).ToList(), cancellationToken)
                .ConfigureAwait(false);
            for (var k = 0; k < open.Count; k++)
            {
                result[open[k]] = k < estimates.Count ? estimates[k] : null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // No estimate recorded: the lines are asked again on the next refresh.
            _logger.LogWarning(ex, "Market estimate failed for contract {ContractId}; the lines keep no estimate.", contractId);
        }

        return result;
    }

    private static void ApplyEstimate(ContractLineItemMarketPrice row, MarketPriceEstimate? estimate, DateTimeOffset? estimatedAt)
    {
        row.EstimatedAt = estimatedAt;
        row.EstimateKind = estimate?.Kind;
        row.EstimateCurrency = estimate?.Currency;
        row.EstimateUnitPriceP25 = estimate?.UnitPriceP25;
        row.EstimateUnitPriceP50 = estimate?.UnitPriceP50;
        row.EstimateUnitPriceP75 = estimate?.UnitPriceP75;
        row.EstimateBasis = estimate?.Basis;
        row.EstimateProduct = estimate?.Product;
    }

    private static MarketPriceEstimate? ToEstimate(ContractLineItemMarketPrice row) =>
        row.RecordId is null
        && row.EstimateKind is { } kind
        && row.EstimateUnitPriceP25 is { } p25
        && row.EstimateUnitPriceP50 is { } p50
        && row.EstimateUnitPriceP75 is { } p75
            ? new MarketPriceEstimate(kind, row.EstimateCurrency ?? string.Empty, p25, p50, p75, row.EstimateBasis ?? string.Empty, row.EstimateProduct)
            : null;

    private async Task<string?> ResolveSupplierNameAsync(
        TenantId tenantId, EntityId? supplierId, CancellationToken cancellationToken)
    {
        if (supplierNames is null || supplierId is not { } id)
        {
            return null;
        }

        var names = await supplierNames.GetNamesAsync(tenantId, [id], cancellationToken).ConfigureAwait(false);
        return names.TryGetValue(id, out var name) ? name : null;
    }

    private static void Apply(ContractLineItemMarketPrice row, MarketPriceMatch? match, DateTimeOffset now)
    {
        row.CheckedAt = now;
        row.RecordId = match?.RecordId;
        row.Product = match?.Product;
        row.Geography = match?.Geography;
        row.Currency = match?.Currency;
        row.TermMonths = match?.TermMonths;
        row.UnitPriceP25 = match?.UnitPriceP25;
        row.UnitPriceP50 = match?.UnitPriceP50;
        row.UnitPriceP75 = match?.UnitPriceP75;
        row.SampleSize = match?.SampleSize;
        row.Provenance = match?.Provenance;
        row.MarketUpdatedAt = match?.UpdatedAt;
        row.MatchKind = match?.Kind;
    }

    private static IReadOnlyDictionary<EntityId, LineItemMarketPrice> ToResult(IEnumerable<ContractLineItemMarketPrice> rows) =>
        rows.ToDictionary(
            r => r.LineItemId,
            r => new LineItemMarketPrice(
                r.LineItemId,
                r.RecordId is not null && r.UnitPriceP50 is not null,
                r.RecordId,
                r.Product,
                r.Geography,
                r.Currency,
                r.TermMonths,
                r.UnitPriceP25,
                r.UnitPriceP50,
                r.UnitPriceP75,
                r.SampleSize,
                r.Provenance,
                r.MarketUpdatedAt,
                r.CheckedAt,
                r.RecordId is null ? null : r.MatchKind ?? MarketMatchKind.Exact,
                ToEstimate(r)));
}
