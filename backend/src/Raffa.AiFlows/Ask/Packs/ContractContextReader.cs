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
/// The per-turn reads every Ask pack builder shares: a contract's 360 view, its supplier display
/// name, its benchmark key, its priced lines and the market deals of its supplier. Moved verbatim
/// from <see cref="Raffa.AiFlows.Ask.Orchestration.AskCopilotService"/> (F1 Ask split, PR-2). Not memoized
/// yet: each call reads exactly as often as the code it replaced.
/// </summary>
public sealed class ContractContextReader(
    Contract360QueryService contract360QueryService,
    ISupplierNameLookup supplierNameLookup,
    BenchmarkKeyResolution benchmarkKeyResolution,
    IBenchmarkService benchmarkService,
    IMarketDealLookup marketDealLookup,
    ITenantContext tenantContext,
    LineItemMarketPriceService? lineItemMarketPriceService = null)
{
    /// <summary>The tenant scope <c>AskAsync</c> already opened for this call
    /// (<see cref="ITenantContext.BeginScope"/>). Falls back to
    /// <see langword="default"/> only in the never-actually-reached case where a helper is somehow
    /// invoked outside that scope (fail-closed: <see langword="default"/> resolves to no rows
    /// under RLS, never another tenant's).</summary>
    private TenantId CurrentTenantId => tenantContext.Current ?? default;

    /// <summary>The Contract 360 view of <paramref name="contractId"/> in the turn's tenant scope, or
    /// <see langword="null"/> when it no longer resolves.</summary>
    internal Task<Contract360Result?> GetContract360Async(Guid contractId, CancellationToken cancellationToken) =>
        contract360QueryService.GetByIdAsync(CurrentTenantId, new EntityId(contractId), cancellationToken);

    /// <summary>Every market deal of <paramref name="supplierName"/> (see
    /// <see cref="IMarketDealLookup.GetBySupplierAsync"/>).</summary>
    internal Task<IReadOnlyList<MarketDeal>> GetDealsBySupplierAsync(string supplierName, CancellationToken cancellationToken) =>
        marketDealLookup.GetBySupplierAsync(supplierName, cancellationToken);

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
        var key = await benchmarkKeyResolution
            .ResolveAsync(CurrentTenantId, supplierId, cancellationToken)
            .ConfigureAwait(false);

        return key is BenchmarkKeyResult.Complete complete ? (complete.Supplier, complete.Geography) : (null, null);
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

        var names = await supplierNameLookup
            .GetNamesAsync(CurrentTenantId, [id], cancellationToken)
            .ConfigureAwait(false);

        return names.TryGetValue(id, out var name) ? name : type.ToString();
    }
}
