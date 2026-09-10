using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Task E13/F04/US01/T02: the tenant-prefix rules every <see cref="IDocumentStorage"/> read or
/// delete must obey (ADR-009), and the save-then-load round trip the reprocess path depends on
/// (R-DOC-07).
/// </summary>
public sealed class DocumentStorageContractTests
{
    private static readonly TenantId TenantA = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly TenantId TenantB = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    [Fact]
    public void Preview_path_is_tenant_prefixed_and_distinct_from_a_document_version_path()
    {
        var documentId = EntityId.New();

        var preview = DocumentStoragePath.BuildPreview(TenantA, documentId);
        var version = DocumentStoragePath.Build(TenantA, documentId, 1, "msa.pdf");

        Assert.StartsWith($"{TenantA.Value:D}/", preview, StringComparison.Ordinal);
        Assert.EndsWith("/preview/page-1.png", preview, StringComparison.Ordinal);
        Assert.NotEqual(version, preview);
        Assert.DoesNotContain("/v1/", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void Ensure_within_tenant_accepts_this_tenants_own_paths_and_refuses_every_other()
    {
        var documentId = EntityId.New();
        var ownPath = DocumentStoragePath.Build(TenantA, documentId, 1, "msa.pdf");

        DocumentStoragePath.EnsureWithinTenant(TenantA, ownPath);

        Assert.Throws<InvalidOperationException>(() => DocumentStoragePath.EnsureWithinTenant(TenantB, ownPath));
        Assert.Throws<InvalidOperationException>(
            () => DocumentStoragePath.EnsureWithinTenant(TenantA, "../" + ownPath));
        Assert.Throws<ArgumentException>(() => DocumentStoragePath.EnsureWithinTenant(TenantA, "  "));
    }

    [Fact]
    public async Task Save_then_load_round_trips_the_bytes_and_delete_makes_them_gone()
    {
        var storage = new InMemoryDocumentStorage();
        var documentId = EntityId.New();
        var bytes = "%PDF-1.4 contract"u8.ToArray();

        using var content = new MemoryStream(bytes);
        var path = await storage.SaveAsync(TenantA, documentId, 1, "msa.pdf", content);

        Assert.Equal(bytes, await storage.LoadAsync(TenantA, path));

        await storage.DeleteAsync(TenantA, path);
        Assert.Null(await storage.LoadAsync(TenantA, path));

        // Idempotent: deleting what is already gone is not an error (R-DOC-10 retry).
        await storage.DeleteAsync(TenantA, path);
    }

    [Fact]
    public async Task Another_tenant_cannot_read_or_delete_this_tenants_object()
    {
        var storage = new InMemoryDocumentStorage();
        var documentId = EntityId.New();
        using var content = new MemoryStream("owned-by-a"u8.ToArray());
        var path = await storage.SaveAsync(TenantA, documentId, 1, "msa.pdf", content);

        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.LoadAsync(TenantB, path));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.DeleteAsync(TenantB, path));
        Assert.NotNull(await storage.LoadAsync(TenantA, path));
    }

    [Fact]
    public async Task A_missing_object_loads_as_null_rather_than_throwing()
    {
        var storage = new InMemoryDocumentStorage();
        var path = DocumentStoragePath.Build(TenantA, EntityId.New(), 1, "never-saved.pdf");

        Assert.Null(await storage.LoadAsync(TenantA, path));
    }

    /// <summary>The same in-memory adapter shape every test host uses, exercised here as the
    /// contract's reference implementation.</summary>
    private sealed class InMemoryDocumentStorage : IDocumentStorage
    {
        private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

        public async Task<string> SaveAsync(
            TenantId tenantId,
            EntityId documentId,
            int versionNumber,
            string fileName,
            Stream content,
            CancellationToken cancellationToken = default) =>
            await StoreAsync(
                DocumentStoragePath.Build(tenantId, documentId, versionNumber, fileName), content, cancellationToken);

        public async Task<string> SavePreviewAsync(
            TenantId tenantId, EntityId documentId, Stream content, CancellationToken cancellationToken = default) =>
            await StoreAsync(DocumentStoragePath.BuildPreview(tenantId, documentId), content, cancellationToken);

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
            _objects.Remove(storagePath);
            return Task.CompletedTask;
        }

        private async Task<string> StoreAsync(string path, Stream content, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            _objects[path] = buffer.ToArray();
            return path;
        }
    }
}
