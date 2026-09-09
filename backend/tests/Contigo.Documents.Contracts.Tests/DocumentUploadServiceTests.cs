using System.Security.Cryptography;
using System.Text;
using Contigo.Documents.Contracts.Application;
using Contigo.Documents.Contracts.Domain;
using Contigo.Documents.Contracts.Infrastructure;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Storage;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Contigo.Documents.Contracts.Tests;

/// <summary>
/// Proves the Definition of Done for task E01/F06/US01/T01 (us-01-document-upload, AC-1/AC-2):
/// <see cref="DocumentUploadService.UploadAsync"/> stores the uploaded bytes in tenant-scoped
/// object storage (no cross-tenant path) and persists the Document/DocumentVersion/queued
/// classification-job metadata as one unit of work, against a real Postgres+RLS database — not
/// an in-memory provider that would silently ignore the RLS policy entirely.
///
/// Runs all assertions through a dedicated, deliberately unprivileged Postgres role
/// (<see cref="AppRoleName"/>: `NOSUPERUSER NOBYPASSRLS`, not the table owner), mirroring
/// <c>Contigo.Tenancy.Tests.TenantRlsCrossTenantIsolationTests</c> and
/// <c>Contigo.Identity.Workspace.Tests.WorkspaceRlsCrossTenantIsolationTests</c>. The
/// Testcontainers bootstrap role is always a Postgres superuser, and superusers unconditionally
/// bypass row security — asserting cross-tenant isolation over that connection would pass
/// vacuously. This role stands in for "the application's own database role", so a passing test
/// here is a real proof, not a tautology.
/// </summary>
public sealed class DocumentUploadServiceTests : IAsyncLifetime
{
    private const string AppRoleName = "contigo_upload_app";
    private const string AppRolePassword = "contigo_upload_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16")
        .Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using (var adminDb = new DocumentsContractsDbContext(adminOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity (covers document/document_version/
            // extraction_job — see that migration's TenantScopedTables list).
            await adminDb.Database.MigrateAsync();

            await adminDb.Database.ExecuteSqlRawAsync(
                $"""
                CREATE ROLE {AppRoleName} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS;
                GRANT USAGE ON SCHEMA public TO {AppRoleName};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRoleName};
                """);
        }

        _appConnectionString = new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Username = AppRoleName,
            Password = AppRolePassword,
        }.ConnectionString;
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private DocumentsContractsDbContext CreateAppContext(ITenantContext tenantContext) =>
        CreateAppContext(_appConnectionString, tenantContext);

    private static DocumentsContractsDbContext CreateAppContext(string connectionString, ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, connectionString, tenantContext);
        return new DocumentsContractsDbContext(optionsBuilder.Options);
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

    private sealed class RecordingDocumentStorage : IDocumentStorage
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

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    [Fact]
    public async Task Upload_persists_document_version_and_a_queued_classification_job()
    {
        var tenantId = TenantId.New();
        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.4 sample contract bytes");
        var expectedChecksum = Convert.ToHexString(SHA256.HashData(bytes));
        var storage = new RecordingDocumentStorage();
        var auditWriter = new RecordingAuditWriter();

        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new DocumentUploadService(db, storage, tenantContext, new FixedClock(now), auditWriter);

        using var content = new MemoryStream(bytes);
        var result = await service.UploadAsync(tenantId, "contract.pdf", "application/pdf", content);

        Assert.True(result.IsSuccess);
        var uploaded = result.Value;
        Assert.Equal("contract.pdf", uploaded.FileName);
        Assert.Equal("application/pdf", uploaded.MimeType);
        Assert.Equal(DocumentProcessingStatus.Uploaded, uploaded.ProcessingStatus);
        Assert.Equal(now, uploaded.CreatedAt);

        // AC-1: bytes actually reached storage, under a tenant-prefixed path.
        var saved = Assert.Single(storage.Saved);
        Assert.StartsWith($"{tenantId.Value:D}/", saved.Path, StringComparison.Ordinal);
        Assert.Equal(bytes, saved.Content);

        // Task E01/F09/US01/T01 (r0-integration) AC-1 "upload document -> audit event": exactly
        // one audit entry, for this tenant, this document, this action.
        var auditEntry = Assert.Single(auditWriter.Written);
        Assert.Equal(tenantId, auditEntry.TenantId);
        Assert.Equal("document.uploaded", auditEntry.Action);
        Assert.Equal("document", auditEntry.ResourceType);
        Assert.Equal(uploaded.DocumentId.Value.ToString(), auditEntry.ResourceId);
        Assert.Equal(now, auditEntry.Timestamp);

        // AC-2: metadata + processing status + job, read back under the same tenant's scope.
        using (tenantContext.BeginScope(tenantId))
        {
            await using var readDb = CreateAppContext(tenantContext);

            var document = await readDb.Documents.SingleAsync(d => d.Id == uploaded.DocumentId);
            Assert.Equal(tenantId, document.TenantId);
            Assert.Equal(saved.Path, document.StoragePath);
            Assert.Equal(expectedChecksum, document.Checksum);
            Assert.Equal(DocumentProcessingStatus.Uploaded, document.ProcessingStatus);

            var version = await readDb.DocumentVersions.SingleAsync(v => v.DocumentId == uploaded.DocumentId);
            Assert.Equal(1, version.VersionNumber);
            Assert.Equal(saved.Path, version.StoragePath);
            Assert.Equal(expectedChecksum, version.Checksum);

            var job = await readDb.ExtractionJobs.SingleAsync(j => j.DocumentId == uploaded.DocumentId);
            Assert.Equal(ExtractionStage.Classification, job.Stage);
            Assert.Equal(ExtractionJobStatus.Queued, job.Status);
            Assert.Equal(now, job.QueuedAt);
        }
    }

    [Fact]
    public async Task Uploading_an_empty_file_fails_and_writes_nothing()
    {
        var tenantId = TenantId.New();
        var storage = new RecordingDocumentStorage();
        var auditWriter = new RecordingAuditWriter();
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new DocumentUploadService(db, storage, tenantContext, new FixedClock(DateTimeOffset.UtcNow), auditWriter);

        using var emptyContent = new MemoryStream();
        var result = await service.UploadAsync(tenantId, "empty.pdf", "application/pdf", emptyContent);

        Assert.True(result.IsFailure);
        Assert.Empty(storage.Saved);
        Assert.Empty(auditWriter.Written);

        using (tenantContext.BeginScope(tenantId))
        {
            await using var readDb = CreateAppContext(tenantContext);
            Assert.Empty(await readDb.Documents.ToListAsync());
        }
    }

    [Fact]
    public async Task A_different_tenant_cannot_see_the_uploaded_document_job_or_version()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var storage = new RecordingDocumentStorage();
        var tenantContext = new TenantContext();

        await using (var db = CreateAppContext(tenantContext))
        {
            var service = new DocumentUploadService(
                db, storage, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());
            using var content = new MemoryStream("owned-by-tenant-a"u8.ToArray());
            var result = await service.UploadAsync(tenantA, "contract.pdf", "application/pdf", content);
            Assert.True(result.IsSuccess);
        }

        // AC-1/ADR-009: tenant A's rows exist (seeded above, over the same tables) but RLS makes
        // them invisible on a connection scoped to tenant B — real cross-tenant proof, not just
        // "the storage path happens to differ".
        using (tenantContext.BeginScope(tenantB))
        {
            await using var dbAsTenantB = CreateAppContext(tenantContext);

            Assert.Empty(await dbAsTenantB.Documents.ToListAsync());
            Assert.Empty(await dbAsTenantB.DocumentVersions.ToListAsync());
            Assert.Empty(await dbAsTenantB.ExtractionJobs.ToListAsync());
        }
    }
}
