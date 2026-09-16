using Raffa.Api.Infrastructure;
using Raffa.Documents.Contracts.Application;
using Raffa.Savings.Application;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary>
/// Maps `GET /api/savings/kpis` — the procurement-homepage KPI row (product spec §4.3 "Display
/// annual spend analyzed, savings identified, savings realized, savings in progress, contracts
/// analyzed and upcoming renewals"; §10.1's own KPI table; module-map.md "Savings |
/// SavingsOpportunity, RealizedSavings | `/api/savings`"; parent story us-01-savings-kpis AC-1,
/// task E04/F03/US01/T01, the wave-spec's `savings-kpis` artifact). Nested under the Savings
/// module's own `/api/savings` prefix (a new top-level path is not listed anywhere in spec Appendix
/// A's endpoint table) rather than a dedicated file mapped alongside <see cref="SavingsEndpointExtensions"/>
/// — kept in its own file/route-mapping call, not folded into that class, so this task's diff does
/// not collide with the concurrently-scheduled `realized-savings` task's own edits to that file
/// (same wave-spec phase, per <c>reports/plan/wave-spec.execution.yaml</c>).
///
/// Thin composition per ADR-002: every real decision (grouping by currency, never silently
/// converting one currency into another, which contracts/opportunities count toward which bucket)
/// is made by <see cref="PortfolioAnalysisCalculator"/>/<see cref="SavingsKpiCalculator"/> (both
/// pure, unit-tested independently of this class); this file only resolves the tenant, calls each
/// module's own query service, and wire-shapes the result — the same pattern
/// <see cref="RenewalsEndpointExtensions"/> already uses to compose
/// <see cref="Raffa.Documents.Contracts.Application.PortfolioQueryService"/> with the Renewals
/// module.
///
/// <para>
/// Three of spec §10.1's six KPI rows ("Contracts Analyzed", "Annual Spend Analyzed") come from
/// <see cref="PortfolioQueryService.GetAnalysisSummaryAsync"/> (Documents/Contracts — no cross-module
/// reach needed, both queries run against that module's own tables); "Savings Identified"/"Savings
/// In Progress"/"Savings Realized" come from <see cref="SavingsKpiQueryService.GetSummaryAsync"/>
/// (Savings — likewise self-contained). "Upcoming Renewals" (spec §10.1: "Actionable renewal
/// pipeline") deliberately does **not** add a dependency on <c>Raffa.Renewals</c> at all: it
/// reuses the exact same <see cref="PortfolioQueryService.GetPortfolioAsync"/> call (auto-renewing
/// contracts only) that <see cref="RenewalsEndpointExtensions.GetRenewalsAsync"/> already makes for
/// `GET /api/renewals`'s own <c>totalCount</c> field, and takes that same
/// <see cref="PortfolioPage.TotalCount"/> value — <see cref="Raffa.Renewals.Application.RenewalPipelineBuilder"/>
/// never drops or filters a candidate (see that type's own doc comment), so building the full
/// pipeline just to count its rows would be redundant work for the same number. This also keeps the
/// homepage KPI and the `/api/renewals` list honestly consistent: they can never disagree, because
/// they are the same query. Same known, documented interim gap `GetRenewalsAsync` already carries:
/// capped at <see cref="PortfolioPageRequest.MaxPageSize"/> (100) auto-renewing contracts per tenant.
/// </para>
///
/// Same interim `X-Tenant-Id` header placeholder as every other endpoint in this host (ADR-010 is
/// not in this task's "Architecture decisions in force" list) — see <c>Program.cs</c>'s own comment
/// on why this interim gap is not promoted to reports/open-questions.md by these tasks.
/// </summary>
public static class SavingsKpiEndpointExtensions
{
    public static IEndpointRouteBuilder MapSavingsKpiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/savings/kpis", GetSavingsKpisAsync);
        return endpoints;
    }

    private static async Task<IResult> GetSavingsKpisAsync(
        HttpRequest request,
        PortfolioQueryService portfolioQueryService,
        SavingsKpiQueryService savingsKpiQueryService,
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

        // "Contracts Analyzed" / "Annual Spend Analyzed" (Documents/Contracts).
        var analysisSummary = await portfolioQueryService
            .GetAnalysisSummaryAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        // "Savings Identified" / "Savings In Progress" / "Savings Realized" (Savings).
        var savingsSummary = await savingsKpiQueryService
            .GetSummaryAsync(tenantId, cancellationToken)
            .ConfigureAwait(false);

        // "Upcoming Renewals" — reuses GET /api/renewals's own candidate query; see the type doc
        // comment for why this never rebuilds the full RenewalPipelineBuilder output just to count it.
        var renewalCandidates = await portfolioQueryService.GetPortfolioAsync(
            tenantId,
            new PortfolioFilter(AutoRenewal: true),
            new PortfolioPageRequest(Page: 1, PageSize: PortfolioPageRequest.MaxPageSize),
            cancellationToken).ConfigureAwait(false);

        return Results.Ok(new
        {
            annualSpendAnalyzed = analysisSummary.AnnualSpendAnalyzed.Select(ToCurrencyAmountResponse),
            contractsAnalyzedCount = analysisSummary.ContractsAnalyzedCount,
            savingsIdentified = savingsSummary.Identified.Select(ToSavingsRangeResponse),
            savingsInProgress = savingsSummary.InProgress.Select(ToSavingsRangeResponse),
            savingsRealized = savingsSummary.Realized.Select(ToRealizedSavingsResponse),
            upcomingRenewalsCount = renewalCandidates.TotalCount,
        });
    }

    private static object ToCurrencyAmountResponse(AnnualSpendByCurrency item) => new
    {
        currency = item.Currency,
        amount = item.Amount,
        contractCount = item.ContractCount,
    };

    private static object ToSavingsRangeResponse(SavingsRangeByCurrency item) => new
    {
        currency = item.Currency,
        low = item.Low,
        high = item.High,
        count = item.Count,
        averageConfidence = item.AverageConfidence,
    };

    private static object ToRealizedSavingsResponse(RealizedSavingsByCurrency item) => new
    {
        currency = item.Currency,
        amount = item.Amount,
        count = item.Count,
    };
}
