using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Savings.Application;

/// <summary>
/// Implements task E04/F03/US01/T01 (savings-kpis)'s database-facing half: fetches every
/// tenant-scoped <see cref="Domain.SavingsOpportunity"/> row, reduces each to a
/// <see cref="SavingsOpportunitySnapshot"/>, and hands the batch to <see cref="SavingsKpiCalculator"/>
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
    /// `GET /api/savings/kpis` (product spec §10.1; parent story us-01-savings-kpis AC-1).</summary>
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

        return calculator.Summarize(snapshots);
    }
}
