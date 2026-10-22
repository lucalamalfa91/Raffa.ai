using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
/// <item><b>At extraction</b> (<see cref="PriceContractAsync"/>): <c>DocumentProcessingOrchestrator</c>
/// prices every line right after the supplier is linked, on every (re)processing pass.</item>
/// <item><b>On read</b> (<see cref="GetCurrentAsync"/>): a line never priced, or last compared
/// longer ago than <see cref="RefreshAfter"/>, is re-priced before it is returned — a market feed
/// re-ingested since, or a supplier linked by a later correction, reaches the screen without a
/// cross-tenant batch job (the Worker cannot enumerate tenants).</item>
/// </list>
///
/// <para>
/// <b>One refresh at a time per contract.</b> Contract 360 and its strategy pack read the same
/// contract at once, so two requests may both find a line stale. The refresh (matcher, estimator,
/// save) runs under a process-wide lock keyed by (tenant, contract): the second request waits, re-reads
/// what the first stored and finds nothing left to do -- one estimate call and one write instead of
/// two racing ones. A read that has nothing to refresh never takes the lock. Another process (the
/// Worker's extraction pass) is not covered by an in-process lock; the unique index below still
/// keeps one row there.
/// </para>
///
/// <para>
/// The matching itself is <see cref="IMarketPriceMatcher"/>'s (implemented in <c>Raffa.Market</c>,
/// which this module may not reference — ADR-002). Both ports are optional: a host without the
/// Market or Suppliers module composed in keeps whatever is stored and prices nothing new. A
/// matcher failure never fails the caller — extraction must not unwind a durable upload, and a
/// read falls back to the stored comparison.
/// </para>
///
/// <para>
/// <b>Keeping a read fast.</b> Matching against the market corpus is quick, but a line the corpus
/// cannot match may need a model estimate, which takes seconds. Two things keep that off the read:
/// <see cref="MarketRepriceJobHandler"/> re-prices a workspace's due contracts ahead of any read (the
/// API queues it when someone enters the workspace), and a read that still finds a re-pricing due
/// waits at most <see cref="MarketPricingOptions.ReadWaitBudgetMs"/> for it. The re-pricing itself is
/// the same code in both cases, run in its own scope so it can outlive the request; concurrent reads of
/// one contract share one run. If it finishes inside the budget the read returns exactly what it always
/// did; if not, the read returns the stored comparison and the next one sees the finished result.
/// </para>
/// </summary>
public sealed class LineItemMarketPriceService(
    DocumentsContractsDbContext dbContext,
    ITenantContext tenantContext,
    IClock clock,
    IMarketPriceMatcher? matcher = null,
    ISupplierNameLookup? supplierNames = null,
    ILogger<LineItemMarketPriceService>? logger = null,
    IMarketPriceEstimator? estimator = null,
    MarketPricingOptions? pricingOptions = null,
    IServiceScopeFactory? scopeFactory = null)
{
    /// <summary>How long a stored comparison is trusted before a read re-prices the line.</summary>
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromHours(6);

    // Process-wide: the racing readers are different requests, each with its own scope and DbContext.
    private static readonly KeyedAsyncLock<(Guid Tenant, Guid Contract)> RefreshLocks = new();

    // Process-wide: the re-pricings started by a read that may stop waiting for them, one per contract.
    // A read arriving while one runs joins it instead of starting another.
    private static readonly Dictionary<(Guid Tenant, Guid Contract), Task<IReadOnlyDictionary<EntityId, LineItemMarketPrice>>> DetachedRefreshes = [];

    private readonly ILogger _logger = logger ?? NullLogger<LineItemMarketPriceService>.Instance;
    private readonly MarketPricingOptions _pricing = pricingOptions ?? new MarketPricingOptions();

    /// <summary>Re-prices every line of <paramref name="contractId"/> now (the extraction path).</summary>
    public Task<IReadOnlyDictionary<EntityId, LineItemMarketPrice>> PriceContractAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken) =>
        RefreshAsync(tenantId, contractId, force: true, bounded: false, cancellationToken);

    /// <summary>The stored comparison per line item, re-pricing only lines never priced or stale.</summary>
    public Task<IReadOnlyDictionary<EntityId, LineItemMarketPrice>> GetCurrentAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken) =>
        RefreshAsync(tenantId, contractId, force: false, bounded: true, cancellationToken);

    /// <summary>
    /// The same re-pricing of only the lines never priced or stale, run to completion with no wait
    /// budget (the pre-warm job's path: nobody is waiting on it).
    /// </summary>
    public Task<IReadOnlyDictionary<EntityId, LineItemMarketPrice>> RefreshIfStaleAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken) =>
        RefreshAsync(tenantId, contractId, force: false, bounded: false, cancellationToken);

    /// <summary>
    /// Up to <paramref name="max"/> contracts of the tenant with a line a read would re-price now: the
    /// same four conditions <see cref="RefreshAsync"/> applies per line (never priced, compared longer
    /// ago than <see cref="RefreshAfter"/>, a match stored before its kind was recorded, or compared
    /// against an older corpus). Empty when no matcher is composed in: there is nothing to refresh.
    /// </summary>
    public async Task<IReadOnlyList<EntityId>> FindContractsNeedingRefreshAsync(
        TenantId tenantId, int max, CancellationToken cancellationToken)
    {
        if (matcher is null || max <= 0)
        {
            return [];
        }

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var corpusVersion = await TryGetCorpusVersionAsync(cancellationToken).ConfigureAwait(false);
        var staleBefore = clock.UtcNow - RefreshAfter;

        var prices = dbContext.ContractLineItemMarketPrices
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId);
        var due =
            from line in dbContext.ContractLineItems.AsNoTracking().Where(l => l.TenantId == tenantId)
            join price in prices on line.Id equals price.LineItemId into priced
            from price in priced.DefaultIfEmpty()
            where dbContext.Contracts.Any(c => c.TenantId == tenantId && c.Id == line.ContractId)
                && (price == null
                    || price.CheckedAt < staleBefore
                    || (price.RecordId != null && price.MatchKind == null)
                    || (corpusVersion != null && price.CorpusVersion != corpusVersion))
            select line.ContractId;

        return await due.Distinct().Take(max).ToListAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<string?> TryGetCorpusVersionAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await matcher!.GetCorpusVersionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Market corpus version unavailable; stored comparisons age out by time only.");
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<EntityId, LineItemMarketPrice>> RefreshAsync(
        TenantId tenantId, EntityId contractId, bool force, bool bounded, CancellationToken cancellationToken)
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
            .Select(l => new { l.Id, l.Description, l.Sku, l.Quantity, l.AnnualCost })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var corpusVersion = await TryGetCorpusVersionAsync(cancellationToken).ConfigureAwait(false);

        // A match stored before the match kind was recorded is re-priced at once: it may be one
        // product of a bundle line read as the whole line's price. So is any line compared against
        // an older corpus: a re-ingested feed reaches the screen on the next read.
        var staleBefore = clock.UtcNow - RefreshAfter;
        // Reads `stored` as it is when called: the lock below swaps it for a fresh read.
        bool IsStale(EntityId lineItemId) => force
            || !stored.TryGetValue(lineItemId, out var row)
            || row.CheckedAt < staleBefore
            || (row.RecordId is not null && row.MatchKind is null)
            || (corpusVersion is not null && row.CorpusVersion != corpusVersion);
        var toPrice = lineItems.Where(l => IsStale(l.Id)).ToList();
        if (toPrice.Count == 0)
        {
            return ToResult(stored.Values);
        }

        // A read does not wait for the re-pricing past its budget (see the type's remarks).
        if (bounded && scopeFactory is not null && _pricing.ReadWaitBudgetMs != MarketPricingOptions.UnboundedReadWait)
        {
            return await ReadWithinBudgetAsync(tenantId, contractId, ToResult(stored.Values), cancellationToken)
                .ConfigureAwait(false);
        }

        // Only one refresh per contract at a time (see the type's remarks). Whoever waited re-reads
        // what the refresh ahead of it stored -- this scope's tracked rows are stale by then -- and
        // prices only what that refresh left undone.
        using var refreshLock = await RefreshLocks
            .AcquireAsync((tenantId.Value, contractId.Value), cancellationToken)
            .ConfigureAwait(false);
        foreach (var entry in dbContext.ChangeTracker.Entries<ContractLineItemMarketPrice>().ToList())
        {
            entry.State = EntityState.Detached;
        }

        stored = await dbContext.ContractLineItemMarketPrices
            .Where(p => p.TenantId == tenantId && p.ContractId == contractId)
            .ToDictionaryAsync(p => p.LineItemId, cancellationToken)
            .ConfigureAwait(false);
        toPrice = lineItems.Where(l => IsStale(l.Id)).ToList();
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
                priceContext, toPrice.Select(l => new MarketPriceLine(l.Description, l.Sku, l.Quantity, l.AnnualCost)).ToList(), matches,
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

    /// <summary>
    /// Starts (or joins) the re-pricing of the contract in a scope of its own and waits for it at most
    /// the budget. Finished in time: its result, which is what the synchronous path would have returned.
    /// Otherwise <paramref name="stored"/>, what was on file when the read began; the re-pricing carries
    /// on and the next read sees it.
    /// </summary>
    private async Task<IReadOnlyDictionary<EntityId, LineItemMarketPrice>> ReadWithinBudgetAsync(
        TenantId tenantId,
        EntityId contractId,
        IReadOnlyDictionary<EntityId, LineItemMarketPrice> stored,
        CancellationToken cancellationToken)
    {
        var refresh = JoinOrStartDetachedRefresh(tenantId, contractId);

        if (_pricing.ReadWaitBudgetMs <= 0)
        {
            return stored;
        }

        using var stopWaiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var budget = Task.Delay(TimeSpan.FromMilliseconds(_pricing.ReadWaitBudgetMs), stopWaiting.Token);
        var first = await Task.WhenAny(refresh, budget).ConfigureAwait(false);
        await stopWaiting.CancelAsync().ConfigureAwait(false);

        if (!ReferenceEquals(first, refresh))
        {
            // Either the budget ran out (answer with what is stored) or the request itself was aborted.
            cancellationToken.ThrowIfCancellationRequested();
            return stored;
        }

        // A re-pricing that failed was already logged where it ran; the read keeps the stored comparison.
        return refresh.IsCompletedSuccessfully ? refresh.Result : stored;
    }

    private Task<IReadOnlyDictionary<EntityId, LineItemMarketPrice>> JoinOrStartDetachedRefresh(
        TenantId tenantId, EntityId contractId)
    {
        var key = (tenantId.Value, contractId.Value);
        lock (DetachedRefreshes)
        {
            if (DetachedRefreshes.TryGetValue(key, out var running))
            {
                return running;
            }

            // Task.Run: the work must not start on the request's thread, and the entry is inserted
            // below under the same lock the task's own cleanup takes, so it cannot be removed first.
            var started = Task.Run(() => RunDetachedRefreshAsync(tenantId, contractId, key));
            DetachedRefreshes[key] = started;
            return started;
        }
    }

    private async Task<IReadOnlyDictionary<EntityId, LineItemMarketPrice>> RunDetachedRefreshAsync(
        TenantId tenantId, EntityId contractId, (Guid Tenant, Guid Contract) key)
    {
        try
        {
            // Not the request's token: this work has to outlive a read that stopped waiting for it.
            using var timeout = new CancellationTokenSource(
                TimeSpan.FromSeconds(Math.Max(1, _pricing.DetachedRefreshTimeoutSeconds)));
            using var scope = scopeFactory!.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<LineItemMarketPriceService>();
            return await service
                .RefreshAsync(tenantId, contractId, force: false, bounded: false, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Background market re-pricing of contract {ContractId} failed; reads keep the stored comparison.", contractId);
            throw;
        }
        finally
        {
            lock (DetachedRefreshes)
            {
                DetachedRefreshes.Remove(key);
            }
        }
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

/// <summary>
/// An async mutual-exclusion lock per key, process-wide. A key's entry exists only while someone holds
/// or waits for it, so a long-running process does not accumulate one per contract it ever saw; a waiter
/// that is cancelled gives its place back.
/// </summary>
internal sealed class KeyedAsyncLock<TKey>
    where TKey : notnull
{
    private readonly Dictionary<TKey, Entry> _entries = [];

    /// <summary>Keys currently held or waited for (for tests).</summary>
    public int ActiveKeys
    {
        get
        {
            lock (_entries)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>Waits until no one else holds <paramref name="key"/>, then holds it until the result is disposed.</summary>
    public async Task<IDisposable> AcquireAsync(TKey key, CancellationToken cancellationToken)
    {
        Entry entry;
        lock (_entries)
        {
            if (!_entries.TryGetValue(key, out entry!))
            {
                _entries[key] = entry = new Entry();
            }

            entry.Users++;
        }

        try
        {
            await entry.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            Leave(key, entry, held: false);
            throw;
        }

        return new Releaser(this, key, entry);
    }

    private void Leave(TKey key, Entry entry, bool held)
    {
        if (held)
        {
            entry.Gate.Release();
        }

        lock (_entries)
        {
            // Nobody holds or waits for it any more: drop it. A caller arriving later makes a new one.
            if (--entry.Users == 0)
            {
                _entries.Remove(key);
            }
        }
    }

    private sealed class Entry
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public int Users { get; set; }
    }

    private sealed class Releaser(KeyedAsyncLock<TKey> owner, TKey key, Entry entry) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.Leave(key, entry, held: true);
            }
        }
    }
}
