using Raffa.AiGateway.Configuration;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Application.Preview;

/// <summary>
/// Task E13/F04/US01/T02 (documents-v2-api, R-DOC-08): renders and stores a document's preview
/// pages, and reads one back for <c>GET /api/documents/{id}/preview</c>.
///
/// <para>
/// Task E22/F02/US01/T01 extended this to multi-page (ADR-029): each page is rendered and stored
/// individually, and the bitmap is disposed before the next page is started — keeping peak memory
/// to one frame regardless of document length (ADR-005 w17 §19/§23). Pages beyond the OCR budget
/// (<see cref="AiGatewayOcrOptions.MaxPagesPerDocument"/>) are not rendered. If a reprocess
/// produces fewer pages than the previous run, surplus page objects are deleted (round-3 clause 2).
/// </para>
///
/// <para>
/// The preview lives under the tenant's own object-storage prefix
/// (<see cref="DocumentStoragePath.BuildPreviewPage"/>) and is streamed through this API under the
/// caller's tenant scope — the client never receives a blob URL (ADR-009). Storing the path on the
/// <c>document</c> row is what makes the read tenant-checked twice: the row is only visible under
/// RLS, and the path is re-validated against the tenant prefix on load.
/// </para>
///
/// <para>
/// <b>Never fails the pipeline.</b> <see cref="RenderAndStoreAsync"/> returns <see langword="null"/>
/// when rendering or storing page 1 fails; the caller simply leaves <c>PreviewPath</c> unset and
/// the endpoint answers 404. A preview is a convenience, not a fact about the contract.
/// </para>
/// </summary>
public sealed class DocumentPreviewService(
    DocumentsContractsDbContext dbContext,
    IDocumentStorage storage,
    IDocumentPreviewRenderer renderer,
    ITenantContext tenantContext,
    AiGatewayOcrOptions? ocrOptions = null,
    IExtractionHangWatch? hangWatch = null,
    ExtractionProgressHeartbeat? progressHeartbeat = null)
{
    /// <summary>Content type every stored preview is served with (R-DOC-08, OpenAPI <c>image/png</c>).</summary>
    public const string PreviewContentType = "image/png";

    /// <summary>
    /// The same default as <c>AiGatewayOcrOptions.MaxPagesPerDocument</c>'s property initialiser
    /// (300), repeated here so the service can cap rendering even when no options object is
    /// injected — the live host always injects the real options, the test double uses a low cap.
    /// </summary>
    private const int DefaultPageBudget = 300;

    /// <summary>
    /// Renders and stores page previews for <paramref name="pageCount"/> pages (or the OCR budget,
    /// whichever is smaller), returning the storage path of page 1 (<see langword="null"/> when no
    /// preview could be produced). Does not save the document row: the caller owns that unit of work.
    ///
    /// <para>
    /// When <paramref name="previousPageCount"/> is supplied and is larger than the pages just
    /// rendered, the surplus page objects (<c>page-{n}.png</c> for <c>n &gt; pageCount</c>) are
    /// deleted (ADR-029 round-3 clause 2). The reap is always bounded to this document's own
    /// <c>preview/</c> prefix and only fires on a confirmed positive page count.
    /// </para>
    /// </summary>
    public async Task<string?> RenderAndStoreAsync(
        TenantId tenantId,
        EntityId documentId,
        string fileName,
        string mimeType,
        ReadOnlyMemory<byte> content,
        int pageCount,
        int? previousPageCount = null,
        CancellationToken cancellationToken = default)
    {
        if (pageCount < 1 || content.IsEmpty)
        {
            return null;
        }

        var budget = ocrOptions?.MaxPagesPerDocument ?? DefaultPageBudget;
        var pagesToRender = Math.Min(pageCount, budget);

        string? page1Path = null;

        for (var page = 1; page <= pagesToRender; page++)
        {
            // Outside PdfiumLock (held inside the renderer): a hang abort must be able to
            // observe cancellation between pages, not while native pdfium is in a lock.
            cancellationToken.ThrowIfCancellationRequested();
            if (progressHeartbeat is not null)
            {
                await progressHeartbeat.PulseBoundAsync(cancellationToken).ConfigureAwait(false);
            }
            else
            {
                hangWatch?.Heartbeat();
            }

            byte[]? png;
            try
            {
                png = renderer.Render(fileName, mimeType, content, page)
                    ?? PlaceholderDocumentPreviewRenderer.RenderPlaceholder("FILE");
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A throwing renderer must not take an admitted document down (ADR-029 clause 3).
                // If page 1 fails we have nothing to point PreviewPath at; stop.
                if (page == 1)
                {
                    return null;
                }

                continue;
            }

            try
            {
                using var buffer = new MemoryStream(png, writable: false);
                var path = await storage.SavePreviewPageAsync(tenantId, documentId, page, buffer, cancellationToken)
                    .ConfigureAwait(false);

                if (page == 1)
                {
                    page1Path = path;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (page == 1)
                {
                    return null;
                }
            }

            // Bitmap is a local variable; GC reclaims it here, before the next page is decoded
            // (ADR-005 w17 §19: never materialise a document's pages as a set).
        }

        // Reap surplus pages from a previous render that was longer (ADR-029 round-3 clause 2).
        if (previousPageCount.HasValue && previousPageCount.Value > pagesToRender && pagesToRender > 0)
        {
            await ReapSurplusPagesAsync(tenantId, documentId, pagesToRender, previousPageCount.Value, cancellationToken)
                .ConfigureAwait(false);
        }

        return page1Path;
    }

    /// <summary>
    /// Reads back one page of one document's preview, or <see langword="null"/> when the document
    /// does not exist for this tenant, has no preview, the page is out of range, or its preview
    /// object is gone.
    /// </summary>
    public async Task<byte[]?> LoadAsync(
        TenantId tenantId,
        EntityId documentId,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var row = await dbContext.Documents
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Id == documentId)
            .Select(d => new { d.PreviewPath, d.PageCount, d.StoragePath, d.FileName, d.MimeType })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (row is null)
        {
            return null;
        }

        // ADR-029 clause 5: out of range is null (→ 404), never a silent page 1.
        if (page < 1 || (row.PageCount.HasValue && page > row.PageCount.Value))
        {
            return null;
        }

        // Re-rasterise PDFs from the original bytes. Previews stored before white-compositing
        // encoded pdfium's transparent background as opaque black, so serving the stored PNG
        // would keep the viewer black even after the encoder fix.
        if (string.Equals(row.MimeType, DocumentFormatSniffer.PdfMimeType, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(row.StoragePath))
        {
            try
            {
                var original = await storage.LoadAsync(tenantId, row.StoragePath, cancellationToken)
                    .ConfigureAwait(false);
                if (original is { Length: > 0 })
                {
                    var png = renderer.Render(row.FileName, row.MimeType, original, page);
                    if (png is { Length: > 0 })
                    {
                        return png;
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Fall through to the stored preview rather than failing the GET.
            }
        }

        if (string.IsNullOrWhiteSpace(row.PreviewPath))
        {
            return null;
        }

        var path = DocumentStoragePath.BuildPreviewPage(tenantId, documentId, page);
        return await storage.LoadAsync(tenantId, path, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes <c>page-{n}.png</c> for <c>n &gt; newPageCount</c> and <c>n ≤ oldPageCount</c>,
    /// scoped strictly to this document's own preview prefix (ADR-009 w17 clause 8).
    /// </summary>
    private async Task ReapSurplusPagesAsync(
        TenantId tenantId,
        EntityId documentId,
        int newPageCount,
        int oldPageCount,
        CancellationToken cancellationToken)
    {
        for (var surplus = newPageCount + 1; surplus <= oldPageCount; surplus++)
        {
            try
            {
                var path = DocumentStoragePath.BuildPreviewPage(tenantId, documentId, surplus);
                await storage.DeleteAsync(tenantId, path, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort: a failed reap leaves an orphaned object but must not block the pipeline.
            }
        }
    }
}
