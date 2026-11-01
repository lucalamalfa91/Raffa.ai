using System.Collections.Concurrent;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;

namespace Raffa.Storage;

/// <summary>
/// Process-local <see cref="IDocumentStorage"/> for the Postgres-only <c>Parity</c> environment
/// (<c>ConnectionStrings:Storage = InMemory</c>): same tenant-prefixed paths and tenant checks as
/// the blob adapter, bytes kept in memory and lost on restart. Never used by a deployed environment.
/// </summary>
public sealed class InMemoryDocumentStorage : IDocumentStorage
{
    private readonly ConcurrentDictionary<string, byte[]> _blobs = new(StringComparer.Ordinal);

    public Task<string> SaveAsync(TenantId tenantId, EntityId documentId, int versionNumber, string fileName, Stream content, CancellationToken cancellationToken = default) =>
        StoreAsync(DocumentStoragePath.Build(tenantId, documentId, versionNumber, fileName), content, cancellationToken);

    public Task<byte[]?> LoadAsync(TenantId tenantId, string storagePath, CancellationToken cancellationToken = default)
    {
        DocumentStoragePath.EnsureWithinTenant(tenantId, storagePath);
        return Task.FromResult(_blobs.TryGetValue(storagePath, out var bytes) ? bytes.ToArray() : null);
    }

    public Task DeleteAsync(TenantId tenantId, string storagePath, CancellationToken cancellationToken = default)
    {
        DocumentStoragePath.EnsureWithinTenant(tenantId, storagePath);
        _blobs.TryRemove(storagePath, out _);
        return Task.CompletedTask;
    }

    public Task<string> SavePreviewAsync(TenantId tenantId, EntityId documentId, Stream content, CancellationToken cancellationToken = default) =>
        StoreAsync(DocumentStoragePath.BuildPreview(tenantId, documentId), content, cancellationToken);

    public Task<string> SavePreviewPageAsync(TenantId tenantId, EntityId documentId, int page, Stream content, CancellationToken cancellationToken = default) =>
        StoreAsync(DocumentStoragePath.BuildPreviewPage(tenantId, documentId, page), content, cancellationToken);

    /// <summary>Drops every blob of <paramref name="tenantId"/> (fixture reset).</summary>
    public void Clear(TenantId tenantId)
    {
        var prefix = DocumentStoragePath.TenantPrefix(tenantId);
        foreach (var key in _blobs.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)))
        {
            _blobs.TryRemove(key, out _);
        }
    }

    private async Task<string> StoreAsync(string path, Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        _blobs[path] = buffer.ToArray();
        return path;
    }
}
