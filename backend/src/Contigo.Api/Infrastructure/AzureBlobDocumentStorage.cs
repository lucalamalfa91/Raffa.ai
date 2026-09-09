using Azure;
using Azure.Storage.Blobs;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Storage;

namespace Contigo.Api.Infrastructure;

/// <summary>
/// Azure Blob Storage adapter for <see cref="IDocumentStorage"/> (ADR-005/ADR-009). Every path is
/// derived from <see cref="DocumentStoragePath"/>, and the read/delete side re-checks it against
/// the caller's tenant prefix before touching the container — see
/// <see cref="DocumentStoragePath.EnsureWithinTenant"/>.
/// </summary>
internal sealed class AzureBlobDocumentStorage(BlobContainerClient container) : IDocumentStorage
{
    public async Task<string> SaveAsync(
        TenantId tenantId,
        EntityId documentId,
        int versionNumber,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var path = DocumentStoragePath.Build(tenantId, documentId, versionNumber, fileName);
        return await UploadAsync(path, content, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Task E13/F04/US01/T02 (R-DOC-08): the rendered first-page preview, under the same
    /// tenant prefix and replaced in place on every reprocess.</summary>
    public async Task<string> SavePreviewAsync(
        TenantId tenantId,
        EntityId documentId,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var path = DocumentStoragePath.BuildPreview(tenantId, documentId);
        return await UploadAsync(path, content, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Task E13/F04/US01/T02 (R-DOC-07): reads the bytes back for a reprocess or a preview
    /// stream; a missing blob is <see langword="null"/>, a cross-tenant path throws.</summary>
    public async Task<byte[]?> LoadAsync(
        TenantId tenantId, string storagePath, CancellationToken cancellationToken = default)
    {
        DocumentStoragePath.EnsureWithinTenant(tenantId, storagePath);

        var blob = container.GetBlobClient(storagePath);
        try
        {
            var response = await blob.DownloadContentAsync(cancellationToken).ConfigureAwait(false);
            return response.Value.Content.ToArray();
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    /// <summary>Task E13/F04/US01/T02 (R-DOC-10): idempotent delete — an already-absent blob is a
    /// success, so a retried deletion completes instead of failing halfway.</summary>
    public async Task DeleteAsync(
        TenantId tenantId, string storagePath, CancellationToken cancellationToken = default)
    {
        DocumentStoragePath.EnsureWithinTenant(tenantId, storagePath);

        var blob = container.GetBlobClient(storagePath);
        await blob.DeleteIfExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> UploadAsync(string path, Stream content, CancellationToken cancellationToken)
    {
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        var blob = container.GetBlobClient(path);
        await blob.UploadAsync(content, overwrite: true, cancellationToken).ConfigureAwait(false);
        return path;
    }
}
