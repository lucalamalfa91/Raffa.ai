using Microsoft.EntityFrameworkCore;
using Raffa.Savings.Domain.Results;
using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Savings;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Savings.Application.Results;

/// <summary>
/// Reads what Risultati is built from: signed results with their invoice-proven part
/// (<c>saving_result</c> + <c>saving_realization</c> + <c>invoice</c>), the validated contracts
/// (<see cref="IContractFactsReader"/>: analysed spend, notice deadlines) and the cases' objectives
/// (<see cref="ICaseObjectiveSource"/>). Builds <c>GET /api/savings/results</c>.
/// </summary>
public sealed class ResultsQueryService(
    SavingsDbContext dbContext,
    ITenantContext tenantContext,
    IContractFactsReader contractFacts,
    ICaseObjectiveSource caseObjectives,
    IClock clock)
{
    public DateOnly Today => DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

    public async Task<ResultsReport> GetReportAsync(TenantId tenantId, string? period, CancellationToken cancellationToken = default)
    {
        var validated = await ListValidatedAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var results = await ListResultsAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var objectives = await caseObjectives.ListAsync(tenantId, cancellationToken).ConfigureAwait(false);
        return ResultsReportBuilder.Build(Today, period, results, validated, objectives);
    }

    public async Task<IReadOnlyList<ResultsContract>> ListValidatedAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var today = Today;
        var facts = await contractFacts.ListValidatedAsync(tenantId, cancellationToken).ConfigureAwait(false);
        // Validation order (the prototype's `validated` list): ties in the charts keep this order.
        return facts
            .OrderBy(f => f.ValidatedAt)
            .ThenBy(f => f.FixtureId ?? int.MaxValue)
            .Select(f =>
            {
                // The effective term: a contract that renewed by itself is read on its next notice deadline.
                var term = ContractTermCalculator.Compute(f, today);
                var deadline = term.RolledOver ? term.NoticeDeadline : f.NoticeDeadline;
                return new ResultsContract(
                    f.ContractId,
                    f.Supplier,
                    f.Category,
                    f.Spend,
                    deadline,
                    ResultsReportBuilder.DaysTo(deadline, today, f.CancelDays),
                    f.Uplift,
                    string.Equals(f.Auto, "Yes", StringComparison.OrdinalIgnoreCase),
                    term.Status);
            })
            .ToList();
    }

    public async Task<IReadOnlyList<ResultsReportItem>> ListResultsAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var rows = await dbContext.SavingResults.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var proofs = await (
                from z in dbContext.SavingRealizations.AsNoTracking()
                join i in dbContext.Invoices.AsNoTracking() on z.InvoiceId equals i.Id
                where z.TenantId == tenantId
                select new { z.SavingResultId, z.RealizedAmount, i.Period })
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var byResult = proofs.GroupBy(p => p.SavingResultId).ToDictionary(g => g.Key, g => g.ToList());

        return rows.Select(r =>
        {
            byResult.TryGetValue(r.Id, out var ps);
            ResultsRealizationProof? proof = ps is { Count: > 0 }
                ? new ResultsRealizationProof(ps.Min(p => p.Period), ps.Max(p => p.Period), ps.Count, PeriodMonths(r))
                : null;
            return new ResultsReportItem(
                r.Id.Value,
                r.SupplierName,
                r.ContractName,
                r.Category,
                r.Kind,
                r.AmountAnnual,
                r.SignedOn,
                r.Note,
                r.BaselineDesc,
                r.PeriodDesc,
                r.Method,
                r.EvidenceFileName,
                r.OwnerName,
                ps?.Sum(p => p.RealizedAmount) ?? 0m,
                proof);
        }).ToList();
    }

    /// <summary>How many months the result's period covers (12 for "12 months from 01 Apr 2026").</summary>
    private static int? PeriodMonths(SavingResult r) =>
        r.PeriodFrom is { } from && r.PeriodTo is { } to
            ? MonthsBetween(from, to.AddDays(1))
            : null;

    private static int MonthsBetween(DateOnly from, DateOnly endExclusive) =>
        ((endExclusive.Year - from.Year) * 12) + endExclusive.Month - from.Month - (endExclusive.Day < from.Day ? 1 : 0);
}
