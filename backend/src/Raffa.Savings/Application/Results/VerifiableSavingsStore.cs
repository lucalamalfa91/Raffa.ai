using Microsoft.EntityFrameworkCore;
using Raffa.Savings.Domain.Results;
using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Savings;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Savings.Application.Results;

/// <summary>Default <see cref="ICaseObjectiveSource"/> / <see cref="ICaseObjectiveWriter"/> over the
/// <c>case_objective</c> table.</summary>
public sealed class CaseObjectiveTableStore(SavingsDbContext dbContext, ITenantContext tenantContext, IClock clock)
    : ICaseObjectiveSource, ICaseObjectiveWriter
{
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal) { "none", "p5", "p10", "budget", "custom" };

    public async Task<IReadOnlyList<CaseObjectiveInfo>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var rows = await dbContext.CaseObjectives.AsNoTracking()
            .Where(o => o.TenantId == tenantId)
            .OrderBy(o => o.UpdatedAt)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        return rows.Select(o => new CaseObjectiveInfo(o.CaseId, o.ContractId, o.OwnerName, o.Kind, o.Pct, o.Amount, o.Text, o.Outcome)).ToList();
    }

    public async Task UpsertAsync(TenantId tenantId, CaseObjectiveInfo objective, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(objective);
        if (objective.Kind is { } kind && !Kinds.Contains(kind))
        {
            throw new ArgumentException($"Unknown objective kind '{kind}'.", nameof(objective));
        }

        using var _ = tenantContext.BeginScope(tenantId);
        var row = await dbContext.CaseObjectives
            .FirstOrDefaultAsync(o => o.TenantId == tenantId && o.CaseId == objective.CaseId, cancellationToken).ConfigureAwait(false);
        if (row is null)
        {
            row = new CaseObjective { TenantId = tenantId, CaseId = objective.CaseId };
            dbContext.CaseObjectives.Add(row);
        }

        row.ContractId = objective.ContractId;
        row.OwnerName = objective.OwnerName;
        row.Kind = objective.Kind;
        row.Pct = objective.Pct;
        row.Amount = objective.Amount;
        row.Text = objective.Text;
        row.Outcome = objective.Outcome;
        row.UpdatedAt = clock.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Default <see cref="ISavingResultRecorder"/>: one <c>saving_result</c> row; realized stays 0
/// until invoices prove it.</summary>
public sealed class SavingResultRecorder(SavingsDbContext dbContext, ITenantContext tenantContext, IClock clock) : ISavingResultRecorder
{
    public async Task<Guid> RecordAsync(TenantId tenantId, SavingResultRecord result, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (!SavingResultRecord.Kinds.Contains(result.Kind))
        {
            throw new ArgumentException($"Unknown result kind '{result.Kind}'.", nameof(result));
        }

        using var _ = tenantContext.BeginScope(tenantId);
        var row = ToEntity(tenantId, result, clock.UtcNow);
        dbContext.SavingResults.Add(row);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return row.Id.Value;
    }

    public static SavingResult ToEntity(TenantId tenantId, SavingResultRecord r, DateTimeOffset now, EntityId? id = null) => new()
    {
        Id = id ?? EntityId.New(),
        TenantId = tenantId,
        ContractId = r.ContractId,
        CaseId = r.CaseId,
        SupplierName = r.SupplierName,
        ContractName = r.ContractName,
        Category = r.Category,
        Kind = r.Kind,
        CountsAsNegotiation = r.Kind != "volume",
        AmountAnnual = r.AmountAnnual,
        Currency = r.Currency,
        Note = r.Note,
        BaselineDesc = r.BaselineDescription,
        BaselineValue = r.BaselineValue,
        NewValue = r.NewValue,
        Quantity = r.Quantity,
        Method = r.Method,
        PeriodFrom = r.PeriodFrom,
        PeriodTo = r.PeriodTo,
        PeriodDesc = r.PeriodDescription,
        EvidenceDocumentId = r.EvidenceDocumentId,
        EvidenceFileName = r.EvidenceFileName,
        OwnerName = r.OwnerName,
        SignedOn = r.SignedOn,
        RecordedAt = now,
        RecordedBy = r.RecordedBy,
    };
}
