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

    // ----- Composition / mapping (public: unit-tested directly with fakes, Raffa.Insights.Tests) -----

    /// <summary>
    /// Composes one <see cref="Contract360Result"/> plus its already-computed
    /// <paramref name="priority"/> into <see cref="ContractCriticalityInputs"/> — the one mapping
    /// only this composition root can do (<c>Raffa.Insights</c> cannot reference
    /// <c>Raffa.Documents.Contracts</c>/<c>Raffa.Renewals</c>/<c>Raffa.Savings</c>). 1:1 field
    /// copy plus the savings-opportunity aggregation below; no scoring decision is made here (that
    /// is <see cref="CriticalityScoreCalculator"/>'s own job).
    /// </summary>
    public static ContractCriticalityInputs ToCriticalityInputs(
        Contract360Result contract,
        PriorityScoreResult priority,
        IReadOnlyDictionary<string, decimal> portfolioAnnualSpendByCurrency,
        IReadOnlyList<SavingsOpportunityResult> allSavingsOpportunities)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(priority);
        ArgumentNullException.ThrowIfNull(portfolioAnnualSpendByCurrency);
        ArgumentNullException.ThrowIfNull(allSavingsOpportunities);

        // AboveBandLineFraction stays null — see this type's own doc comment ("Benchmark matching
        // is honestly not wired for contract priced lines yet"); the calculator falls back to the
        // savings-opportunity range below.
        var contractSavings = allSavingsOpportunities.Where(o => o.ContractId == contract.ContractId).ToList();
        var savingsPotential = contractSavings.Count == 0
            ? new SavingsPotentialInputs(AboveBandLineFraction: null, SavingsOpportunityRangeLow: null, SavingsOpportunityRangeHigh: null)
            : new SavingsPotentialInputs(
                AboveBandLineFraction: null,
                SavingsOpportunityRangeLow: contractSavings.Sum(o => o.EstimatedSavingsLow),
                SavingsOpportunityRangeHigh: contractSavings.Sum(o => o.EstimatedSavingsHigh));

        var portfolioAnnualSpend = portfolioAnnualSpendByCurrency.GetValueOrDefault(contract.Overview.Currency, 0m);

        return new ContractCriticalityInputs(
            contract.ContractId,
            new RenewalUrgencyInputs(priority.TotalScore),
            ToCriticalityRiskSeverity(contract.Header.Risk),
            contract.Header.AnnualSpend,
            portfolioAnnualSpend,
            savingsPotential,
            ToCriticalFacts(contract));
    }

    /// <summary>
    /// This tenant's total annual spend, grouped by currency (never summed across currencies — no
    /// FX-conversion service exists anywhere in this codebase, the same rule
    /// <c>PortfolioAnalysisCalculator.Summarize</c>/<c>SavingsKpiCalculator</c> already enforce for
    /// the identical reason) — the spend-weight component's per-currency denominator.
    /// </summary>
    public static IReadOnlyDictionary<string, decimal> ComputePortfolioAnnualSpendByCurrency(
        IReadOnlyList<Contract360Result> contracts)
    {
        ArgumentNullException.ThrowIfNull(contracts);

        return contracts
            .Where(c => c.Header.AnnualSpend is not null)
            .GroupBy(c => c.Overview.Currency, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(c => c.Header.AnnualSpend!.Value),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// This contract's tracked critical facts — recorded risks (a risk is inherently critical) plus
    /// priced lines that carry a unit price (the commercial facts a negotiation actually leans on) —
    /// each keyed by <see cref="CriticalFactConfidence.FieldKey"/> and carrying its own recorded
    /// <see cref="Contract360Risk.Confidence"/>/<see cref="Contract360ProductLineItem.Confidence"/>
    /// (Appendix C rule 2). A fact with no recorded confidence is omitted, not assumed strong or weak
    /// (Appendix C rule 10) — <see cref="CriticalityScoreCalculator"/>'s own "no critical facts
    /// tracked" default then applies honestly if every candidate fact lacks one.
    /// </summary>
    public static IReadOnlyList<CriticalFactConfidence> ToCriticalFacts(Contract360Result contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        var facts = new List<CriticalFactConfidence>();

        for (var i = 0; i < contract.Risks.Count; i++)
        {
            if (contract.Risks[i].Confidence is { } confidence)
            {
                facts.Add(new CriticalFactConfidence($"risk[{i}]", confidence));
            }
        }

        for (var i = 0; i < contract.Products.Count; i++)
        {
            var product = contract.Products[i];
            if (product.UnitPrice is not null && product.Confidence is { } confidence)
            {
                facts.Add(new CriticalFactConfidence($"priced-line[{i}].unitPrice", confidence));
            }
        }

        return facts;
    }

    /// <summary>
    /// This contract's priced lines without a benchmark lookup — used by callers that compose their
    /// own adapter call (Ask's market-compare pack). <see cref="PricedLine.TermMonths"/> is the
    /// contract's own <see cref="Contract360Overview.RenewalTermMonths"/> when recorded;
    /// <c>ContractLineItem.BillingPeriod</c> stays free text. The band itself is left unset so the
    /// pack states "insufficient market data" rather than fabricating one.
    /// </summary>
    public static IReadOnlyList<PricedLine> ToPricedLines(Contract360Result contract) =>
        MapPricedLines(contract, static _ => default);

    /// <summary>
    /// This contract's priced lines, generalized from <see cref="Contract360ProductLineItem"/> into
    /// <see cref="PricedLine"/> (parent story AC-3; task E21/F03/US01/T01). When
    /// <paramref name="supplierName"/> and <paramref name="geography"/> are both present — the
    /// complete key <see cref="BenchmarkKeyResolution"/> resolved in the host — calls
    /// <see cref="IBenchmarkService.GetBenchmarkAsync"/> per line and fills the distribution, term,
    /// sample size, adapter name and as-of date from a sufficient result. An incomplete key or an
    /// adapter abstention leaves the band unset so the pack states "insufficient market data"
    /// (ADR-001 w17 clause 4). Never fabricates a number.
    ///
    /// <para>
    /// <paramref name="storedMarketPrices"/> — each line's stored market comparison
    /// (<see cref="LineItemMarketPriceService"/>, the one Contract 360's market column shows) —
    /// wins for a line it matched, so the strategy and the product table never disagree about the
    /// same line; the benchmark call above remains the fallback for every other line.
    /// </para>
    /// </summary>
    public static async Task<IReadOnlyList<PricedLine>> ToPricedLines(
        Contract360Result contract,
        IBenchmarkService benchmarkService,
        string? supplierName,
        string? geography,
        DateOnly asOfDate,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<EntityId, LineItemMarketPrice>? storedMarketPrices = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(benchmarkService);

        var keyIsComplete = !string.IsNullOrWhiteSpace(supplierName)
            && !string.IsNullOrWhiteSpace(geography);
        var bands = new LineBenchmark[contract.Products.Count];

        for (var i = 0; i < contract.Products.Count; i++)
        {
            var product = contract.Products[i];
            if (storedMarketPrices is not null
                && storedMarketPrices.TryGetValue(product.LineItemId, out var stored)
                && stored is { Matched: true, UnitPriceP25: { } p25, UnitPriceP50: { } p50, UnitPriceP75: { } p75 })
            {
                bands[i] = new LineBenchmark(
                    new BenchmarkDistribution(p25, p50, p75), stored.SampleSize, StoredMarketSource, stored.MarketUpdatedAt);
                continue;
            }

            if (keyIsComplete)
            {
                var termMonths = contract.Overview.RenewalTermMonths;
                var query = new BenchmarkQuery(
                    Supplier: supplierName!,
                    Product: product.Description,
                    Sku: product.Sku,
                    Geography: geography!,
                    Quantity: product.Quantity ?? 1m,
                    Term: termMonths is { } months ? $"{months} months" : "unknown",
                    Currency: contract.Overview.Currency,
                    PurchaseDate: contract.Overview.EffectiveDate ?? asOfDate);

                var benchmarkResult = await benchmarkService
                    .GetBenchmarkAsync(query, cancellationToken)
                    .ConfigureAwait(false);

                if (benchmarkResult.IsSuccess && benchmarkResult.Value.HasSufficientData)
                {
                    var value = benchmarkResult.Value;
                    bands[i] = new LineBenchmark(
                        value.Distribution, value.SampleSize, value.Source, value.UpdatedAt);
                }
            }
        }

        return MapPricedLines(contract, i => bands[i]);
    }

    /// <summary>The adapter label a stored comparison carries into the strategy pack — the same
    /// corpus, and the same wording, <c>MarketFeedBenchmarkAdapter</c> reports as its source.</summary>
    private const string StoredMarketSource = "market-feed (representative, mock)";

    private readonly record struct LineBenchmark(
        BenchmarkDistribution? Distribution,
        int? SampleSize,
        string? AdapterName,
        DateTimeOffset? AsOf);

    private static IReadOnlyList<PricedLine> MapPricedLines(
        Contract360Result contract,
        Func<int, LineBenchmark> resolveBand)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(resolveBand);

        var termMonths = contract.Overview.RenewalTermMonths;
        var lines = new List<PricedLine>(contract.Products.Count);
        for (var i = 0; i < contract.Products.Count; i++)
        {
            var product = contract.Products[i];
            var band = resolveBand(i);
            lines.Add(new PricedLine(
                product.Sku,
                product.Description,
                product.Quantity,
                product.UnitPrice,
                contract.Overview.Currency,
                termMonths,
                band.Distribution,
                band.SampleSize,
                band.AdapterName,
                band.AsOf));
        }

        return lines;
    }

    /// <summary>
    /// Composes <see cref="Contract360Result"/> plus its already-computed <paramref name="renewal"/>
    /// into <see cref="StrategyInputs"/>. <paramref name="supplierName"/> is the host-resolved half
    /// of the (supplier name, geography) pair <see cref="BenchmarkKeyResolution"/> produced — the
    /// module cannot look the name up itself (allow-list <c>[SharedKernel, Benchmark]</c>). Null
    /// when the key was incomplete; the builder then falls back to generic phrasing.
    ///
    /// <para>
    /// <b>The cancellation deadline comes from two places, in order.</b> The renewal engine derives
    /// one from <c>EndDate</c> minus the contract's notice period; nothing persists a notice period
    /// today (<see cref="ContractRenewalTerms.CancellationNoticeDays"/> is always null here), so
    /// that derivation is always empty and the strategy pack used to open with "when you must move"
    /// and no deadline at all — on the one question the requirements themselves use as the worked
    /// example. The extracted <c>CancellationDeadline</c> on the contract header is a real, cited
    /// fact a human can correct; it is used whenever the engine has nothing, with the days-left
    /// count derived from <paramref name="asOfDate"/> the same way the engine would. Found by the
    /// golden set (task E13/F06/US01/T02, GAP-ASK-STRATEGY-NO-NOTICE-DEADLINE).
    /// </para>
    /// </summary>
    public static StrategyInputs ToStrategyInputs(
        Contract360Result contract,
        RenewalCalculationResult renewal,
        IReadOnlyList<PricedLine> pricedLines,
        IReadOnlyList<CriticalFactConfidence> criticalFacts,
        DateOnly asOfDate,
        string? supplierName = null)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(renewal);
        ArgumentNullException.ThrowIfNull(pricedLines);
        ArgumentNullException.ThrowIfNull(criticalFacts);

        var cancellationDeadline = renewal.CancellationDeadline ?? contract.Header.CancellationDeadline;
        var daysUntilCancellationDeadline = renewal.DaysUntilCancellationDeadline
            ?? (cancellationDeadline is { } deadline ? deadline.DayNumber - asOfDate.DayNumber : null);

        return new StrategyInputs(
            contract.ContractId,
            supplierName,
            renewal.RenewalDate,
            cancellationDeadline,
            renewal.DaysUntilRenewal,
            daysUntilCancellationDeadline,
            contract.Header.AutoRenewal,
            pricedLines,
            criticalFacts,
            asOfDate);
    }

    /// <summary>
    /// Runs <see cref="RenewalEngine.Calculate"/> for one contract — the same
    /// <c>Contract360Header</c> → <c>ContractRenewalTerms</c> mapping
    /// <c>RenewalsEndpointExtensions.ComputePriority</c> already performs (duplicated here, not
    /// referenced: that method is private to its own file — same "each composition file owns its own
    /// copy" shape this host already accepts, mirrors how <c>Raffa.Quotes</c>/<c>Raffa.Savings</c>
    /// duplicate a formula rather than cross-reference when architecture forbids the reference).
    /// </summary>
    public static RenewalCalculationResult ComputeRenewal(Contract360Header header, RenewalEngine renewalEngine)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(renewalEngine);

        var terms = new ContractRenewalTerms(
            header.ContractId, header.EndDate, header.AutoRenewal, CancellationNoticeDays: null);
        return renewalEngine.Calculate(terms);
    }

    /// <summary>Runs <see cref="RenewalEngine.Calculate"/> then
    /// <see cref="PriorityScoreCalculator.Calculate"/> for one contract — see
    /// <see cref="ComputeRenewal"/>'s own doc comment for why this is its own copy of
    /// <c>RenewalsEndpointExtensions.ComputePriority</c>'s identical composition.</summary>
    public static PriorityScoreResult ComputePriority(
        Contract360Header header, RenewalEngine renewalEngine, PriorityScoreCalculator priorityScoreCalculator)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(priorityScoreCalculator);

        var renewal = ComputeRenewal(header, renewalEngine);
        var inputs = new RenewalPriorityInputs(
            header.AnnualSpend,
            AnnualUpliftPercent: null,
            ToContractRiskLevel(header.Risk),
            BenchmarkMarketPositionPercent: null);

        return priorityScoreCalculator.Calculate(renewal, inputs);
    }

    /// <summary>Maps Documents/Contracts' <see cref="RiskSeverity"/> onto Insights' own
    /// <see cref="CriticalityRiskSeverity"/>, 1:1 by name plus an explicit
    /// <see cref="CriticalityRiskSeverity.None"/> for "not assessed" (<paramref name="risk"/>
    /// <see langword="null"/>) — only <c>Raffa.Api</c> may perform this mapping (ADR-002).</summary>
    public static CriticalityRiskSeverity? ToCriticalityRiskSeverity(RiskSeverity? risk) => risk switch
    {
        null => null,
        RiskSeverity.Low => CriticalityRiskSeverity.Low,
        RiskSeverity.Medium => CriticalityRiskSeverity.Medium,
        RiskSeverity.High => CriticalityRiskSeverity.High,
        RiskSeverity.Critical => CriticalityRiskSeverity.Critical,
        _ => throw new ArgumentOutOfRangeException(nameof(risk), risk, "Unknown RiskSeverity."),
    };

    /// <summary>Maps Documents/Contracts' <see cref="RiskSeverity"/> onto Renewals' own
    /// <see cref="ContractRiskLevel"/> — the identical mapping
    /// <c>RenewalsEndpointExtensions.MapRiskLevel</c> already performs (duplicated, not referenced:
    /// that method is private to its own file).</summary>
    public static ContractRiskLevel? ToContractRiskLevel(RiskSeverity? risk) => risk switch
    {
        null => null,
        RiskSeverity.Low => ContractRiskLevel.Low,
        RiskSeverity.Medium => ContractRiskLevel.Medium,
        RiskSeverity.High => ContractRiskLevel.High,
        RiskSeverity.Critical => ContractRiskLevel.Critical,
        _ => throw new ArgumentOutOfRangeException(nameof(risk), risk, "Unknown RiskSeverity."),
    };

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
