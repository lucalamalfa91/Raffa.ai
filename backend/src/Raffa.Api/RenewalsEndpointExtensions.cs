using Raffa.Api.Infrastructure;
using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Renewals.Application;
using Raffa.Renewals.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Api;

/// <summary>
/// Maps `GET /api/renewals` (product spec Appendix A "Renewal pipeline"; §9.1/§9.3/§10.1; story
/// us-01-renewal-dashboard-api AC-1/AC-2, task E03/F03/US01/T01) and
/// `GET /api/renewals/{contractId}/priority` (story us-02-priority-score AC-1/AC-2, task
/// E03/F01/US02/T02, the wave-spec's `renewal-priority-explain` artifact — the "explainability
/// query" half of that task's own title). Thin composition per ADR-002:
/// <see cref="PortfolioQueryService"/> (Documents/Contracts) already reads the tenant-scoped
/// contract facts an "actionable renewal pipeline" needs (supplier id, annual spend, end date,
/// auto-renewal, the already-extracted cancellation-deadline fact — same rows `GET /api/contracts`
/// returns); <see cref="RenewalPipelineBuilder"/> (Renewals) turns those into a pipeline row plus a
/// facts/recommendations insight card via the deterministic <c>RenewalEngine</c> (task
/// E03/F01/US01/T01, the `renewal-engine` wave-spec artifact this task depends on). Neither module
/// may reference the other (`Raffa.ArchitectureTests.DependencyDirectionTests`'s allow-list for
/// both is exactly `[SharedKernel, AiGateway|Benchmark]`), so mapping <see cref="PortfolioListItem"/>
/// into <see cref="RenewalDashboardCandidate"/> can only happen here, in `Raffa.Api` — "the one
/// project allowed to reference every module" (`backend/README.md` "Dependency direction") — the
/// same pattern <c>ChatEndpointExtensions</c> already uses for <c>EmbeddingSearchResult</c> -&gt;
/// <c>AiEvidenceSnippet</c>.
///
/// <para>
/// `GET /api/renewals/{contractId}/priority` reuses <see cref="Contract360QueryService"/>
/// (already the tenant-scoped, 404-on-missing-or-wrong-tenant single-contract lookup
/// `Raffa.Api.ContractsEndpointExtensions` uses for `GET /api/contracts/{id}`) instead of the
/// portfolio's paged list — one contract, not a page. <see cref="MapRiskLevel"/> below is the
/// composition <see cref="ContractRiskLevel"/>'s own doc comment named as an open gap ("a
/// composition root maps `PortfolioListItem.Risk`... onto this enum 1:1; no task in this wave
/// wires that composition yet") — <see cref="Contract360Header.Risk"/> is computed the same way
/// <c>PortfolioListItem.Risk</c> is, so the same mapping applies. <c>AnnualUpliftPercent</c> and
/// <c>BenchmarkMarketPositionPercent</c> stay honestly <see langword="null"/> — neither has a real
/// producer yet (Benchmark Service is still an R0 placeholder, and no task has added an uplift
/// column/extraction field to <c>Contract</c>), the same gap
/// <see cref="RenewalInsightRecommendations"/>'s own doc comment already documents for the sibling
/// `GET /api/renewals` response — <see cref="Raffa.Renewals.Application.PriorityScoreCalculator"/>
/// itself already handles an unknown input honestly (Appendix C rule 10: the minimum for uplift,
/// the documented neutral midpoint for benchmark position, parent story AC-3), so this composition
/// does not need its own special case for either.
/// </para>
///
/// Same interim `X-Tenant-Id` header placeholder as every other endpoint in this host
/// (<c>Program.cs</c>'s document endpoints, <see cref="WorkspaceEndpointExtensions"/>,
/// <see cref="PortfolioEndpointExtensions"/>, <see cref="ContractsEndpointExtensions"/>,
/// <see cref="ChatEndpointExtensions"/>): ADR-010 (Entra ID/OIDC) is not in this task's
/// "Architecture decisions in force" list, so there is no validated caller principal yet — see
/// <c>Program.cs</c>'s own comment on why this interim gap is not promoted to
/// reports/open-questions.md by this task.
///
/// Only auto-renewing contracts have a renewal to act on at all (the same "Renewal" derivation
/// rule <see cref="PortfolioListItem.RenewalDate"/>'s own doc comment already states), so the
/// underlying <see cref="PortfolioQueryService.GetPortfolioAsync"/> call below is filtered to
/// <c>AutoRenewal: true</c> — pushed to SQL rather than fetched then discarded. That call reuses
/// <see cref="PortfolioQueryService"/>'s own page-size cap
/// (<see cref="PortfolioPageRequest.MaxPageSize"/> = 100) instead of a dedicated unpaged query: an
/// interim limitation, honestly surfaced via the response's <c>totalCount</c> (a tenant with more
/// than 100 auto-renewing contracts sees only the 100 most recently created ones, and
/// <c>totalCount</c> lets a caller detect that rather than silently trusting an incomplete list —
/// Appendix C rule 10). A dedicated unpaged renewal-candidates query is a follow-up, not attempted
/// by this task.
///
/// Task E03/F03/US01/T02 (renewal-action) adds `POST /api/renewals/{id}/action` to this same file
/// (AC-3: "updates owner/status/action") — see <see cref="PostRenewalActionAsync"/> below. Same
/// thin-composition shape and the same interim `X-Tenant-Id` placeholder as the GET handler above;
/// unlike the GET handler, this one does not compose across modules (<see cref="RenewalActionService"/>
/// is the whole implementation) because it never needs to read <c>Raffa.Documents.Contracts</c> —
/// see <see cref="RenewalActionService"/>'s own doc comment for the honest gap that leaves (no
/// check that the route's <c>{id}</c> names an existing, tenant-owned contract).
///
/// <para>
/// Task E13/F03/US01/T02 (requirements R-SUP-04, ADR-024 "never a bare SupplierId guid"): every
/// `GET /api/renewals` row — and its nested §9.3 insight card, which a user reads as prose — gains
/// <c>supplierName</c>. Resolved from the very same <see cref="PortfolioQueryService"/> page this
/// endpoint already builds its candidates from, via
/// <see cref="PortfolioEndpointExtensions.ResolveSupplierNamesAsync"/> (one batched
/// <see cref="ISupplierNameLookup"/> call for the whole page, shared with the portfolio endpoint
/// rather than reimplemented). <see cref="RenewalPipelineItem"/> itself is untouched: Renewals may
/// not reference the Suppliers module either (its allow-list is <c>[SharedKernel, Benchmark]</c>),
/// so the join belongs in this composition root, exactly like <see cref="ToCandidate"/>'s own
/// mapping.
/// </para>
///
/// <para>
/// Task E19/F01/US01/T01 (renewal-action-api, ADR-028 §D1) adds <c>GET /api/renewals/{id}/action</c>
/// (<see cref="GetRenewalActionAsync"/>) — the same <c>{id}</c>/contract-id meaning
/// <see cref="PostRenewalActionAsync"/> already keys on, confirmed against that handler before this
/// route was written — plus the persisted row embedded in every `GET /api/renewals` item under
/// <c>savedAction</c>, resolved for the whole page in one call
/// (<see cref="RenewalActionService.GetActionsAsync"/>) rather than a per-row GET: Renewals and
/// Savings are list surfaces, so a per-row call would be an N+1 across the portfolio. `savedAction`
/// sits beside the pre-existing, unchanged <c>action</c> (the calculator's own
/// <c>RecommendedAction</c>) — reusing that name would overwrite a deterministic calculator's output
/// with user state on a shipped screen, so the embedded field is deliberately named differently.
/// </para>
/// </summary>
public static class RenewalsEndpointExtensions
{
    public static IEndpointRouteBuilder MapRenewalsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/renewals", GetRenewalsAsync);
        endpoints.MapGet("/api/renewals/{contractId}/priority", GetRenewalPriorityAsync);
        endpoints.MapPost("/api/renewals/{id}/action", PostRenewalActionAsync);
        endpoints.MapGet("/api/renewals/{id}/action", GetRenewalActionAsync);
        return endpoints;
    }

    private static async Task<IResult> GetRenewalsAsync(
        HttpRequest request,
        PortfolioQueryService portfolioQueryService,
        RenewalPipelineBuilder pipelineBuilder,
        ISupplierNameLookup supplierNameLookup,
        RenewalActionService actionService,
        IBenchmarkService benchmarkService,
        BenchmarkKeyResolution benchmarkKeyResolution,
        IClock clock,
        ITenantContext tenantContext,
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
            new PortfolioFilter(AutoRenewal: true),
            new PortfolioPageRequest(Page: 1, PageSize: PortfolioPageRequest.MaxPageSize),
            cancellationToken).ConfigureAwait(false);

        var supplierNames = await PortfolioEndpointExtensions
            .ResolveSupplierNamesAsync(tenantId, portfolioPage.Items, supplierNameLookup, tenantContext, cancellationToken)
            .ConfigureAwait(false);

        // Task E21/F02/US01/T01 (NW-22): resolve the market band per candidate here in the host —
        // the one project allowed to reference every module — so RenewalPipelineBuilder stays pure
        // (ADR-002 w17 clause 2). BenchmarkKeyResolution resolved the (supplier, geography) key in
        // phase 1 (E21/F01/US01/T01); here we call IBenchmarkService and determine the position
        // from the distribution. Abstentions (incomplete key, thin sample, no spend) produce a null
        // band that the builder maps to "insufficient market data" (ADR-001 w17 clause 4).
        var candidatesWithBands = new List<RenewalDashboardCandidate>(portfolioPage.Items.Count);
        foreach (var item in portfolioPage.Items)
        {
            var band = await ResolveMarketBandAsync(
                tenantId, item, benchmarkService, benchmarkKeyResolution, clock, cancellationToken)
                .ConfigureAwait(false);
            candidatesWithBands.Add(ToCandidate(item, band));
        }

        var pipeline = pipelineBuilder.Build(candidatesWithBands);

        // ADR-028 §D1 (task E19/F01/US01/T01): the persisted renewal action embedded under
        // `savedAction` on every row, resolved once for the whole page's contract ids -- a per-row
        // GET would be an N+1 across the portfolio (RenewalActionService.GetActionsAsync's own doc
        // comment).
        var savedActions = await actionService.GetActionsAsync(
            tenantId, pipeline.Select(item => item.ContractId).ToList(), cancellationToken).ConfigureAwait(false);

        return Results.Ok(new
        {
            items = pipeline.Select(item => ToPipelineResponse(item, supplierNames, savedActions)),
            totalCount = portfolioPage.TotalCount,
        });
    }

    /// <summary>
    /// `GET /api/renewals/{contractId}/priority` (task E03/F01/US02/T02, parent story
    /// us-02-priority-score AC-1/AC-2) — the explainable priority-score breakdown for one
    /// tenant-scoped contract. Same guard-clause shape as
    /// <c>Raffa.Api.ContractsEndpointExtensions.GetContract360Async</c> (tenant header, then
    /// route-id GUID, both before any database call); a contract that does not exist, or belongs
    /// to a different tenant than the caller's `X-Tenant-Id`, both read back as 404 —
    /// <see cref="Contract360QueryService"/> cannot and does not distinguish the two (ADR-009).
    /// </summary>
    private static async Task<IResult> GetRenewalPriorityAsync(
        string contractId,
        HttpRequest request,
        Contract360QueryService contract360QueryService,
        RenewalEngine renewalEngine,
        PriorityScoreCalculator priorityScoreCalculator,
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

        if (!Guid.TryParse(contractId, out var contractGuid))
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

        var priority = ComputePriority(contract360.Header, renewalEngine, priorityScoreCalculator);

        return Results.Ok(ToPriorityResponse(priority));
    }

    /// <summary>
    /// Maps one tenant-scoped portfolio row to the Renewals module's own input shape — the one
    /// mapping only this composition root can do (see the type doc comment). 1:1 field copy; no
    /// decision is made here. <paramref name="band"/> is the result of <see cref="ResolveMarketBandAsync"/>
    /// called just before this in <see cref="GetRenewalsAsync"/>; null means the adapter abstained.
    /// </summary>
    private static RenewalDashboardCandidate ToCandidate(PortfolioListItem item, ResolvedMarketBand? band = null) =>
        new(
            new EntityId(item.ContractId),
            item.SupplierId is { } supplierId ? new EntityId(supplierId) : null,
            item.EndDate,
            item.AutoRenewal,
            item.AnnualSpend,
            item.CancellationDeadline,
            band);

    /// <summary>
    /// Resolves the market position band for one portfolio row (task E21/F02/US01/T01, NW-22).
    ///
    /// <list type="number">
    /// <item>Calls <see cref="BenchmarkKeyResolution.ResolveAsync"/> to obtain the
    ///   (supplier name, geography) key — the two dimensions a <c>BenchmarkQuery</c> requires
    ///   that only the host can supply (phase 1, E21/F01/US01/T01). An incomplete key → null.</item>
    /// <item>Calls <see cref="IBenchmarkService.GetBenchmarkAsync"/> with a query composed from
    ///   the resolved key plus the portfolio item's own currency and today's date as the purchase
    ///   date. A service failure or an adapter abstention (too few comparables) → null.</item>
    /// <item>Compares <see cref="PortfolioListItem.AnnualSpend"/> to the P25/P75 band
    ///   (at-or-below P25 = <c>"below market"</c>, at-or-above P75 = <c>"above market"</c>,
    ///   otherwise <c>"in line with market"</c>). No annual spend or inverted markers → null.</item>
    /// </list>
    ///
    /// A null result is not a defect — the builder maps it to <c>"insufficient market data"</c>
    /// (ADR-001 w17 clause 4, AC-3). Never throws: all failure modes are returned as null so the
    /// loop in <see cref="GetRenewalsAsync"/> keeps processing the remaining candidates.
    /// </summary>
    private static async Task<ResolvedMarketBand?> ResolveMarketBandAsync(
        TenantId tenantId,
        PortfolioListItem item,
        IBenchmarkService benchmarkService,
        BenchmarkKeyResolution benchmarkKeyResolution,
        IClock clock,
        CancellationToken cancellationToken)
    {
        var keyResult = await benchmarkKeyResolution
            .ResolveAsync(
                tenantId,
                item.SupplierId is { } sid ? new EntityId(sid) : null,
                cancellationToken)
            .ConfigureAwait(false);

        if (keyResult is not BenchmarkKeyResult.Complete key)
            return null; // incomplete key (no supplier name or no workspace country) → abstain

        var query = new BenchmarkQuery(
            Supplier: key.Supplier,
            // PortfolioListItem has no product name; contract type is the closest identifying
            // fact on the row (PortfolioListItem's own "Contract" column proxy). An unmatched
            // product is an adapter abstention, not a fabricated catalog name.
            Product: item.Type.ToString(),
            Sku: null,
            Geography: key.Geography,
            Quantity: 1m,
            // PortfolioListItem does not carry RenewalTermMonths; "unknown" lets the adapter skip
            // term-gating rather than inventing "12 months" (Appendix C rule 10).
            Term: "unknown",
            Currency: item.Currency,
            PurchaseDate: DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));

        var benchmarkResult = await benchmarkService
            .GetBenchmarkAsync(query, cancellationToken)
            .ConfigureAwait(false);

        if (benchmarkResult.IsFailure || !benchmarkResult.Value.HasSufficientData)
            return null; // adapter abstained (thin sample or service error) → abstain

        var position = DetermineMarketPosition(item.AnnualSpend, benchmarkResult.Value.Distribution!);
        if (position is null)
            return null; // no spend to compare → abstain

        return new ResolvedMarketBand(
            position,
            benchmarkResult.Value.Source,
            benchmarkResult.Value.SampleSize,
            benchmarkResult.Value.UpdatedAt);
    }

    /// <summary>
    /// Classifies <paramref name="annualSpend"/> relative to the P25/P75 band — the same
    /// <c>[P25, P75]</c> rule <c>Raffa.Quotes.Application.Assessment.MarketAssessmentCalculator</c>
    /// already uses: at-or-below P25 → <c>"below market"</c>, at-or-above P75 →
    /// <c>"above market"</c>, anything else (including P50) → <c>"in line with market"</c>.
    /// Returns <see langword="null"/> when spend is unknown or the markers are not well-ordered
    /// — never a fabricated position (Appendix C rule 10; ADR-001 w17 clause 4).
    /// </summary>
    private static string? DetermineMarketPosition(decimal? annualSpend, BenchmarkDistribution dist)
    {
        if (annualSpend is not { } spend)
            return null;

        if (!(dist.P25 <= dist.P50 && dist.P50 <= dist.P75))
            return null;

        if (spend <= dist.P25) return "below market";
        if (spend >= dist.P75) return "above market";
        return "in line with market";
    }

    /// <summary>
    /// Wire-shapes <see cref="RenewalPipelineItem"/> per AC-1 (top-level supplier/renewal/days/
    /// spend/deadline/action columns) and AC-2 (nested <c>insightCard.facts</c> /
    /// <c>insightCard.recommendations</c>, spec §9.3). Enum members and <c>EntityId</c>/
    /// <c>EntityId?</c> wrapper values are projected to plain strings/GUIDs — the same convention
    /// <see cref="PortfolioEndpointExtensions"/> and <see cref="ContractsEndpointExtensions"/>
    /// already use. Task E19/F01/US01/T01 (ADR-028 §D1) adds <c>savedAction</c> beside the
    /// pre-existing, unchanged <c>action</c> — <see langword="null"/> when nothing was ever recorded
    /// for this contract (never a default/placeholder object), otherwise the same shape
    /// <see cref="GetRenewalActionAsync"/>/<see cref="PostRenewalActionAsync"/> return
    /// (<see cref="ToActionResponse"/>).
    /// </summary>
    private static object ToPipelineResponse(
        RenewalPipelineItem item,
        IReadOnlyDictionary<EntityId, string> supplierNames,
        IReadOnlyDictionary<EntityId, RenewalActionResult> savedActions)
    {
        var facts = item.InsightCard.Facts;
        var recommendations = item.InsightCard.Recommendations;
        var supplierName = PortfolioEndpointExtensions.LookupSupplierName(supplierNames, item.SupplierId?.Value);
        var savedAction = savedActions.TryGetValue(item.ContractId, out var saved) ? ToActionResponse(saved) : null;

        return new
        {
            contractId = item.ContractId.Value,
            supplierId = item.SupplierId?.Value,
            supplierName,
            status = item.Status.ToString(),
            renewalDate = item.RenewalDate,
            daysUntilRenewal = item.DaysUntilRenewal,
            annualSpend = item.AnnualSpend,
            cancellationDeadline = item.CancellationDeadline,
            daysUntilCancellationDeadline = item.DaysUntilCancellationDeadline,
            autoRenewal = item.AutoRenewal,
            action = recommendations.RecommendedAction,
            savedAction,
            insightCard = new
            {
                facts = new
                {
                    supplierId = facts.SupplierId?.Value,
                    supplierName = PortfolioEndpointExtensions.LookupSupplierName(supplierNames, facts.SupplierId?.Value),
                    renewalDate = facts.RenewalDate,
                    daysUntilRenewal = facts.DaysUntilRenewal,
                    annualSpend = facts.AnnualSpend,
                    cancellationDeadline = facts.CancellationDeadline,
                    daysUntilCancellationDeadline = facts.DaysUntilCancellationDeadline,
                },
                recommendations = new
                {
                    recommendedAction = recommendations.RecommendedAction,
                    explanation = recommendations.Explanation,
                    annualUpliftPercent = recommendations.AnnualUpliftPercent,
                    marketPosition = recommendations.MarketPosition,
                    potentialSavingsRange = recommendations.PotentialSavingsRange,
                },
            },
        };
    }

    /// <summary>
    /// Composes <see cref="Contract360Header"/> (Documents/Contracts) into the two small DTOs
    /// <see cref="RenewalEngine"/>/<see cref="PriorityScoreCalculator"/> actually accept
    /// (<see cref="ContractRenewalTerms"/>, <see cref="RenewalPriorityInputs"/>) and runs both —
    /// the same "map here, in the one project allowed to reference every module" pattern
    /// <see cref="ToCandidate"/> already uses for the dashboard endpoint. <c>CancellationNoticeDays</c>
    /// is deliberately null (same gap <c>Raffa.Renewals.Application.ContractRenewalTerms</c>'s
    /// own doc comment documents: <c>Contract</c> has no persisted column for it yet) — this
    /// endpoint only needs the priority score, not a cancellation deadline.
    /// </summary>
    private static PriorityScoreResult ComputePriority(
        Contract360Header header, RenewalEngine renewalEngine, PriorityScoreCalculator priorityScoreCalculator)
    {
        var terms = new ContractRenewalTerms(
            header.ContractId, header.EndDate, header.AutoRenewal, CancellationNoticeDays: null);
        var calculation = renewalEngine.Calculate(terms);

        var inputs = new RenewalPriorityInputs(
            header.AnnualSpend,
            AnnualUpliftPercent: null,
            MapRiskLevel(header.Risk),
            BenchmarkMarketPositionPercent: null);

        return priorityScoreCalculator.Calculate(calculation, inputs);
    }

    /// <summary>
    /// Maps Documents/Contracts' <see cref="RiskSeverity"/> onto the Renewals module's own
    /// <see cref="ContractRiskLevel"/>, 1:1 by name (both name exactly Low/Medium/High/Critical) —
    /// the composition <see cref="ContractRiskLevel"/>'s own doc comment named as an open gap ("a
    /// composition root maps `PortfolioListItem.Risk`... onto this enum 1:1; no task in this wave
    /// wires that composition yet"). Only <c>Raffa.Api</c> may perform this mapping: ADR-002
    /// forbids <c>Raffa.Renewals</c> from referencing <c>Raffa.Documents.Contracts</c> at all
    /// (`Raffa.ArchitectureTests.DependencyDirectionTests`'s allow-list for that module is
    /// exactly `[SharedKernel, Benchmark]`).
    /// </summary>
    private static ContractRiskLevel? MapRiskLevel(RiskSeverity? risk) => risk switch
    {
        null => null,
        RiskSeverity.Low => ContractRiskLevel.Low,
        RiskSeverity.Medium => ContractRiskLevel.Medium,
        RiskSeverity.High => ContractRiskLevel.High,
        RiskSeverity.Critical => ContractRiskLevel.Critical,
        _ => throw new ArgumentOutOfRangeException(nameof(risk), risk, "Unknown RiskSeverity."),
    };

    /// <summary>
    /// Wire-shapes <see cref="PriorityScoreResult"/> (parent story us-02-priority-score AC-1/AC-2):
    /// total plus every named component as its own <c>{ score, explanation }</c> pair, never a
    /// single opaque number — the "explainability query" this task (E03/F01/US02/T02) adds as
    /// <see cref="PriorityScoreCalculator"/>'s first real host caller
    /// (<c>Raffa.Renewals.Infrastructure.ServiceCollectionExtensions</c>'s own doc comment used
    /// to name this exact gap: "no host endpoint calls it yet").
    /// </summary>
    private static object ToPriorityResponse(PriorityScoreResult result)
    {
        return new
        {
            contractId = result.ContractId.Value,
            totalScore = result.TotalScore,
            components = new
            {
                spendWeight = ToComponentResponse(result.SpendWeight),
                timeUrgency = ToComponentResponse(result.TimeUrgency),
                benchmarkOpportunity = ToComponentResponse(result.BenchmarkOpportunity),
                priceIncreaseRisk = ToComponentResponse(result.PriceIncreaseRisk),
                contractRisk = ToComponentResponse(result.ContractRisk),
            },
        };
    }

    private static object ToComponentResponse(PriorityScoreComponent component) => new
    {
        score = component.Score,
        explanation = component.Explanation,
    };
    /// `POST /api/renewals/{id}/action` (us-01-renewal-dashboard-api AC-3: "updates owner/status/
    /// action"). <c>{id}</c> is the same <c>contractId</c> `GET /api/renewals` returns per row —
    /// there is no separate, persisted "renewal id" (see <see cref="Raffa.Renewals.Domain.RenewalAction"/>'s
    /// own doc comment). 400 for a missing/invalid tenant header or route id (same guard shape as
    /// every other endpoint in this file/host), 400 with <see cref="Raffa.SharedKernel.Result{T}.Error"/>
    /// for an empty <c>owner</c>/<c>action</c> or an unrecognized <c>status</c> — never a 404: unlike
    /// `PATCH /api/contracts/{id}`, this module cannot check whether <c>{id}</c> names an existing
    /// contract at all (ADR-002 forbids <c>Raffa.Renewals</c> from referencing
    /// <c>Raffa.Documents.Contracts</c>), so a well-formed action against a nonexistent or
    /// cross-tenant contract id still upserts a row rather than failing closed — an honest,
    /// documented gap (<see cref="Raffa.Renewals.Domain.RenewalAction"/>'s own doc comment),
    /// not silently swallowed.
    /// </summary>
    private static async Task<IResult> PostRenewalActionAsync(
        string id,
        RenewalActionRequest request,
        HttpRequest httpRequest,
        RenewalActionService actionService,
        ICallerContext callerContext,
        CancellationToken cancellationToken)
    {
        // NW-05 (ADR-010 w15 footer; ADR-022 w15 footer clause 2): identity first, then the tenant
        // header as an authorized selector, then membership -- 401 / 400 / 404 in that order, all
        // owned by ICallerContext (acceptance A15-8). The scope it hands back is the tenant scope
        // this handler runs in; disposing it here is the same lifetime the old BeginScope had.
        var caller = await callerContext.ResolveTenantAsync(httpRequest, cancellationToken);
        if (caller.Failure is not null)
        {
            return caller.Failure;
        }

        using var callerTenantScope = caller.Scope;
        var tenantGuid = caller.TenantId.Value;

        if (!Guid.TryParse(id, out var contractGuid))
        {
            return Results.BadRequest(
                "The renewal id in the route must be a GUID (the same 'contractId' GET /api/renewals returns).");
        }

        var result = await actionService.SetActionAsync(
            new TenantId(tenantGuid),
            new EntityId(contractGuid),
            request.Owner,
            request.Status,
            request.Action,
            caller.Identity!,
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Results.BadRequest(result.Error);
        }

        return Results.Ok(ToActionResponse(result.Value));
    }

    /// <summary>
    /// `GET /api/renewals/{id}/action` (task E19/F01/US01/T01, renewal-action-api; ADR-028 §D1;
    /// parent story us-01-renewal-action-api AC-1). <c>{id}</c> carries <b>exactly</b> the same
    /// meaning as on <see cref="PostRenewalActionAsync"/> above — confirmed against that handler
    /// before this route was written (ADR-028 assumption 1): the contract id
    /// <see cref="RenewalActionService"/> keys its row on — so a caller reads back precisely the row
    /// its own (or a colleague's) earlier POST to the same <c>{id}</c> wrote. Same guard-clause shape
    /// as <see cref="PostRenewalActionAsync"/>: tenant/identity via <see cref="ICallerContext"/>
    /// first, then the route id's GUID format. 404 when the id is a well-formed contract id with no
    /// persisted row — <see cref="RenewalActionService.GetActionAsync"/> cannot and does not
    /// distinguish "never posted" from "posted by a different tenant" (ADR-009's RLS already makes
    /// that indistinguishable at the query level), so neither does this handler. Never a 200 with a
    /// default/placeholder body: absence of a row <b>is</b> the status <c>NotStarted</c> (ADR-028
    /// §D1) — a fact the caller reconstructs itself, not one this route fabricates.
    /// </summary>
    private static async Task<IResult> GetRenewalActionAsync(
        string id,
        HttpRequest request,
        RenewalActionService actionService,
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

        if (!Guid.TryParse(id, out var contractGuid))
        {
            return Results.BadRequest(
                "The renewal id in the route must be a GUID (the same 'contractId' GET /api/renewals returns).");
        }

        var action = await actionService.GetActionAsync(
            new TenantId(tenantGuid), new EntityId(contractGuid), cancellationToken).ConfigureAwait(false);

        return action is null ? Results.NotFound() : Results.Ok(ToActionResponse(action));
    }

    /// <summary>
    /// Wire-shapes a <see cref="RenewalActionResult"/> — the one place <see cref="PostRenewalActionAsync"/>,
    /// <see cref="GetRenewalActionAsync"/> and the <c>savedAction</c> embedding in
    /// <see cref="ToPipelineResponse"/> all build this response, so a write and every way of reading
    /// it back can never drift into different shapes (task E19/F01/US01/T01).
    /// </summary>
    private static object ToActionResponse(RenewalActionResult action) => new
    {
        contractId = action.ContractId.Value,
        owner = action.Owner,
        status = action.Status.ToString(),
        action = action.Action,
        updatedAt = action.UpdatedAt,
    };
}
