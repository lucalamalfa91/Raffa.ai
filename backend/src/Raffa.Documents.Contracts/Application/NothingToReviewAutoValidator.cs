using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// A document is <see cref="DocumentProcessingStatus.NeedsReview"/> only while there is a field a
/// human can actually decide on. The review screen and the Documents row's "Review N fields"
/// action both count the contract's weak <see cref="ExtractionEvidence"/> (latest row per field,
/// <see cref="ExtractionConfidencePolicy.IsWeakEvidence"/>); a document can still land in
/// <c>NeedsReview</c> with that count at zero — a low-confidence line item, clause, obligation or
/// risk (none of which is a reviewable field), or a failed stage. "Review 0 fields" is not a state
/// the review screen can resolve, so such a document is validated automatically instead.
///
/// <para>
/// <c>StagedExtractionService</c> applies the same rule at the end of every run through
/// <see cref="WeakFactCountsAsync"/>; <see cref="AutoValidateInTenantAsync"/> is the sweep
/// <c>GET /api/documents</c> runs so documents stored before the rule existed heal on the next
/// list read, the same self-healing shape as <c>HungProcessingRecoveryService</c>.
/// </para>
/// </summary>
public sealed class NothingToReviewAutoValidator(
    DocumentsContractsDbContext dbContext,
    ITenantContext tenantContext,
    IAuditWriter auditWriter,
    IClock clock)
{
    /// <summary>Audit action for a document validated because it had nothing left to review.</summary>
    public const string AutoValidatedAuditAction = "document.auto_validated";

    private const string SystemActor = "system:extraction";

    /// <summary>
    /// Moves every <see cref="DocumentProcessingStatus.NeedsReview"/> document of this tenant whose
    /// contract has no weak fact left to <see cref="DocumentProcessingStatus.Completed"/>, with one
    /// audit row each. Tenant-scoped (ADR-009).
    /// </summary>
    public async Task AutoValidateInTenantAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var candidates = await dbContext.Documents
            .Where(d => d.TenantId == tenantId
                && d.ProcessingStatus == DocumentProcessingStatus.NeedsReview
                && d.ContractId != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (candidates.Count == 0)
        {
            return;
        }

        var contractIds = candidates.Select(d => d.ContractId!.Value).Distinct().ToList();
        var weakByContract = await WeakFactCountsAsync(dbContext, tenantId, contractIds, cancellationToken)
            .ConfigureAwait(false);

        var validated = candidates
            .Where(d => weakByContract.GetValueOrDefault(d.ContractId!.Value) == 0)
            .ToList();

        if (validated.Count == 0)
        {
            return;
        }

        foreach (var document in validated)
        {
            document.ProcessingStatus = DocumentProcessingStatus.Completed;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var now = clock.UtcNow;
        foreach (var document in validated)
        {
            await auditWriter.WriteAsync(
                    new AuditEntry(
                        tenantId,
                        SystemActor,
                        AutoValidatedAuditAction,
                        "document",
                        document.Id.Value.ToString(),
                        now,
                        $"contractId={document.ContractId!.Value.Value}; weakFactCount=0"),
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Per contract, how many extracted fields a human should still confirm: the latest
    /// <see cref="ExtractionEvidence"/> row per field name that is
    /// <see cref="ExtractionConfidencePolicy.IsWeakEvidence"/> (R-DOC-06's <c>weakFactCount</c>, the
    /// number the row's "Review N fields" action shows). Counted per field, never per evidence row,
    /// so a field re-extracted three times still counts once. A contract with no weak field is
    /// absent from the result (read it with <c>GetValueOrDefault</c>).
    /// </summary>
    public static async Task<Dictionary<EntityId, int>> WeakFactCountsAsync(
        DocumentsContractsDbContext dbContext,
        TenantId tenantId,
        IReadOnlyList<EntityId> contractIds,
        CancellationToken cancellationToken)
    {
        if (contractIds.Count == 0)
        {
            return [];
        }

        var evidence = await dbContext.ExtractionEvidences
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && contractIds.Contains(e.ContractId))
            .Select(e => new { e.ContractId, e.FieldName, e.Confidence, e.Decision, e.CreatedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return evidence
            .GroupBy(e => e.ContractId)
            .ToDictionary(
                byContract => byContract.Key,
                byContract => byContract
                    .GroupBy(e => e.FieldName, StringComparer.OrdinalIgnoreCase)
                    .Select(byField => byField.OrderByDescending(e => e.CreatedAt).First())
                    .Count(latest => ExtractionConfidencePolicy.IsWeakEvidence(latest.Confidence, latest.Decision)));
    }
}
