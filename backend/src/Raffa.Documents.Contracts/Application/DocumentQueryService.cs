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

        var documents = await query
            .OrderByDescending(d => d.CreatedAt)
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
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (documents.Count == 0)
        {
            return new DocumentListPage([], pageNumber, size, totalCount);
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
                d.ContractId is { } weakContractId ? weakFactsByContract.GetValueOrDefault(weakContractId) : 0))
            .ToList();

        return new DocumentListPage(items, pageNumber, size, totalCount);
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
