using Contigo.Documents.Contracts.Infrastructure;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Storage;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Contigo.Documents.Contracts.Application.Preview;

/// <summary>
/// Task E13/F04/US01/T02 (documents-v2-api, R-DOC-08): renders and stores a document's first-page
/// preview, and reads it back for <c>GET /api/documents/{id}/preview</c>.
///
/// <para>
/// The preview lives under the tenant's own object-storage prefix
/// (<see cref="DocumentStoragePath.BuildPreview"/>) and is streamed through this API under the
/// caller's tenant scope — the client never receives a blob URL (ADR-009). Storing the path on the
/// <c>document</c> row is what makes the read tenant-checked twice: the row is only visible under
/// RLS, and the path is re-validated against the tenant prefix on load.
/// </para>
///
/// <para>
/// <b>Never fails the upload.</b> <see cref="RenderAndStoreAsync"/> returns <see langword="null"/>
/// when rendering or storing fails; the caller simply leaves <c>PreviewPath</c> unset and the
/// endpoint answers 404 for that document's preview. A preview is a convenience, not a fact about
/// the contract.
/// </para>
/// </summary>
public sealed class DocumentPreviewService(
    DocumentsContractsDbContext dbContext,
    IDocumentStorage storage,
    IDocumentPreviewRenderer renderer,
    ITenantContext tenantContext)
{
    /// <summary>Content type every stored preview is served with (R-DOC-08, OpenAPI <c>image/png</c>).</summary>
    public const string PreviewContentType = "image/png";

    /// <summary>
    /// Renders the preview for the just-parsed bytes and stores it, returning the storage path
    /// (<see langword="null"/> when no preview could be produced). Does not save the document row:
    /// the caller owns that unit of work.
    /// </summary>
    public async Task<string?> RenderAndStoreAsync(
        TenantId tenantId,
        EntityId documentId,
        string fileName,
        string mimeType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        byte[]? png;
        try
        {
            png = renderer.Render(fileName, mimeType, content)
                ?? PlaceholderDocumentPreviewRenderer.RenderPlaceholder("FILE");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A renderer that throws (a native rasteriser refusing a malformed page, say) must not
            // take an already-admitted upload down with it.
            return null;
        }

        try
        {
            using var buffer = new MemoryStream(png, writable: false);
            return await storage.SavePreviewAsync(tenantId, documentId, buffer, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads back one document's preview, or <see langword="null"/> when the document does not
    /// exist for this tenant, has no preview, or its preview object is gone.
    /// </summary>
    public async Task<byte[]?> LoadAsync(
        TenantId tenantId, EntityId documentId, CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var previewPath = await dbContext.Documents
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId && d.Id == documentId)
            .Select(d => d.PreviewPath)
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(previewPath))
        {
            return null;
        }

        return await storage.LoadAsync(tenantId, previewPath, cancellationToken).ConfigureAwait(false);
    }
}
