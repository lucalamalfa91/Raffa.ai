using Contigo.Benchmark.Contracts;
using Contigo.Documents.Contracts.Application;
using Contigo.Documents.Contracts.Domain;
using Contigo.Insights.Contracts;
using Contigo.Insights.Criticality;
using Contigo.Insights.Strategy;
using Contigo.Renewals.Application;
using Contigo.Renewals.Domain;
using Contigo.Savings.Application;
using Contigo.SharedKernel;

namespace Contigo.Api;

/// <summary>
/// Maps <c>GET /api/insights/criticality</c> and <c>GET /api/contracts/{id}/strategy</c> (task
/// E13/F07/US01/T01, insights-calculators; parent story us-01-insights AC-5: "return the same
/// numbers the Ask pack narrates (shared builder, one source)"). <b>Not called from
/// <c>Program.cs</c> by this task</b> — F06/T01 maps it in phase 3 (this task's own coding
/// objective) — so these routes are unreachable today; every composition/mapping method below is
/// instead unit-tested directly, with hand-built fakes, from <c>Contigo.Insights.Tests</c> (this
/// task's own "Tests required" table pins "endpoint composition" there, not
/// <c>Contigo.Api.Tests</c>) — <c>Contigo.Insights.Tests.csproj</c> references this project for
/// exactly that reason.
///
/// <para>
/// Thin composition per ADR-002: every real decision (component formulas, weights, citation keys,
/// levers, next-step wording) is made by <c>Contigo.Insights.Criticality.CriticalityScoreCalculator</c>
/// / <see cref="StrategyPackBuilder"/> (both pure, unit-tested independently of this class); this
/// file only resolves the tenant, fetches tenant data through each module's own already-existing
/// query service (<see cref="PortfolioQueryService"/> / <see cref="Contract360QueryService"/> /
/// <see cref="SavingsOpportunityService"/> — Documents/Contracts and Savings; <see cref="RenewalEngine"/>
/// / <see cref="PriorityScoreCalculator"/> — Renewals), maps the result onto Insights' own DTOs (the
/// one mapping neither module may do itself — <c>Contigo.Insights</c>'s allow-list is exactly
/// <c>[SharedKernel, Benchmark]</c>; same pattern <c>RenewalsEndpointExtensions.ToCandidate</c>/
/// <c>ComputePriority</c> already use for the identical reason), and wire-shapes the response.
/// </para>
///
/// <para>
/// <b>Benchmark matching is honestly not wired for contract priced lines yet</b> (task's own coding
/// objective lists "benchmark via <c>IBenchmarkService</c>" among the composed sources): a
/// <c>Contigo.Benchmark.Contracts.BenchmarkQuery</c> requires a non-null supplier name and geography,
/// and <c>Contigo.Documents.Contracts.Domain.Contract</c> has neither field today (only a bare
/// <c>SupplierId</c> guid, no name resolver wired to this composition — Suppliers/Products is not
/// named in this task's own "Context the implementer needs" list) — the exact same gap
/// <see cref="Contract360Result.Benchmark"/>'s own doc comment already names verbatim ("no
/// supplier-name/geography field exists on <c>Contract</c> today"). <see cref="ToPricedLines"/>
/// therefore always produces <c>Benchmark: null</c> today (an honest "insufficient market data" for
/// every priced-line target — <c>PricedLineNegotiationCalculator</c>'s own abstain path), not a
/// hard-coded shortcut: once a follow-up task resolves a real supplier name + geography onto
/// <see cref="Contract360Result"/>, this method is where a real <c>IBenchmarkService.GetBenchmarkAsync</c>
/// call would be added, the same "wiring lands with the first real caller" gap this codebase
/// documents everywhere else (see <c>backend/README.md</c> "Insights").
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
        CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
            || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

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
        CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
            || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

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

        var renewal = ComputeRenewal(contract360.Header, renewalEngine);
        var pricedLines = ToPricedLines(contract360);
        var criticalFacts = ToCriticalFacts(contract360);
        var asOfDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        var strategyInputs = ToStrategyInputs(contract360, renewal, pricedLines, criticalFacts, asOfDate);
        var pack = StrategyPackBuilder.Build(strategyInputs);

        return Results.Ok(ToStrategyResponse(pack));
    }

    // ----- Composition / mapping (public: unit-tested directly with fakes, Contigo.Insights.Tests) -----

    /// <summary>
    /// Composes one <see cref="Contract360Result"/> plus its already-computed
    /// <paramref name="priority"/> into <see cref="ContractCriticalityInputs"/> — the one mapping
    /// only this composition root can do (<c>Contigo.Insights</c> cannot reference
    /// <c>Contigo.Documents.Contracts</c>/<c>Contigo.Renewals</c>/<c>Contigo.Savings</c>). 1:1 field
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
    /// This contract's priced lines, generalized from <see cref="Contract360ProductLineItem"/> into
    /// <see cref="PricedLine"/> (parent story AC-3). <see cref="PricedLine.TermMonths"/> stays null:
    /// <c>ContractLineItem.BillingPeriod</c> is free text with no normalized-months column, unlike
    /// <c>Contigo.Quotes.Domain.QuoteLine.NormalizedTermMonths</c> — a follow-up gap, not this task's
    /// to close. <see cref="PricedLine.Benchmark"/>/<see cref="PricedLine.SampleSize"/> stay null —
    /// see this type's own doc comment ("Benchmark matching is honestly not wired... yet").
    /// </summary>
    public static IReadOnlyList<PricedLine> ToPricedLines(Contract360Result contract)
    {
        ArgumentNullException.ThrowIfNull(contract);

        return contract.Products
            .Select(product => new PricedLine(
                product.Sku,
                product.Description,
                product.Quantity,
                product.UnitPrice,
                contract.Overview.Currency,
                TermMonths: null,
                Benchmark: null,
                SampleSize: null))
            .ToList();
    }

    /// <summary>
    /// Composes <see cref="Contract360Result"/> plus its already-computed <paramref name="renewal"/>
    /// into <see cref="StrategyInputs"/>. <see cref="StrategyInputs.SupplierName"/> stays null — see
    /// this type's own doc comment ("Contract.SupplierId is a bare id; no name resolver is wired to
    /// this composition yet").
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
        DateOnly asOfDate)
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
            SupplierName: null,
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
    /// copy" shape this host already accepts, mirrors how <c>Contigo.Quotes</c>/<c>Contigo.Savings</c>
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
    /// <see langword="null"/>) — only <c>Contigo.Api</c> may perform this mapping (ADR-002).</summary>
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
