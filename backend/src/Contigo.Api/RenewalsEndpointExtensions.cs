using Contigo.Documents.Contracts.Application;
using Contigo.Documents.Contracts.Domain;
using Contigo.Renewals.Application;
using Contigo.Renewals.Domain;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Suppliers;
using Contigo.SharedKernel.Tenancy;

namespace Contigo.Api;

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
/// may reference the other (`Contigo.ArchitectureTests.DependencyDirectionTests`'s allow-list for
/// both is exactly `[SharedKernel, AiGateway|Benchmark]`), so mapping <see cref="PortfolioListItem"/>
/// into <see cref="RenewalDashboardCandidate"/> can only happen here, in `Contigo.Api` — "the one
/// project allowed to reference every module" (`backend/README.md` "Dependency direction") — the
/// same pattern <c>ChatEndpointExtensions</c> already uses for <c>EmbeddingSearchResult</c> -&gt;
/// <c>AiEvidenceSnippet</c>.
///
/// <para>
/// `GET /api/renewals/{contractId}/priority` reuses <see cref="Contract360QueryService"/>
/// (already the tenant-scoped, 404-on-missing-or-wrong-tenant single-contract lookup
/// `Contigo.Api.ContractsEndpointExtensions` uses for `GET /api/contracts/{id}`) instead of the
/// portfolio's paged list — one contract, not a page. <see cref="MapRiskLevel"/> below is the
/// composition <see cref="ContractRiskLevel"/>'s own doc comment named as an open gap ("a
/// composition root maps `PortfolioListItem.Risk`... onto this enum 1:1; no task in this wave
/// wires that composition yet") — <see cref="Contract360Header.Risk"/> is computed the same way
/// <c>PortfolioListItem.Risk</c> is, so the same mapping applies. <c>AnnualUpliftPercent</c> and
/// <c>BenchmarkMarketPositionPercent</c> stay honestly <see langword="null"/> — neither has a real
/// producer yet (Benchmark Service is still an R0 placeholder, and no task has added an uplift
/// column/extraction field to <c>Contract</c>), the same gap
/// <see cref="RenewalInsightRecommendations"/>'s own doc comment already documents for the sibling
/// `GET /api/renewals` response — <see cref="Contigo.Renewals.Application.PriorityScoreCalculator"/>
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
/// is the whole implementation) because it never needs to read <c>Contigo.Documents.Contracts</c> —
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
/// </summary>
public static class RenewalsEndpointExtensions
{
    public static IEndpointRouteBuilder MapRenewalsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/renewals", GetRenewalsAsync);
        endpoints.MapGet("/api/renewals/{contractId}/priority", GetRenewalPriorityAsync);
        endpoints.MapPost("/api/renewals/{id}/action", PostRenewalActionAsync);
        return endpoints;
    }

    private static async Task<IResult> GetRenewalsAsync(
        HttpRequest request,
        PortfolioQueryService portfolioQueryService,
        RenewalPipelineBuilder pipelineBuilder,
        ISupplierNameLookup supplierNameLookup,
        ITenantContext tenantContext,
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
            new PortfolioFilter(AutoRenewal: true),
            new PortfolioPageRequest(Page: 1, PageSize: PortfolioPageRequest.MaxPageSize),
            cancellationToken).ConfigureAwait(false);

        var supplierNames = await PortfolioEndpointExtensions
            .ResolveSupplierNamesAsync(tenantId, portfolioPage.Items, supplierNameLookup, tenantContext, cancellationToken)
            .ConfigureAwait(false);

        var candidates = portfolioPage.Items.Select(ToCandidate);
        var pipeline = pipelineBuilder.Build(candidates);

        return Results.Ok(new
        {
            items = pipeline.Select(item => ToPipelineResponse(item, supplierNames)),
            totalCount = portfolioPage.TotalCount,
        });
    }

    /// <summary>
    /// `GET /api/renewals/{contractId}/priority` (task E03/F01/US02/T02, parent story
    /// us-02-priority-score AC-1/AC-2) — the explainable priority-score breakdown for one
    /// tenant-scoped contract. Same guard-clause shape as
    /// <c>Contigo.Api.ContractsEndpointExtensions.GetContract360Async</c> (tenant header, then
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
        CancellationToken cancellationToken)
    {
        if (!request.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
            || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

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
    /// decision is made here.
    /// </summary>
    private static RenewalDashboardCandidate ToCandidate(PortfolioListItem item) =>
        new(
            new EntityId(item.ContractId),
            item.SupplierId is { } supplierId ? new EntityId(supplierId) : null,
            item.EndDate,
            item.AutoRenewal,
            item.AnnualSpend,
            item.CancellationDeadline);

    /// <summary>
    /// Wire-shapes <see cref="RenewalPipelineItem"/> per AC-1 (top-level supplier/renewal/days/
    /// spend/deadline/action columns) and AC-2 (nested <c>insightCard.facts</c> /
    /// <c>insightCard.recommendations</c>, spec §9.3). Enum members and <c>EntityId</c>/
    /// <c>EntityId?</c> wrapper values are projected to plain strings/GUIDs — the same convention
    /// <see cref="PortfolioEndpointExtensions"/> and <see cref="ContractsEndpointExtensions"/>
    /// already use.
    /// </summary>
    private static object ToPipelineResponse(
        RenewalPipelineItem item, IReadOnlyDictionary<EntityId, string> supplierNames)
    {
        var facts = item.InsightCard.Facts;
        var recommendations = item.InsightCard.Recommendations;
        var supplierName = PortfolioEndpointExtensions.LookupSupplierName(supplierNames, item.SupplierId?.Value);

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
    /// is deliberately null (same gap <c>Contigo.Renewals.Application.ContractRenewalTerms</c>'s
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
    /// wires that composition yet"). Only <c>Contigo.Api</c> may perform this mapping: ADR-002
    /// forbids <c>Contigo.Renewals</c> from referencing <c>Contigo.Documents.Contracts</c> at all
    /// (`Contigo.ArchitectureTests.DependencyDirectionTests`'s allow-list for that module is
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
    /// (<c>Contigo.Renewals.Infrastructure.ServiceCollectionExtensions</c>'s own doc comment used
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
    /// there is no separate, persisted "renewal id" (see <see cref="Contigo.Renewals.Domain.RenewalAction"/>'s
    /// own doc comment). 400 for a missing/invalid tenant header or route id (same guard shape as
    /// every other endpoint in this file/host), 400 with <see cref="Contigo.SharedKernel.Result{T}.Error"/>
    /// for an empty <c>owner</c>/<c>action</c> or an unrecognized <c>status</c> — never a 404: unlike
    /// `PATCH /api/contracts/{id}`, this module cannot check whether <c>{id}</c> names an existing
    /// contract at all (ADR-002 forbids <c>Contigo.Renewals</c> from referencing
    /// <c>Contigo.Documents.Contracts</c>), so a well-formed action against a nonexistent or
    /// cross-tenant contract id still upserts a row rather than failing closed — an honest,
    /// documented gap (<see cref="Contigo.Renewals.Domain.RenewalAction"/>'s own doc comment),
    /// not silently swallowed.
    /// </summary>
    private static async Task<IResult> PostRenewalActionAsync(
        string id,
        RenewalActionRequest request,
        HttpRequest httpRequest,
        RenewalActionService actionService,
        CancellationToken cancellationToken)
    {
        if (!httpRequest.Headers.TryGetValue("X-Tenant-Id", out var tenantHeaderValues)
            || !Guid.TryParse(tenantHeaderValues.ToString(), out var tenantGuid))
        {
            return Results.BadRequest("A valid 'X-Tenant-Id' header (a GUID) is required.");
        }

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
            cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return Results.BadRequest(result.Error);
        }

        var action = result.Value;
        return Results.Ok(new
        {
            contractId = action.ContractId.Value,
            owner = action.Owner,
            status = action.Status.ToString(),
            action = action.Action,
            updatedAt = action.UpdatedAt,
        });
    }
}
