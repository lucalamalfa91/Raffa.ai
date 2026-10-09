using System.Text;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// F5-D01: the same file (same SHA-256) uploaded twice by one tenant is one document, one Contract
/// and one extraction job; the second upload is answered "already uploaded" with the existing
/// document. Runs over EF InMemory, so without Docker; the unique index that backs the race is
/// asserted on the model in <see cref="ExtractionIdempotenceTests"/>.
/// </summary>
public sealed class DocumentUploadDedupeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Written { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Written.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingStorage : IDocumentStorage
    {
        public List<string> Saved { get; } = [];

        public Task<string> SaveAsync(
            TenantId tenantId, EntityId documentId, int versionNumber, string fileName, Stream content,
            CancellationToken cancellationToken = default)
        {
            var path = DocumentStoragePath.Build(tenantId, documentId, versionNumber, fileName);
            Saved.Add(path);
            return Task.FromResult(path);
        }

        public Task<string> SavePreviewAsync(
            TenantId tenantId, EntityId documentId, Stream content, CancellationToken cancellationToken = default) =>
            Task.FromResult(DocumentStoragePath.BuildPreview(tenantId, documentId));

        public Task<string> SavePreviewPageAsync(
            TenantId tenantId, EntityId documentId, int page, Stream content, CancellationToken cancellationToken = default) =>
            Task.FromResult(DocumentStoragePath.BuildPreviewPage(tenantId, documentId, page));

        public Task<byte[]?> LoadAsync(TenantId tenantId, string storagePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);

        public Task DeleteAsync(TenantId tenantId, string storagePath, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class Harness
    {
        private readonly string _databaseName = Guid.NewGuid().ToString();

        public RecordingStorage Storage { get; } = new();

        public InMemoryExtractionQueue Queue { get; } = new();

        public RecordingAuditWriter Audit { get; } = new();

        public TenantContext TenantContext { get; } = new();

        public DocumentsContractsDbContext CreateContext() => InMemoryDocumentsDb.Create(_databaseName);

        public async Task<Result<DocumentUploadResult>> UploadAsync(TenantId tenantId, string fileName, string content)
        {
            await using var db = CreateContext();
            var service = new DocumentUploadService(db, Storage, Queue, TenantContext, new FixedClock(), Audit);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
            return await service.UploadAsync(tenantId, fileName, "application/pdf", stream, "uploader@example.com");
        }
    }

    [Fact]
    public async Task The_same_file_twice_is_one_document_one_contract_one_job_and_the_second_answer_links_the_first()
    {
        var harness = new Harness();
        var tenantId = TenantId.New();

        var first = await harness.UploadAsync(tenantId, "msa.pdf", "%PDF-1.4 same bytes");
        var second = await harness.UploadAsync(tenantId, "msa-copy.pdf", "%PDF-1.4 same bytes");

        Assert.True(first.IsSuccess);
        Assert.False(first.Value.AlreadyUploaded);
        Assert.True(second.IsSuccess);
        Assert.True(second.Value.AlreadyUploaded);
        Assert.Equal(first.Value.DocumentId, second.Value.DocumentId); // the link to the existing document
        Assert.Equal("msa.pdf", second.Value.FileName);

        await using var db = harness.CreateContext();
        Assert.Equal(1, await db.Documents.CountAsync());
        Assert.Equal(1, await db.Contracts.CountAsync()); // one Contract: spend and renewals are not doubled
        Assert.Equal(1, await db.ExtractionJobs.CountAsync());
        Assert.Equal(1, await db.DocumentVersions.CountAsync());
        Assert.Equal(second.Value.ContractId, (await db.Documents.SingleAsync()).ContractId);

        // Nothing was stored or queued for the repeat, and it is on the audit trail.
        Assert.Single(harness.Storage.Saved);
        Assert.Single(harness.Queue.Published);
        Assert.Contains(harness.Audit.Written, e => e.Action == "document.upload_deduplicated");
    }

    [Fact]
    public async Task Different_content_in_the_same_tenant_is_a_new_document()
    {
        var harness = new Harness();
        var tenantId = TenantId.New();

        var first = await harness.UploadAsync(tenantId, "a.pdf", "%PDF-1.4 one");
        var second = await harness.UploadAsync(tenantId, "b.pdf", "%PDF-1.4 two");

        Assert.NotEqual(first.Value.DocumentId, second.Value.DocumentId);
        Assert.False(second.Value.AlreadyUploaded);
        await using var db = harness.CreateContext();
        Assert.Equal(2, await db.Documents.CountAsync());
        Assert.Equal(2, await db.Contracts.CountAsync());
    }

    [Fact]
    public async Task The_same_file_in_another_tenant_is_not_a_duplicate()
    {
        var harness = new Harness();

        var tenantA = await harness.UploadAsync(TenantId.New(), "msa.pdf", "%PDF-1.4 shared bytes");
        var tenantB = await harness.UploadAsync(TenantId.New(), "msa.pdf", "%PDF-1.4 shared bytes");

        Assert.False(tenantB.Value.AlreadyUploaded);
        Assert.NotEqual(tenantA.Value.DocumentId, tenantB.Value.DocumentId);
    }

    [Fact]
    public async Task A_rejected_upload_can_be_uploaded_again()
    {
        var harness = new Harness();
        var tenantId = TenantId.New();
        var first = await harness.UploadAsync(tenantId, "notes.pdf", "%PDF-1.4 not a contract");

        await using (var db = harness.CreateContext())
        {
            var document = await db.Documents.SingleAsync();
            document.ProcessingStatus = DocumentProcessingStatus.Rejected; // the blob is gone; "upload it again"
            await db.SaveChangesAsync();
        }

        var again = await harness.UploadAsync(tenantId, "notes.pdf", "%PDF-1.4 not a contract");

        Assert.False(again.Value.AlreadyUploaded);
        Assert.NotEqual(first.Value.DocumentId, again.Value.DocumentId);
    }

    [Fact]
    public async Task A_duplicate_of_a_document_still_processing_or_failed_points_at_that_document()
    {
        var harness = new Harness();
        var tenantId = TenantId.New();
        var first = await harness.UploadAsync(tenantId, "msa.pdf", "%PDF-1.4 bytes");

        await using (var db = harness.CreateContext())
        {
            (await db.Documents.SingleAsync()).ProcessingStatus = DocumentProcessingStatus.Failed;
            await db.SaveChangesAsync();
        }

        var again = await harness.UploadAsync(tenantId, "msa.pdf", "%PDF-1.4 bytes");

        Assert.True(again.Value.AlreadyUploaded);
        Assert.Equal(first.Value.DocumentId, again.Value.DocumentId);
        Assert.Equal(DocumentProcessingStatus.Failed, again.Value.ProcessingStatus); // reprocess it, do not re-upload
    }
}
