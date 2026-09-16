using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Savings.Application;

/// <summary>
/// Implements task E04/F03/US01/T01 (savings-kpis)'s database-facing half, plus task
/// E20/F01/US01/T01's verified-money read: fetches every tenant-scoped
/// <see cref="Domain.SavingsOpportunity"/> row and every <see cref="Domain.RealizedSavings"/> row,
/// reduces each to a snapshot, and hands both sequences to <see cref="SavingsKpiCalculator"/>
/// (the pure half — see that type's own doc comment) — the same "thin EF fetch, then a pure
/// calculator" split <c>Raffa.Api.RenewalsEndpointExtensions.GetRenewalsAsync</c> +
/// <c>Raffa.Renewals.Application.RenewalPipelineBuilder</c> already establish, collapsed into one
/// class here because both halves live in this module already (no cross-module composition needed —
/// unlike the renewals dashboard, nothing here reaches into <c>Raffa.Documents.Contracts</c>).
///
/// Same shape as <see cref="SavingsOpportunityService"/>: nothing upstream opens a tenant scope
/// before a read runs (see that type's own doc comment), so this opens its own
/// <see cref="ITenantContext.BeginScope"/> rather than trusting one is already active.
/// </summary>
public sealed class SavingsKpiQueryService(
    SavingsDbContext dbContext, ITenantContext tenantContext, SavingsKpiCalculator calculator)
{
    /// <summary>Backs the "Savings Identified"/"Savings In Progress"/"Savings Realized" thirds of
    /// `GET /api/savings/kpis` (product spec §10.1; parent story us-01-savings-kpis AC-1). Verified
    /// money is projected from <see cref="SavingsDbContext.RealizedSavingsRecords"/>, never from an
    /// opportunity's estimate range.</summary>
    public async Task<SavingsKpiSummary> GetSummaryAsync(
        TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);

        var snapshots = await dbContext.SavingsOpportunities
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId)
            .Select(o => new SavingsOpportunitySnapshot(
                o.Status, o.Currency, o.EstimatedSavingsLow, o.EstimatedSavingsHigh, o.Confidence))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var realized = await dbContext.RealizedSavingsRecords
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .Select(r => new RealizedSavingsSnapshot(r.Currency, r.Amount))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return calculator.Summarize(snapshots, realized);
    }
}
