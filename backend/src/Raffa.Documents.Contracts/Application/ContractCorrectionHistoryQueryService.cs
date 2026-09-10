using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Implements task E02/F05/US01/T02 (us-01-correction-history, AC-2: "correction history is
/// queryable") — reads back the per-field <see cref="CorrectionHistory"/> trail task
/// E02/F05/US01/T01's <see cref="ContractCorrectionService"/> already writes, scoped to the
/// caller's tenant. This is a different read surface from the cross-module audit trail
/// <see cref="ContractCorrectionService"/> also now writes to via <see cref="IAuditWriter"/> (this
/// task's other half, `GET /api/audit`): that endpoint answers "who changed something, when, on
/// which resource" for every module, while this service answers "what exactly changed on this
/// contract, field by field, old value to new value" — see
/// <c>Raffa.Audit.Domain.AuditEvent</c>'s own doc comment on why the two are allowed to diverge
/// rather than one subsuming the other.
///
/// Same read-side conventions as <see cref="DocumentQueryService"/>: opens its own
/// <see cref="ITenantContext.BeginScope"/> (nothing upstream of this call has already opened one —
/// it is itself the top-level entry point for a read), and filters explicitly by
/// <see cref="TenantId"/> in the query on top of the ADR-009 RLS backstop, so a cross-tenant
/// contract id is denied for two independent reasons, not one. Returns <c>null</c> — not an empty
/// list — when the contract itself does not exist (or does not belong to this tenant), so
/// <c>Raffa.Api.ContractsEndpointExtensions</c> can tell "no such contract" (404) apart from
/// "this contract has never been corrected" (200 with an empty array).
/// </summary>
public sealed class ContractCorrectionHistoryQueryService(
    DocumentsContractsDbContext dbContext, ITenantContext tenantContext)
{
    /// <summary>Same discriminator <see cref="ContractCorrectionService"/> writes on every
    /// <see cref="CorrectionHistory.TargetEntityType"/> row for a <see cref="Contract"/>
    /// correction — filtered explicitly here too, even though today it is the only value this
    /// module ever writes, because <see cref="CorrectionHistory.TargetEntityId"/> is a polymorphic
    /// pointer (Contract, Clause, Obligation, ...; see that type's own doc comment) and this
    /// service is specifically the `/api/contracts/{id}/corrections` read.</summary>
    private const string ContractEntityType = nameof(Contract);

    public async Task<IReadOnlyList<CorrectionHistoryRecord>?> GetHistoryAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default)
    {
        // Entry point: open this call's own tenant scope rather than trusting one is already
        // active (see the type doc comment) — must happen before either query below, since the
        // RLS connection interceptor reads ITenantContext.Current only when the connection opens.
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var contractExists = await dbContext.Contracts
            .AsNoTracking()
            .AnyAsync(c => c.TenantId == tenantId && c.Id == contractId, cancellationToken)
            .ConfigureAwait(false);

        if (!contractExists)
        {
            return null;
        }

        // Newest first — mirrors Raffa.Audit.Infrastructure.AuditQueryService's own
        // "most recent activity first" ordering for the sibling audit-trail read.
        return await dbContext.CorrectionHistories
            .AsNoTracking()
            .Where(h => h.TenantId == tenantId
                && h.TargetEntityType == ContractEntityType
                && h.TargetEntityId == contractId)
            .OrderByDescending(h => h.CorrectedAt)
            .Select(h => new CorrectionHistoryRecord(
                h.FieldName, h.PreviousValue, h.NewValue, h.CorrectedBy, h.CorrectedAt, h.Reason))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}

/// <summary>
/// One <see cref="CorrectionHistory"/> row as returned to a caller of
/// <see cref="ContractCorrectionHistoryQueryService"/> — the query side's own read model,
/// deliberately independent of the domain entity (same rationale as
/// <c>Raffa.Audit.Infrastructure.AuditEventRecord</c>'s own doc comment: the two are allowed to
/// diverge later without forcing a breaking HTTP contract change). Omits
/// <c>TenantId</c>/<c>TargetEntityType</c>/<c>TargetEntityId</c>: every record in one response
/// already belongs to the one tenant and one contract the caller asked about.
/// </summary>
public sealed record CorrectionHistoryRecord(
    string FieldName,
    string? PreviousValue,
    string? NewValue,
    string CorrectedBy,
    DateTimeOffset CorrectedAt,
    string? Reason);
