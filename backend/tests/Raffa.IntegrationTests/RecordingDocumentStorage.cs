using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;

namespace Raffa.IntegrationTests;

/// <summary>
/// Fake <see cref="IDocumentStorage"/> for <see cref="R0IntegrationFixture"/> — proves the
/// "storage" step of task E01/F09/US01/T01's AC-1 without a real Azure Blob/Azurite dependency,
/// mirroring <c>Raffa.Documents.Contracts.Tests.DocumentUploadServiceTests</c>'s own test double
/// of the same shape (ADR-005/ADR-011: domain code only ever sees <see cref="IDocumentStorage"/>,
/// so substituting the adapter at the test host's DI level is the sanctioned way to prove this
/// step without a real cloud dependency).
/// </summary>
public sealed class RecordingDocumentStorage : IDocumentStorage
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
