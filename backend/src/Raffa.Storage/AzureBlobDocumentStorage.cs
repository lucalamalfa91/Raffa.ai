using Azure;
using Azure.Storage.Blobs;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;

namespace Raffa.Storage;

/// <summary>
/// Azure Blob Storage adapter for <see cref="IDocumentStorage"/> (ADR-005/ADR-009). Every path is
/// derived from <see cref="DocumentStoragePath"/>, and the read/delete side re-checks it against
/// the caller's tenant prefix before touching the container — see
/// <see cref="DocumentStoragePath.EnsureWithinTenant"/>.
///
/// Task E16/F02/US02/T01 (durable-queue-transport, ADR-027 §D11): moved out of
/// <c>Raffa.Api.Infrastructure</c> (where it was <c>internal sealed</c> to that host) into this
/// dedicated adapter project so <c>Raffa.Worker</c> — which must read the blob it is asked to OCR —
/// can resolve <see cref="IDocumentStorage"/> too. <c>public</c> rather than <c>internal</c>
/// because it is now shared by two hosts' own composition roots
/// (<see cref="StorageServiceCollectionExtensions"/>, called from both
/// <c>Raffa.Api/Program.cs</c> and <c>Raffa.Worker/WorkerServiceCollectionExtensions</c>), not
/// `internal` to either one.
/// </summary>
public sealed class AzureBlobDocumentStorage(BlobContainerClient container) : IDocumentStorage
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

    /// <summary>Task E22/F02/US01/T01 (ADR-029): one rendered page of the preview — deterministic
    /// path replaced in place on every reprocess (round-3 clause 1).</summary>
    public async Task<string> SavePreviewPageAsync(
        TenantId tenantId,
        EntityId documentId,
        int page,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var path = DocumentStoragePath.BuildPreviewPage(tenantId, documentId, page);
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
