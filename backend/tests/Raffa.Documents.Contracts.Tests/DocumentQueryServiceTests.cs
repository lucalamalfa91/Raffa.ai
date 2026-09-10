using System.Text;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Storage;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Proves the Definition of Done for task E01/F06/US01/T02 (us-01-document-upload, AC-3):
/// <see cref="DocumentQueryService.GetByIdAsync"/> reads back the metadata + processing status
/// that task E01/F06/US01/T01's <see cref="DocumentUploadService"/> persisted (AC-2), scoped to
/// the caller's tenant, against a real Postgres+RLS database — mirrors
/// <see cref="DocumentUploadServiceTests"/>'s own unprivileged-role rationale so a passing
/// "a different tenant gets nothing back" assertion is a real RLS proof, not a tautology from a
/// superuser connection that unconditionally bypasses row security.
/// </summary>
public sealed class DocumentQueryServiceTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_query_app";
    private const string AppRolePassword = "raffa_query_app_test_password";

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

    private DocumentsContractsDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new DocumentsContractsDbContext(optionsBuilder.Options);
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

    /// <summary>
    /// This test class only proves the read side (<see cref="DocumentQueryService"/>); the audit
    /// write task E01/F09/US01/T01 added to <see cref="DocumentUploadService"/> is proven by
    /// <c>Raffa.Documents.Contracts.Tests.DocumentUploadServiceTests</c>'s own
    /// <c>RecordingAuditWriter</c> instead — this one just needs the seed upload to succeed.
    /// </summary>
    private sealed class NoOpAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    /// <summary>Seeds a document via the real upload path (task T01) so this test proves a read
    /// back of genuinely persisted data, not a hand-inserted fixture row.</summary>
    private async Task<EntityId> SeedDocumentAsync(
        ITenantContext tenantContext, TenantId tenantId, string fileName, DateTimeOffset now)
    {
        await using var db = CreateAppContext(tenantContext);
        var uploadService = new DocumentUploadService(
            db, new RecordingDocumentStorage(), tenantContext, new FixedClock(now), new NoOpAuditWriter());

        using var content = new MemoryStream(Encoding.UTF8.GetBytes($"%PDF-1.4 {fileName}"));
        var result = await uploadService.UploadAsync(tenantId, fileName, "application/pdf", content);
        Assert.True(result.IsSuccess);
        return result.Value.DocumentId;
    }

    [Fact]
    public async Task Returns_the_persisted_metadata_and_status_for_the_owning_tenant()
    {
        var tenantId = TenantId.New();
        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);
        var tenantContext = new TenantContext();

        var documentId = await SeedDocumentAsync(tenantContext, tenantId, "contract.pdf", now);

        await using var db = CreateAppContext(tenantContext);
        var queryService = new DocumentQueryService(db, tenantContext);

        // AC-3: GET /api/documents/{id} returns metadata/status for the caller's tenant.
        var metadata = await queryService.GetByIdAsync(tenantId, documentId);

        Assert.NotNull(metadata);
        Assert.Equal(documentId, metadata!.DocumentId);
        Assert.Null(metadata.ContractId);
        Assert.Equal("contract.pdf", metadata.FileName);
        Assert.Equal("application/pdf", metadata.MimeType);
        Assert.Equal(ContractDocumentType.Other, metadata.DocumentType);
        Assert.Equal(DocumentProcessingStatus.Uploaded, metadata.ProcessingStatus);
        Assert.Equal(now, metadata.CreatedAt);
    }

    [Fact]
    public async Task Returns_null_for_a_document_that_belongs_to_a_different_tenant()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var tenantContext = new TenantContext();

        var documentId = await SeedDocumentAsync(tenantContext, tenantA, "contract.pdf", DateTimeOffset.UtcNow);

        await using var db = CreateAppContext(tenantContext);
        var queryService = new DocumentQueryService(db, tenantContext);

        // AC-3 "for the caller's tenant": tenant B must not be able to read tenant A's document,
        // even though the row genuinely exists (seeded above) — both the app-level tenant
        // predicate and Postgres RLS independently deny it.
        var metadata = await queryService.GetByIdAsync(tenantB, documentId);

        Assert.Null(metadata);
    }

    [Fact]
    public async Task Returns_null_for_an_id_that_does_not_exist()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();

        await using var db = CreateAppContext(tenantContext);
        var queryService = new DocumentQueryService(db, tenantContext);

        var metadata = await queryService.GetByIdAsync(tenantId, EntityId.New());

        Assert.Null(metadata);
    }
}
