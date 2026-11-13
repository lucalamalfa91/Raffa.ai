using System.Text;
using Microsoft.EntityFrameworkCore;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Contracts;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Renewal plan F1: the signed contract of a renewed case is filed as a document of the existing
/// contract — completed, no extraction job, no second contract — and the same file is never stored twice.
/// </summary>
public sealed class ContractDocumentAttacherTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 13, 12, 0, 0, TimeSpan.Zero);
    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly TenantId _tenant = TenantId.New();
    private readonly TenantContext _tenantContext = new();
    private readonly MemoryStorage _storage = new();
    private readonly Audit _audit = new();

    [Fact]
    public async Task The_signed_contract_becomes_a_completed_document_of_the_existing_contract()
    {
        var contractId = await SeedContractAsync();
        var bytes = Encoding.UTF8.GetBytes("%PDF signed");
        var id = await Attacher().AttachSignedAsync(_tenant, new SignedContractDocument(contractId, "Splunk_OrderForm_signed.pdf", "application/pdf", bytes, "Marta"));

        await using var db = InMemoryDocumentsDb.Create(_databaseName);
        var document = await db.Documents.SingleAsync();
        Assert.Equal(id, document.Id.Value);
        Assert.Equal(contractId, document.ContractId!.Value.Value);
        Assert.Equal(DocumentProcessingStatus.Completed, document.ProcessingStatus);
        Assert.Equal(ContractDocumentType.OrderForm, document.DocumentType);
        Assert.Equal(bytes, _storage.Blobs[document.StoragePath]);
        Assert.Equal("Marta", (await db.DocumentVersions.SingleAsync()).CreatedBy);
        Assert.Equal(1, await db.Contracts.CountAsync());
        Assert.Empty(await db.ExtractionJobs.ToListAsync());
        Assert.Equal("document.renewal_signed_attached", Assert.Single(_audit.Written).Action);

        // The same file again: the existing document, nothing stored twice.
        var again = await Attacher().AttachSignedAsync(_tenant, new SignedContractDocument(contractId, "copy.pdf", "application/pdf", bytes, "Marta"));
        Assert.Equal(id, again);
        Assert.Single(_storage.Blobs);
    }

    [Fact]
    public async Task An_unknown_contract_is_refused()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Attacher().AttachSignedAsync(_tenant, new SignedContractDocument(Guid.NewGuid(), "x.pdf", "application/pdf", [1], "Marta")));
        Assert.Empty(_storage.Blobs);
    }

    private ContractDocumentAttacher Attacher() =>
        new(InMemoryDocumentsDb.Create(_databaseName), _storage, _tenantContext, new Clock(), _audit);

    private async Task<Guid> SeedContractAsync()
    {
        await using var db = InMemoryDocumentsDb.Create(_databaseName);
        var contract = new Contract { TenantId = _tenant, Type = ContractDocumentType.Msa, Status = "active", Currency = "USD", CreatedAt = Now };
        db.Contracts.Add(contract);
        await db.SaveChangesAsync();
        return contract.Id.Value;
    }

    private sealed class Clock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class Audit : IAuditWriter
    {
        public List<AuditEntry> Written { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Written.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryStorage : IDocumentStorage
    {
        public Dictionary<string, byte[]> Blobs { get; } = new(StringComparer.Ordinal);

        public async Task<string> SaveAsync(TenantId tenantId, EntityId documentId, int versionNumber, string fileName, Stream content, CancellationToken cancellationToken = default)
        {
            var path = DocumentStoragePath.Build(tenantId, documentId, versionNumber, fileName);
            using var buffer = new MemoryStream();
            await content.CopyToAsync(buffer, cancellationToken);
            Blobs[path] = buffer.ToArray();
            return path;
        }

        public Task<byte[]?> LoadAsync(TenantId tenantId, string storagePath, CancellationToken cancellationToken = default) =>
            Task.FromResult(Blobs.TryGetValue(storagePath, out var b) ? b : null);

        public Task DeleteAsync(TenantId tenantId, string storagePath, CancellationToken cancellationToken = default)
        {
            Blobs.Remove(storagePath);
            return Task.CompletedTask;
        }

        public Task<string> SavePreviewAsync(TenantId tenantId, EntityId documentId, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<string> SavePreviewPageAsync(TenantId tenantId, EntityId documentId, int page, Stream content, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
