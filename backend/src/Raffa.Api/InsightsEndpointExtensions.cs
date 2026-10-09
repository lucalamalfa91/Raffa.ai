using Raffa.AiFlows.Shared.ContractContext;
using Raffa.Api.Infrastructure;
using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Insights.Contracts;
using Raffa.Insights.Criticality;
using Raffa.Insights.Strategy;
using Raffa.Renewals.Application;
using Raffa.Renewals.Domain;
using Raffa.Savings.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Market;

namespace Raffa.Api;

/// <summary>
/// Maps <c>GET /api/insights/criticality</c> and <c>GET /api/contracts/{id}/strategy</c> (task
/// E13/F07/US01/T01, insights-calculators; parent story us-01-insights AC-5: "return the same
/// numbers the Ask pack narrates (shared builder, one source)"). <b>Not called from
/// <c>Program.cs</c> by this task</b> — F06/T01 maps it in phase 3 (this task's own coding
/// objective) — so these routes are unreachable today; every composition/mapping method below is
/// instead unit-tested directly, with hand-built fakes, from <c>Raffa.Insights.Tests</c> (this
/// task's own "Tests required" table pins "endpoint composition" there, not
/// <c>Raffa.Api.Tests</c>) — <c>Raffa.Insights.Tests.csproj</c> references this project for
/// exactly that reason.
///
/// <para>
/// Thin composition per ADR-002: every real decision (component formulas, weights, citation keys,
/// levers, next-step wording) is made by <c>Raffa.Insights.Criticality.CriticalityScoreCalculator</c>
/// / <see cref="StrategyPackBuilder"/> (both pure, unit-tested independently of this class); this
/// file only resolves the tenant, fetches tenant data through each module's own already-existing
/// query service (<see cref="PortfolioQueryService"/> / <see cref="Contract360QueryService"/> /
/// <see cref="SavingsOpportunityService"/> — Documents/Contracts and Savings; <see cref="RenewalEngine"/>
/// / <see cref="PriorityScoreCalculator"/> — Renewals), maps the result onto Insights' own DTOs (the
/// one mapping neither module may do itself — <c>Raffa.Insights</c>'s allow-list is exactly
/// <c>[SharedKernel, Benchmark]</c>; same pattern <c>RenewalsEndpointExtensions.ToCandidate</c>/
/// <c>ComputePriority</c> already use for the identical reason), and wire-shapes the response.
/// </para>
///
/// <para>
/// <b>Benchmark matching is wired in the host</b> (task E21/F03/US01/T01, NW-62):
/// <see cref="BenchmarkKeyResolution"/> — the same resolver the 360's <c>benchmark</c> member uses,
/// one resolution per screen (ADR-024 w17 clause 7) — supplies the (supplier name, geography)
/// pair from the supplier lookup and the workspace country (ISO 3166-1 alpha-2). Geography is
/// resolved here because <c>Raffa.Insights</c>'s allow-list is exactly <c>[SharedKernel, Benchmark]</c>
/// and cannot read the workspace. <see cref="ToPricedLines"/> then calls
/// <see cref="IBenchmarkService.GetBenchmarkAsync"/> per priced line. Two honest shapes only
/// (ADR-001 w17 clause 4): a representative position carrying adapter, sample size and as-of date,
/// or the explicit "insufficient market data" when the adapter abstains or the key is incomplete.
/// Never a fabricated number, never a bare percentile, never "market" unqualified.
/// </para>
///
/// Same interim <c>X-Tenant-Id</c> header placeholder as every other endpoint in this host (ADR-010
/// is not in this task's "Architecture decisions in force" list).
/// </summary>
public static class InsightsEndpointExtensions
{
    public static IEndpointRouteBuilder MapInsightsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/insights/criticality", GetCriticalityAsync);
        endpoints.MapGet("/api/contracts/{id}/strategy", GetContractStrategyAsync);
        return endpoints;
    }

    /// <summary>
    /// <c>GET /api/insights/criticality</c> (AC-1/AC-2/AC-5): the ranked criticality list for every
    /// contract in the caller's tenant. Reuses <see cref="PortfolioQueryService.GetPortfolioAsync"/>
    /// only to enumerate contract ids (same <see cref="PortfolioPageRequest.MaxPageSize"/> = 100 cap
    /// every other portfolio-wide composition in this host already accepts and documents — see
    /// <c>RenewalsEndpointExtensions</c>/<c>SavingsKpiEndpointExtensions</c>), then reads full detail
    /// per contract via <see cref="Contract360QueryService"/> — an N+1 sequence, the same
    /// "materialize the whole tenant set, optimize later" tradeoff <c>SavingsKpiEndpointExtensions</c>'s
    /// own doc comment already accepts for a portfolio-wide composition.
    /// </summary>
    private static async Task<IResult> GetCriticalityAsync(
        HttpRequest request,
        PortfolioQueryService portfolioQueryService,
        Contract360QueryService contract360QueryService,
        RenewalEngine renewalEngine,
        PriorityScoreCalculator priorityScoreCalculator,
        SavingsOpportunityService savingsOpportunityService,
        CriticalityScoreCalculator criticalityScoreCalculator,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantGuid = caller.TenantId.Value;

        var tenantId = new TenantId(tenantGuid);

        var portfolioPage = await portfolioQueryService.GetPortfolioAsync(
            tenantId,
            PortfolioFilter.None,
            new PortfolioPageRequest(Page: 1, PageSize: PortfolioPageRequest.MaxPageSize),
            cancellationToken).ConfigureAwait(false);

        var allSavingsOpportunities = await savingsOpportunityService
            .ListAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        var contracts = new List<Contract360Result>();
        foreach (var item in portfolioPage.Items)
        {
            var contract360 = await contract360QueryService
                .GetByIdAsync(tenantId, new EntityId(item.ContractId), cancellationToken)
                .ConfigureAwait(false);

            // The portfolio row was just read a moment ago; a null here means a concurrent delete —
            // skip it rather than fail the whole ranking for one disappeared contract.
            if (contract360 is not null)
            {
                contracts.Add(contract360);
            }
        }

        var portfolioAnnualSpendByCurrency = ComputePortfolioAnnualSpendByCurrency(contracts);

        var inputs = contracts
            .Select(contract => ToCriticalityInputs(
                contract,
                ComputePriority(contract.Header, renewalEngine, priorityScoreCalculator),
                portfolioAnnualSpendByCurrency,
                allSavingsOpportunities))
            .ToList();

        var scores = criticalityScoreCalculator.CalculateMany(inputs);

        return Results.Ok(new
        {
            items = scores.Select(ToCriticalityResponse),
            totalCount = portfolioPage.TotalCount,
        });
    }

    /// <summary>
    /// <c>GET /api/contracts/{id}/strategy</c> (AC-3/AC-4/AC-5): the renewal-strategy pack for one
    /// tenant-scoped contract. Same guard-clause shape as
    /// <c>RenewalsEndpointExtensions.GetRenewalPriorityAsync</c>: tenant header, then route-id GUID,
    /// both before any database call; a contract that does not exist, or belongs to a different
    /// tenant, both read back as 404 (<see cref="Contract360QueryService"/> cannot and does not
    /// distinguish the two, ADR-009).
    /// </summary>
    private static async Task<IResult> GetContractStrategyAsync(
        string id,
        HttpRequest request,
        Contract360QueryService contract360QueryService,
        RenewalEngine renewalEngine,
        IClock clock,
        ICallerContext callerContext,
        IBenchmarkService benchmarkService,
        BenchmarkKeyResolution benchmarkKeyResolution,
        LineItemMarketPriceService lineItemMarketPriceService,
        CancellationToken cancellationToken)
    {
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(request, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantGuid = caller.TenantId.Value;

        if (!Guid.TryParse(id, out var contractGuid))
        {
            return Results.BadRequest("The contract id in the route must be a GUID.");
        }

        var tenantId = new TenantId(tenantGuid);
        var contractEntityId = new EntityId(contractGuid);

        var contract360 = await contract360QueryService
            .GetByIdAsync(tenantId, contractEntityId, cancellationToken)
            .ConfigureAwait(false);

        if (contract360 is null)
        {
            return Results.NotFound();
        }

        var asOfDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        // One resolution per screen (ADR-024 w17 clause 7): the same BenchmarkKeyResolution
        // E21/F01 registered for the 360's benchmark member. Geography is the workspace country.
        var key = await benchmarkKeyResolution
            .ResolveAsync(tenantId, contract360.Header.SupplierId, cancellationToken)
            .ConfigureAwait(false);

        string? supplierName = null;
        string? geography = null;
        if (key is BenchmarkKeyResult.Complete complete)
        {
            supplierName = complete.Supplier;
            geography = complete.Geography;
        }

        var renewal = ComputeRenewal(contract360.Header, renewalEngine);

        // The same stored per-line comparison Contract 360's market column shows (one resolution
        // per screen, ADR-024 w17 clause 7): a line matched there is priced from it here.
        var storedMarketPrices = await lineItemMarketPriceService
            .GetCurrentAsync(tenantId, contractEntityId, cancellationToken)
            .ConfigureAwait(false);
        var pricedLines = await ToPricedLines(
                contract360, benchmarkService, supplierName, geography, asOfDate, cancellationToken, storedMarketPrices)
            .ConfigureAwait(false);
        var criticalFacts = ToCriticalFacts(contract360);

        var strategyInputs = ToStrategyInputs(
            contract360, renewal, pricedLines, criticalFacts, asOfDate, supplierName);
        var pack = StrategyPackBuilder.Build(strategyInputs);

        return Results.Ok(ToStrategyResponse(pack));
    }

    // ----- Composition / mapping (public: the bodies live in ContractInsightsMapper; these keep the
    // ----- original entry points for Ask's packs and for Raffa.Insights.Tests) -----

    /// <inheritdoc cref="ContractInsightsMapper.ToCriticalityInputs"/>
    public static ContractCriticalityInputs ToCriticalityInputs(
        Contract360Result contract,
        PriorityScoreResult priority,
        IReadOnlyDictionary<string, decimal> portfolioAnnualSpendByCurrency,
        IReadOnlyList<SavingsOpportunityResult> allSavingsOpportunities) =>
        ContractInsightsMapper.ToCriticalityInputs(contract, priority, portfolioAnnualSpendByCurrency, allSavingsOpportunities);

    /// <inheritdoc cref="ContractInsightsMapper.ComputePortfolioAnnualSpendByCurrency"/>
    public static IReadOnlyDictionary<string, decimal> ComputePortfolioAnnualSpendByCurrency(
        IReadOnlyList<Contract360Result> contracts) =>
        ContractInsightsMapper.ComputePortfolioAnnualSpendByCurrency(contracts);

    /// <inheritdoc cref="ContractInsightsMapper.ToCriticalFacts"/>
    public static IReadOnlyList<CriticalFactConfidence> ToCriticalFacts(Contract360Result contract) =>
        ContractInsightsMapper.ToCriticalFacts(contract);

    /// <inheritdoc cref="ContractInsightsMapper.ToPricedLines(Contract360Result)"/>
    public static IReadOnlyList<PricedLine> ToPricedLines(Contract360Result contract) =>
        ContractInsightsMapper.ToPricedLines(contract);

    /// <inheritdoc cref="ContractInsightsMapper.ToPricedLines(Contract360Result, IBenchmarkService, string?, string?, DateOnly, CancellationToken, IReadOnlyDictionary{EntityId, LineItemMarketPrice}?)"/>
    public static Task<IReadOnlyList<PricedLine>> ToPricedLines(
        Contract360Result contract,
        IBenchmarkService benchmarkService,
        string? supplierName,
        string? geography,
        DateOnly asOfDate,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<EntityId, LineItemMarketPrice>? storedMarketPrices = null) =>
        ContractInsightsMapper.ToPricedLines(
            contract, benchmarkService, supplierName, geography, asOfDate, cancellationToken, storedMarketPrices);

    /// <inheritdoc cref="ContractInsightsMapper.ToStrategyInputs"/>
    public static StrategyInputs ToStrategyInputs(
        Contract360Result contract,
        RenewalCalculationResult renewal,
        IReadOnlyList<PricedLine> pricedLines,
        IReadOnlyList<CriticalFactConfidence> criticalFacts,
        DateOnly asOfDate,
        string? supplierName = null) =>
        ContractInsightsMapper.ToStrategyInputs(contract, renewal, pricedLines, criticalFacts, asOfDate, supplierName);

    /// <inheritdoc cref="ContractInsightsMapper.ComputeRenewal"/>
    public static RenewalCalculationResult ComputeRenewal(Contract360Header header, RenewalEngine renewalEngine) =>
        ContractInsightsMapper.ComputeRenewal(header, renewalEngine);

    /// <inheritdoc cref="ContractInsightsMapper.ComputePriority"/>
    public static PriorityScoreResult ComputePriority(
        Contract360Header header, RenewalEngine renewalEngine, PriorityScoreCalculator priorityScoreCalculator) =>
        ContractInsightsMapper.ComputePriority(header, renewalEngine, priorityScoreCalculator);

    /// <inheritdoc cref="ContractInsightsMapper.ToCriticalityRiskSeverity"/>
    public static CriticalityRiskSeverity? ToCriticalityRiskSeverity(RiskSeverity? risk) =>
        ContractInsightsMapper.ToCriticalityRiskSeverity(risk);

    /// <inheritdoc cref="ContractInsightsMapper.ToContractRiskLevel"/>
    public static ContractRiskLevel? ToContractRiskLevel(RiskSeverity? risk) =>
        ContractInsightsMapper.ToContractRiskLevel(risk);

    // ----- Response wire-shaping -----

    private static object ToCriticalityResponse(CriticalityScore score) => new
    {
        contractId = score.ContractId.Value,
        totalScore = score.TotalScore,
        components = new
        {
            renewalUrgency = ToComponentResponse(score.RenewalUrgency),
            riskSeverity = ToComponentResponse(score.RiskSeverity),
            spendWeight = ToComponentResponse(score.SpendWeight),
            savingsPotential = ToComponentResponse(score.SavingsPotential),
            openCriticalFacts = ToComponentResponse(score.OpenCriticalFacts),
        },
    };

    private static object ToComponentResponse(CriticalityComponent component) => new
    {
        score = component.Score,
        weight = component.Weight,
        explanation = component.Explanation,
    };

    private static object ToStrategyResponse(StrategyPack pack) => new
    {
        contractId = pack.ContractId.Value,
        whenYouMustMove = new
        {
            renewalDate = pack.WhenYouMustMove.RenewalDate,
            cancellationDeadline = pack.WhenYouMustMove.CancellationDeadline,
            daysLeft = pack.WhenYouMustMove.DaysLeft,
            passedDeadline = pack.WhenYouMustMove.PassedDeadline,
            explanation = pack.WhenYouMustMove.Explanation,
        },
        whereYouCanPush = pack.WhereYouCanPush.Select(ToLeverResponse),
        targets = pack.Targets.Select(t => new
        {
            description = t.Description,
            openingTarget = t.OpeningTarget,
            acceptableRangeLow = t.AcceptableRangeLow,
            acceptableRangeHigh = t.AcceptableRangeHigh,
            walkAwayThreshold = t.WalkAwayThreshold,
            explanation = t.Explanation,
        }),
        nextSteps = pack.NextSteps.Select(s => new { label = s.Label, dueHint = s.DueHint }),
        openWeakFacts = pack.OpenWeakFacts,
    };

    private static object ToLeverResponse(ContractNegotiationLever lever) => new
    {
        leverType = lever.LeverType.ToString(),
        rationale = lever.Rationale,
        citationKeys = lever.CitationKeys,
    };
}
