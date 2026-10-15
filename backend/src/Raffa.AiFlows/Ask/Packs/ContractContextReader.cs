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
using System.Collections.Concurrent;

namespace Raffa.AiFlows.Ask.Packs;

/// <summary>
/// The per-turn reads every Ask pack builder shares: a contract's 360 view, its supplier display
/// name, its benchmark key, its priced lines and the market deals of its supplier. Moved verbatim
/// from <see cref="Raffa.AiFlows.Ask.Orchestration.AskCopilotService"/> (F1 Ask split, PR-2).
///
/// <para>
/// F1-T01: with <see cref="AskTurnOptions.MemoizeTurnReads"/> on, each named read is made once per
/// turn and its result reused (the reader is Scoped, and <see cref="BeginTurn"/> at the start of every
/// turn clears the cache, so a reused scope never serves a previous turn). Off (the default), every
/// call reads exactly as often as the code it replaced. Only reads no turn step writes are cached:
/// never the savings opportunities, the renewal todos, the embedding or market retrievals, the web
/// flows or the audit.
/// </para>
/// </summary>
public sealed class ContractContextReader(
    Contract360QueryService contract360QueryService,
    ISupplierNameLookup supplierNameLookup,
    BenchmarkKeyResolution benchmarkKeyResolution,
    IBenchmarkService benchmarkService,
    IMarketDealLookup marketDealLookup,
    ITenantContext tenantContext,
    LineItemMarketPriceService? lineItemMarketPriceService = null,
    AskTurnOptions? turnOptions = null)
{
    private readonly bool memoize = turnOptions?.MemoizeTurnReads == true;

    // A cached value is boxed so a legitimately null read (a contract that no longer resolves) is cached too.
    private sealed record Memo<T>(T Value);

    private readonly ConcurrentDictionary<(TenantId, Guid), Memo<Contract360Result?>> contract360Cache = new();
    private readonly ConcurrentDictionary<(TenantId, Guid, ContractDocumentType), Memo<string>> displayNameCache = new();
    private readonly ConcurrentDictionary<(TenantId, Guid), Memo<(string? SupplierName, string? Geography)>> benchmarkKeyCache = new();
    private readonly ConcurrentDictionary<string, Memo<IReadOnlyList<MarketDeal>>> dealsCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(TenantId, Guid, string?, string?, DateOnly), Memo<IReadOnlyList<PricedLine>>> pricedLinesCache = new();

    /// <summary>Starts a turn: forgets everything the previous turn of this scope read.</summary>
    public void BeginTurn()
    {
        contract360Cache.Clear();
        displayNameCache.Clear();
        benchmarkKeyCache.Clear();
        dealsCache.Clear();
        pricedLinesCache.Clear();
    }

    /// <summary>The tenant scope <c>AskAsync</c> already opened for this call
    /// (<see cref="ITenantContext.BeginScope"/>). Falls back to
    /// <see langword="default"/> only in the never-actually-reached case where a helper is somehow
    /// invoked outside that scope (fail-closed: <see langword="default"/> resolves to no rows
    /// under RLS, never another tenant's).</summary>
    private TenantId CurrentTenantId => tenantContext.Current ?? default;

    /// <summary>The Contract 360 view of <paramref name="contractId"/> in the turn's tenant scope, or
    /// <see langword="null"/> when it no longer resolves.</summary>
    internal async Task<Contract360Result?> GetContract360Async(Guid contractId, CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenantId;
        if (memoize && contract360Cache.TryGetValue((tenantId, contractId), out var cached))
        {
            return cached.Value;
        }

        var result = await contract360QueryService.GetByIdAsync(tenantId, new EntityId(contractId), cancellationToken).ConfigureAwait(false);
        if (memoize)
        {
            contract360Cache[(tenantId, contractId)] = new Memo<Contract360Result?>(result);
        }

        return result;
    }

    /// <summary>Every market deal of <paramref name="supplierName"/> (see
    /// <see cref="IMarketDealLookup.GetBySupplierAsync"/>).</summary>
    internal async Task<IReadOnlyList<MarketDeal>> GetDealsBySupplierAsync(string supplierName, CancellationToken cancellationToken)
    {
        if (memoize && dealsCache.TryGetValue(supplierName, out var cached))
        {
            return cached.Value;
        }

        var deals = await marketDealLookup.GetBySupplierAsync(supplierName, cancellationToken).ConfigureAwait(false);
        if (memoize)
        {
            dealsCache[supplierName] = new Memo<IReadOnlyList<MarketDeal>>(deals);
        }

        return deals;
    }

    /// <summary>
    /// The priced lines every Ask pack reads: the same <c>ContractInsightsMapper.ToPricedLines</c>
    /// call <c>GET /api/contracts/{id}/strategy</c> makes, including each line's stored market
    /// comparison (<see cref="LineItemMarketPriceService"/>, the figure Contract 360's market column
    /// shows) — so Ask, the strategy endpoint and the product table never state two different
    /// bands for one line (ADR-024 w17 clause 7).
    /// </summary>
    internal async Task<IReadOnlyList<PricedLine>> ResolvePricedLinesAsync(
        Contract360Result contract360,
        string? benchmarkSupplierName,
        string? geography,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        var cacheKey = (CurrentTenantId, contract360.ContractId.Value, benchmarkSupplierName, geography, asOfDate);
        if (memoize && pricedLinesCache.TryGetValue(cacheKey, out var cached))
        {
            return cached.Value;
        }

        var lines = await ReadPricedLinesAsync(contract360, benchmarkSupplierName, geography, asOfDate, cancellationToken).ConfigureAwait(false);
        if (memoize)
        {
            pricedLinesCache[cacheKey] = new Memo<IReadOnlyList<PricedLine>>(lines);
        }

        return lines;
    }

    private async Task<IReadOnlyList<PricedLine>> ReadPricedLinesAsync(
        Contract360Result contract360,
        string? benchmarkSupplierName,
        string? geography,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        var storedMarketPrices = lineItemMarketPriceService is null
            ? null
            : await lineItemMarketPriceService
                .GetCurrentAsync(CurrentTenantId, contract360.ContractId, cancellationToken)
                .ConfigureAwait(false);

        return await ContractInsightsMapper
            .ToPricedLines(contract360, benchmarkService, benchmarkSupplierName, geography, asOfDate, cancellationToken, storedMarketPrices)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Resolves the (supplier name, geography) key <see cref="AskCopilotService.BuildRenewalStrategyPackAsync"/>/
    /// <see cref="BuildMarketComparePackAsync"/> need for the async
    /// <c>ContractInsightsMapper.ToPricedLines</c> overload — the same
    /// <see cref="BenchmarkKeyResolution"/> <c>GET /api/contracts/{id}/strategy</c> already resolves
    /// (ADR-024 w17 clause 7 "one resolution per screen"; task E28/F01/US01/T01, NW-82). An
    /// <see cref="BenchmarkKeyResult.Incomplete"/> key (no <c>SupplierId</c>, an unresolved name, or
    /// no workspace country) maps to <c>(null, null)</c>, so <c>ToPricedLines</c> leaves every
    /// line's band unset rather than querying with a fabricated geography.
    /// </summary>
    internal async Task<(string? SupplierName, string? Geography)> ResolveBenchmarkKeyAsync(
        EntityId? supplierId, CancellationToken cancellationToken)
    {
        var tenantId = CurrentTenantId;
        if (memoize && supplierId is { } cachedId && benchmarkKeyCache.TryGetValue((tenantId, cachedId.Value), out var cached))
        {
            return cached.Value;
        }

        var key = await benchmarkKeyResolution
            .ResolveAsync(tenantId, supplierId, cancellationToken)
            .ConfigureAwait(false);

        var resolved = key is BenchmarkKeyResult.Complete complete ? (complete.Supplier, complete.Geography) : (null, null);
        if (memoize && supplierId is { } id)
        {
            benchmarkKeyCache[(tenantId, id.Value)] = new Memo<(string? SupplierName, string? Geography)>(resolved);
        }

        return resolved;
    }

    internal Task<string> ResolveDisplayNameAsync(PortfolioListItem item, CancellationToken cancellationToken) =>
        ResolveDisplayNameAsync(item.SupplierId is { } supplierId ? new EntityId(supplierId) : null, item.Type, cancellationToken);

    /// <summary>
    /// Shared core of <see cref="ResolveDisplayNameAsync(PortfolioListItem, CancellationToken)"/> —
    /// task E31/F02/US01/T01's own <see cref="AskCopilotService.BuildNegotiationPointsPackAsync"/> resolves a display
    /// name from a bare <see cref="Contract360Header.SupplierId"/> rather than a full
    /// <see cref="PortfolioListItem"/> (it starts from a contract id, not a portfolio row), so this
    /// is the one place both paths fall back to the contract type when no supplier name resolves
    /// (Appendix C rule 10: never fabricate a name that was not resolved).
    /// </summary>
    internal async Task<string> ResolveDisplayNameAsync(
        EntityId? supplierId, ContractDocumentType type, CancellationToken cancellationToken)
    {
        if (supplierId is not { } id)
        {
            return type.ToString();
        }

        var tenantId = CurrentTenantId;
        if (memoize && displayNameCache.TryGetValue((tenantId, id.Value, type), out var cached))
        {
            return cached.Value;
        }

        var names = await supplierNameLookup
            .GetNamesAsync(tenantId, [id], cancellationToken)
            .ConfigureAwait(false);

        var display = names.TryGetValue(id, out var name) ? name : type.ToString();
        if (memoize)
        {
            displayNameCache[(tenantId, id.Value, type)] = new Memo<string>(display);
        }

        return display;
    }
}
