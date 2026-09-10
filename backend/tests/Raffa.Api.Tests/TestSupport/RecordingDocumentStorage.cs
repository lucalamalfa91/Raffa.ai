using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;

namespace Raffa.Api.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="IDocumentStorage"/> for the in-memory API host (task E13/F04/US01/T01):
/// records every save so a test can prove a rejected upload never reached storage, and an admitted
/// one did — the same shape <c>Raffa.IntegrationTests.RecordingDocumentStorage</c> gives the
/// Testcontainers hosts.
/// </summary>
internal sealed class RecordingDocumentStorage : IDocumentStorage
{
    public List<(string Path, byte[] Content)> Saved { get; } = [];

    /// <summary>Paths this fake was asked to delete, in order (R-DOC-10 assertions).</summary>
    public List<string> Deleted { get; } = [];

    /// <summary>What is actually stored right now, keyed by path — so a load after a save really
    /// returns the same bytes and a load after a delete really returns null (task E13/F04/US01/T02).</summary>
    private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

    public async Task<string> SaveAsync(
        TenantId tenantId,
        EntityId documentId,
        int versionNumber,
        string fileName,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var path = DocumentStoragePath.Build(tenantId, documentId, versionNumber, fileName);
        return await StoreAsync(path, content, cancellationToken).ConfigureAwait(false);
    }

    public async Task<string> SavePreviewAsync(
        TenantId tenantId,
        EntityId documentId,
        Stream content,
        CancellationToken cancellationToken = default)
    {
        var path = DocumentStoragePath.BuildPreview(tenantId, documentId);
        return await StoreAsync(path, content, cancellationToken).ConfigureAwait(false);
    }

    public Task<byte[]?> LoadAsync(
        TenantId tenantId, string storagePath, CancellationToken cancellationToken = default)
    {
        DocumentStoragePath.EnsureWithinTenant(tenantId, storagePath);
        return Task.FromResult(_objects.TryGetValue(storagePath, out var bytes) ? bytes : null);
    }

    public Task DeleteAsync(
        TenantId tenantId, string storagePath, CancellationToken cancellationToken = default)
    {
        DocumentStoragePath.EnsureWithinTenant(tenantId, storagePath);
        Deleted.Add(storagePath);
        _objects.Remove(storagePath);
        return Task.CompletedTask;
    }

    private async Task<string> StoreAsync(string path, Stream content, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var bytes = buffer.ToArray();

        Saved.Add((path, bytes));
        _objects[path] = bytes;
        return path;
    }
}
