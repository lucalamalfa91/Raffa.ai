using Microsoft.EntityFrameworkCore;
using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Savings.Application.Spend;

/// <summary>The recorded results and their invoice proof, as <see cref="SavingsAllocator"/> reads them.</summary>
public interface ISavingsLedgerSource
{
    Task<IReadOnlyList<SavingInput>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The recorded results (<c>saving_result</c>) with what invoices prove of each
/// (<c>saving_realization</c> joined to its <c>invoice</c> month), as <see cref="SavingsAllocator"/>
/// reads them. Tenant-scoped.
/// </summary>
public sealed class SavingsLedgerReader(SavingsDbContext dbContext, ITenantContext tenantContext) : ISavingsLedgerSource
{
    public async Task<IReadOnlyList<SavingInput>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var rows = await dbContext.SavingResults.AsNoTracking()
            .Where(r => r.TenantId == tenantId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var proofs = await (
                from z in dbContext.SavingRealizations.AsNoTracking()
                join i in dbContext.Invoices.AsNoTracking() on z.InvoiceId equals i.Id
                where z.TenantId == tenantId
                select new { z.SavingResultId, z.RealizedAmount, i.Period })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var byResult = proofs.GroupBy(p => p.SavingResultId).ToDictionary(g => g.Key, g => g.ToList());

        return rows.Select(r => new SavingInput(
                r.Id.Value,
                r.ContractId,
                r.SupplierName,
                r.ContractName,
                r.Kind,
                r.CountsAsNegotiation && r.Kind != "volume",
                r.AmountAnnual,
                r.Currency,
                r.PeriodFrom,
                r.PeriodTo,
                r.SignedOn,
                r.Note,
                r.BaselineDesc,
                r.PeriodDesc,
                r.Method,
                r.EvidenceFileName,
                byResult.TryGetValue(r.Id, out var ps)
                    ? ps.Select(p => new SavingRealizationInput(new DateOnly(p.Period.Year, p.Period.Month, 1), p.RealizedAmount)).ToList()
                    : []))
            .ToList();
    }
}
