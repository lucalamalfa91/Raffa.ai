using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Tenant-scoped reads over the <c>document</c> table: one document's metadata (task
/// E01/F06/US01/T02) and — task E13/F04/US01/T02 — the server-side Documents list
/// (<c>inputs/requirements.md</c> R-DOC-06, replacing the V1 screen's <c>sessionStorage</c>).
///
/// <para>
/// <b>Supplier names, when the module is there.</b> <see cref="ISupplierNameLookup"/> is a
/// SharedKernel port (task E13/F03/US01/T01) implemented by <c>Raffa.Suppliers.Products</c>. It
/// is optional here — a host that has not registered the Suppliers module still gets a working
/// list, with <c>supplierName</c> honestly <see langword="null"/> rather than a raw id.
/// </para>
/// </summary>
public sealed class DocumentQueryService(
    DocumentsContractsDbContext dbContext,
    ITenantContext tenantContext,
    ISupplierNameLookup? supplierNameLookup = null)
{
    /// <summary>
    /// Confidence at or above which an extracted fact is treated as trustworthy. Same value and
    /// same reasoning as <c>StagedExtractionService.LowConfidenceThreshold</c> and
    /// <c>DocumentProcessingPipeline</c>'s own classification threshold — repeated rather than
    /// shared because each governs an independent decision (see those types' doc comments).
    /// </summary>
    private const double WeakFactThreshold = 0.6;

    /// <summary>
    /// The stricter bar a <b>critical</b> field is judged against — the same 0.8
    /// <c>StagedExtractionService.CriticalConfidenceThreshold</c> applies to the <c>supplier</c>
    /// fact (requirements R-SUP-01, spec §7.3). A supplier fact between the two bars is exactly the
    /// one the pipeline refused to link, so it must count towards "Review N fields" or the row
    /// would announce nothing to review for a document that is in <c>needs_review</c> because of it.
    /// </summary>
    private const double CriticalWeakFactThreshold = 0.8;

    private const string SupplierFieldName = "supplier";

    private static bool IsWeak(string fieldName, double? confidence)
    {
        var threshold = string.Equals(fieldName, SupplierFieldName, StringComparison.OrdinalIgnoreCase)
            ? CriticalWeakFactThreshold
            : WeakFactThreshold;
        return confidence is null || confidence < threshold;
    }

    public async Task<DocumentMetadataResult?> GetByIdAsync(
        TenantId tenantId, EntityId documentId, CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var document = await dbContext.Documents
            .AsNoTracking()
            .SingleOrDefaultAsync(d => d.TenantId == tenantId && d.Id == documentId, cancellationToken)
            .ConfigureAwait(false);

        return document is null
            ? null
            : new DocumentMetadataResult(
                document.Id,
                document.ContractId,
                document.FileName,
                document.MimeType,
                document.DocumentType,
                document.ProcessingStatus,
                document.CreatedAt);
    }

    /// <summary>
    /// One page of the tenant's documents, newest first (R-DOC-06). <paramref name="status"/>
    /// filters on the exact <see cref="DocumentProcessingStatus"/>; omit it for every status (the
    /// screen's own "Needs your attention" default is a client-side selection over three statuses,
    /// not a server concept — see <c>web/src/routes/documents</c>).
    /// </summary>
    public async Task<DocumentListPage> ListAsync(
        TenantId tenantId,
        DocumentProcessingStatus? status = null,
        int page = 1,
        int pageSize = PortfolioPageRequest.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        var pageNumber = Math.Max(page, 1);
        var size = Math.Clamp(pageSize, 1, PortfolioPageRequest.MaxPageSize);

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var query = dbContext.Documents.AsNoTracking().Where(d => d.TenantId == tenantId);
        if (status is { } wanted)
        {
            query = query.Where(d => d.ProcessingStatus == wanted);
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        // ADR-027 §D7 (task E16/F02/US03/T01): the counts are tenant-wide and unfiltered by
        // `?status=` -- one grouped query over every row of this tenant, never four round trips
        // and never a page-scoped number (a page-scoped count would be a new lie at page 2).
        // They are overlapping projections, not a partition: no sum holds, and the client must
        // not derive "askable" from `all - needsAttention` (ADR-027 §C5/§C9.1).
        var byStatus = await dbContext.Documents.AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .GroupBy(d => d.ProcessingStatus)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        int Of(params DocumentProcessingStatus[] statuses) =>
            byStatus.Where(b => statuses.Contains(b.Status)).Sum(b => b.Count);
        // ADR-027 §C9: `needsAttention` is "not Completed and not Rejected" -- the set the screen's
        // own default filter lists -- so it contains `processing`; §C5's `needsReview` sits inside
        // both. Five overlapping projections of one grouped query, never a partition.
        var counts = new DocumentCounts(
            All: byStatus.Where(b => b.Status != DocumentProcessingStatus.Rejected).Sum(b => b.Count),
            NeedsAttention: Of(
                DocumentProcessingStatus.Uploaded,
                DocumentProcessingStatus.Processing,
                DocumentProcessingStatus.NeedsReview,
                DocumentProcessingStatus.Failed),
            NeedsReview: Of(DocumentProcessingStatus.NeedsReview),
            Processing: Of(DocumentProcessingStatus.Uploaded, DocumentProcessingStatus.Processing),
            Rejected: Of(DocumentProcessingStatus.Rejected));

        // Actionable-first order (fix 2026-09-14 evening, superseding the queue-order fix earlier the
        // same day): that first fix put in-flight rows on top because a stalled-looking bar made a
        // twenty-file drop read as frozen from the bottom up. The perceived-instant-batch fix (ADR-020
        // w15 footer 10-12, same day) closed that problem a different way -- a row reads "Uploaded",
        // never a stalled bar -- so queue position no longer needs to drive the list's own order, and
        // it can instead answer the question a user actually opens this screen with: what is ready for
        // me to look at? Completed/NeedsReview/Failed (terminal -- there is a decision or an answer
        // waiting) sort first, newest first, so the most recently finished document is the one you see;
        // Processing next (a Worker is genuinely on it), then Uploaded (queued, not yet started) --
        // both still oldest-first within their own bucket, so the document closest to being picked up
        // stays at the top of its section. `Rejected` rows share the terminal bucket too, though in
        // practice they never reach here unfiltered: `counts.all` excludes them and the client's own
        // "attention"/"all" views filter them out client-side (they are visible only through the
        // dedicated `status=Rejected` fetch, which returns nothing else to sort against).
        var documents = await query
            .OrderBy(d => d.ProcessingStatus == DocumentProcessingStatus.Processing ? 1
                : d.ProcessingStatus == DocumentProcessingStatus.Uploaded ? 2
                : 0)
            .ThenBy(d => d.ProcessingStatus == DocumentProcessingStatus.Uploaded
                || d.ProcessingStatus == DocumentProcessingStatus.Processing ? d.CreatedAt : DateTimeOffset.MinValue)
            .ThenByDescending(d => d.CreatedAt)
            .ThenBy(d => d.Id)
            .Skip((pageNumber - 1) * size)
            .Take(size)
            .Select(d => new
            {
                d.Id,
                d.ContractId,
                d.FileName,
                d.DocumentType,
                d.ProcessingStatus,
                d.PageCount,
                d.CreatedAt,
                d.RejectionReason,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (documents.Count == 0)
        {
            return new DocumentListPage([], pageNumber, size, totalCount, counts);
        }

        var documentIds = documents.Select(d => d.Id).ToList();
        var contractIds = documents.Where(d => d.ContractId is not null)
            .Select(d => d.ContractId!.Value)
            .Distinct()
            .ToList();

        var stagesByDocument = await StagesByDocumentAsync(
            tenantId,
            documents.Where(d => d.ProcessingStatus == DocumentProcessingStatus.Processing).Select(d => d.Id).ToList(),
            cancellationToken).ConfigureAwait(false);

        var weakFactsByContract = await WeakFactCountsAsync(tenantId, contractIds, cancellationToken)
            .ConfigureAwait(false);
        var supplierNamesByContract = await SupplierNamesByContractAsync(tenantId, contractIds, cancellationToken)
            .ConfigureAwait(false);

        var items = documents
            .Select(d => new DocumentListItem(
                d.Id,
                d.ContractId,
                d.ContractId is { } contractId ? supplierNamesByContract.GetValueOrDefault(contractId) : null,
                d.FileName,
                d.DocumentType,
                d.ProcessingStatus,
                d.ProcessingStatus == DocumentProcessingStatus.Processing
                    ? stagesByDocument.GetValueOrDefault(d.Id, DocumentProcessingStageMap.Uploading)
                    : null,
                d.PageCount,
                d.CreatedAt,
                d.ContractId is { } weakContractId ? weakFactsByContract.GetValueOrDefault(weakContractId) : 0,
                d.RejectionReason))
            .ToList();

        return new DocumentListPage(items, pageNumber, size, totalCount, counts);
    }

    /// <summary>
    /// ADR-027 §D9 (task E16/F02/US03/T01): how many of this tenant's documents are still
    /// non-terminal (<c>Uploaded</c> + <c>Processing</c>) — the number <c>GET /api/contracts</c>
    /// carries as <c>processingDocumentCount</c>, so an empty portfolio can say "3 documents still
    /// processing" instead of "no contracts". Same definition as <see cref="DocumentCounts.Processing"/>.
    /// </summary>
    public async Task<int> CountProcessingDocumentsAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);
        return await dbContext.Documents.AsNoTracking()
            .CountAsync(
                d => d.TenantId == tenantId
                    && (d.ProcessingStatus == DocumentProcessingStatus.Uploaded
                        || d.ProcessingStatus == DocumentProcessingStatus.Processing),
                cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// ADR-027 §D9: <see cref="ContractReadiness"/> for one contract, from definition #1 (§D8)
    /// over its linked documents. <c>Stage</c> is the live stage of the first document still
    /// <c>Processing</c> (an <c>Uploaded</c> one reports the "uploading" stage), and only while the
    /// state is <see cref="ContractReadinessState.Processing"/>. A contract with no linked document
    /// is <see cref="ContractReadinessState.Unavailable"/>, never <c>Processing</c>: nothing is coming.
    /// </summary>
    public async Task<ContractReadiness> GetReadinessAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var linked = await dbContext.Documents.AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.ContractId == contractId)
            .OrderBy(d => d.CreatedAt)
            .Select(d => new { d.Id, d.ProcessingStatus })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var completed = linked.Count(d => d.ProcessingStatus == DocumentProcessingStatus.Completed);
        if (completed > 0)
        {
            return new ContractReadiness(ContractReadinessState.Ready, null, linked.Count, completed);
        }

        var inFlight = linked.FirstOrDefault(d =>
            d.ProcessingStatus is DocumentProcessingStatus.Uploaded or DocumentProcessingStatus.Processing);
        if (inFlight is null)
        {
            return new ContractReadiness(ContractReadinessState.Unavailable, null, linked.Count, 0);
        }

        var stage = inFlight.ProcessingStatus == DocumentProcessingStatus.Processing
            ? (await StagesByDocumentAsync(tenantId, [inFlight.Id], cancellationToken).ConfigureAwait(false))
                .GetValueOrDefault(inFlight.Id, DocumentProcessingStageMap.Uploading)
            : DocumentProcessingStageMap.Uploading;
        return new ContractReadiness(ContractReadinessState.Processing, stage, linked.Count, 0);
    }

    private async Task<Dictionary<EntityId, string>> StagesByDocumentAsync(
        TenantId tenantId, IReadOnlyList<EntityId> documentIds, CancellationToken cancellationToken)
    {
        if (documentIds.Count == 0)
        {
            return [];
        }

        var jobs = await dbContext.ExtractionJobs
            .AsNoTracking()
            .Where(j => j.TenantId == tenantId && documentIds.Contains(j.DocumentId))
            .Select(j => new { j.DocumentId, j.Stage, j.Status, j.StartedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return jobs
            .GroupBy(j => j.DocumentId)
            .ToDictionary(
                group => group.Key,
                group => DocumentProcessingStageMap.Resolve(
                    group.Select(j => new ExtractionJobSnapshot(j.Stage, j.Status, j.StartedAt)).ToList()));
    }

    /// <summary>
    /// Per contract, how many extracted fields a human should still confirm: the latest
    /// <see cref="ExtractionEvidence"/> row per field name whose confidence is missing or below
    /// <see cref="WeakFactThreshold"/> (R-DOC-06's <c>weakFactCount</c>, the number the row's
    /// "Review N fields" action shows). Counted per field, never per evidence row, so a field
    /// re-extracted three times still counts once.
    /// </summary>
    private async Task<Dictionary<EntityId, int>> WeakFactCountsAsync(
        TenantId tenantId, IReadOnlyList<EntityId> contractIds, CancellationToken cancellationToken)
    {
        if (contractIds.Count == 0)
        {
            return [];
        }

        var evidence = await dbContext.ExtractionEvidences
            .AsNoTracking()
            .Where(e => e.TenantId == tenantId && contractIds.Contains(e.ContractId))
            .Select(e => new { e.ContractId, e.FieldName, e.Confidence, e.CreatedAt })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return evidence
            .GroupBy(e => e.ContractId)
            .ToDictionary(
                byContract => byContract.Key,
                byContract => byContract
                    .GroupBy(e => e.FieldName, StringComparer.OrdinalIgnoreCase)
                    .Select(byField => byField.OrderByDescending(e => e.CreatedAt).First())
                    .Count(latest => IsWeak(latest.FieldName, latest.Confidence)));
    }

    private async Task<Dictionary<EntityId, string>> SupplierNamesByContractAsync(
        TenantId tenantId, IReadOnlyList<EntityId> contractIds, CancellationToken cancellationToken)
    {
        if (supplierNameLookup is null || contractIds.Count == 0)
        {
            return [];
        }

        var supplierIdByContract = await dbContext.Contracts
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && contractIds.Contains(c.Id) && c.SupplierId != null)
            .Select(c => new { c.Id, SupplierId = c.SupplierId!.Value })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (supplierIdByContract.Count == 0)
        {
            return [];
        }

        var names = await supplierNameLookup
            .GetNamesAsync(tenantId, supplierIdByContract.Select(x => x.SupplierId).Distinct().ToList(), cancellationToken)
            .ConfigureAwait(false);

        var result = new Dictionary<EntityId, string>();
        foreach (var row in supplierIdByContract)
        {
            if (names.TryGetValue(row.SupplierId, out var name) && !string.IsNullOrWhiteSpace(name))
            {
                result[row.Id] = name;
            }
        }

        return result;
    }
}
