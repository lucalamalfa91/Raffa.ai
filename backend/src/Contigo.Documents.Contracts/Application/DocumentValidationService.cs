using Contigo.Documents.Contracts.Domain;
using Contigo.Documents.Contracts.Infrastructure;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Contigo.Documents.Contracts.Application;

/// <summary>Outcome of <see cref="DocumentValidationService.ValidateAsync"/>.</summary>
/// <param name="AcceptedFields">The field names the reviewer accepted as extracted, normalized
/// (trimmed, distinct, empty entries dropped) — echoed back and recorded on the audit row.</param>
/// <param name="AlreadyValidated">True when the document was already <see cref="DocumentProcessingStatus.Completed"/>
/// before this call: the call is idempotent and still audited, but changed nothing.</param>
public sealed record DocumentValidationResult(
    EntityId DocumentId,
    EntityId? ContractId,
    DocumentProcessingStatus ProcessingStatus,
    DateTimeOffset ValidatedAt,
    IReadOnlyList<string> AcceptedFields,
    bool AlreadyValidated);

/// <summary>
/// The human sign-off that closes a review (<c>POST /api/documents/{id}/validate</c>; product spec
/// §7.1 "needs review → completed", §7.3 human-in-the-loop; ADR-020 screen 6 "Mark as validated").
/// Until this service existed the review screen's own "Mark as validated" was a client-side
/// navigation with no write behind it: a reviewer could accept every proposed field and the
/// document still sat in <c>needs_review</c> — invisible to Ask Contigo, Portfolio and Renewals,
/// which read only <c>completed</c> documents. A field <em>correction</em> (<c>PATCH
/// /api/contracts/{id}</c>) is a real write but never a status transition either, so the only way
/// a document ever reached <c>completed</c> was an extraction with no weak fact at all.
///
/// <para>
/// <b>What validating means.</b> The reviewer has looked at every field the screen flagged and
/// either accepted the extraction or corrected it (corrections are already durable through
/// <c>ContractCorrectionService</c> by the time this is called). This service records that
/// decision as the document's status and one append-only <c>document.validated</c> audit row
/// naming the accepted fields (Appendix C rule 9: capture decisions from day one) — never by
/// rewriting the <see cref="ExtractionEvidence"/> rows, whose confidence stays what the model
/// reported (rule 5: preserve the original extraction).
/// </para>
///
/// <para>
/// <b>Only a reviewed document can be validated.</b> A document still <see cref="DocumentProcessingStatus.Uploaded"/>
/// or <see cref="DocumentProcessingStatus.Processing"/> has nothing to sign off yet, and a
/// <see cref="DocumentProcessingStatus.Failed"/> one has no extraction to trust — both are refused
/// with a named reason (the endpoint answers 409), not silently flipped. Validating an already
/// <see cref="DocumentProcessingStatus.Completed"/> document is an idempotent no-op that is still
/// audited, so a retried click never errors and never hides that it happened.
/// </para>
/// </summary>
public sealed class DocumentValidationService(
    DocumentsContractsDbContext dbContext,
    ITenantContext tenantContext,
    IAuditWriter auditWriter,
    IClock clock)
{
    /// <summary>Audit action for a completed review sign-off.</summary>
    public const string ValidatedAuditAction = "document.validated";

    public const string StillProcessingError =
        "This document is still being processed; wait for extraction to finish before validating it.";

    public const string FailedProcessingError =
        "This document failed processing; reprocess it before validating.";

    /// <summary>Upper bound on the accepted-field names recorded per call — the review screen has
    /// fewer than twenty correctable fields, so anything beyond this is a malformed request.</summary>
    private const int MaxAcceptedFields = 50;

    /// <summary>
    /// Marks <paramref name="documentId"/> <see cref="DocumentProcessingStatus.Completed"/> and
    /// audits the sign-off. <see langword="null"/> when no such document exists for this tenant;
    /// a failure when the document is not in a reviewable state (see the type doc comment).
    /// </summary>
    /// <param name="acceptedFields">Field names the reviewer accepted as extracted, as the review
    /// screen reports them (any casing; may be empty when every field was corrected instead).</param>
    /// <param name="actor">Who signed off, for the audit row.</param>
    public async Task<Result<DocumentValidationResult>?> ValidateAsync(
        TenantId tenantId,
        EntityId documentId,
        IReadOnlyList<string> acceptedFields,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(acceptedFields);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var document = await dbContext.Documents
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentId, cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            return null;
        }

        switch (document.ProcessingStatus)
        {
            case DocumentProcessingStatus.Uploaded:
            case DocumentProcessingStatus.Processing:
                return Result<DocumentValidationResult>.Failure(StillProcessingError);
            case DocumentProcessingStatus.Failed:
                return Result<DocumentValidationResult>.Failure(FailedProcessingError);
        }

        var normalizedFields = acceptedFields
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxAcceptedFields)
            .ToList();

        var now = clock.UtcNow;
        var alreadyValidated = document.ProcessingStatus == DocumentProcessingStatus.Completed;

        if (!alreadyValidated)
        {
            document.ProcessingStatus = DocumentProcessingStatus.Completed;
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await auditWriter.WriteAsync(
                new AuditEntry(
                    tenantId,
                    actor,
                    ValidatedAuditAction,
                    "document",
                    documentId.Value.ToString(),
                    now,
                    $"contractId={(document.ContractId is { } contractId ? contractId.Value.ToString() : "none")}; " +
                    $"acceptedFields={string.Join(",", normalizedFields)}; alreadyValidated={alreadyValidated}"),
                cancellationToken)
            .ConfigureAwait(false);

        return Result<DocumentValidationResult>.Success(new DocumentValidationResult(
            document.Id,
            document.ContractId,
            document.ProcessingStatus,
            now,
            normalizedFields,
            alreadyValidated));
    }
}
