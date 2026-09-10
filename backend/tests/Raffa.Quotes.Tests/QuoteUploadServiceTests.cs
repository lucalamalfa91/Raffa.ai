using System.Security.Cryptography;
using System.Text;
using Raffa.Quotes.Application;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.Quotes.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Proves the Definition of Done for task E05/F01/US01/T01 (quote-extraction, AC-1): "POST
/// /api/quotes uploads a quote and creates an extraction job". <see cref="QuoteUploadService.UploadAsync"/>
/// stores the uploaded bytes in tenant-scoped object storage (no cross-tenant path) and persists
/// the <see cref="Quote"/>/queued <see cref="QuoteExtractionJob"/> as one unit of work, against a
/// real Postgres+RLS database — not an in-memory provider that would silently ignore the RLS
/// policy entirely. Mirrors
/// <c>Raffa.Documents.Contracts.Tests.DocumentUploadServiceTests</c> exactly, scoped to this
/// module's own <see cref="QuotesDbContext"/>.
///
/// Runs all assertions through a dedicated, deliberately unprivileged Postgres role
/// (<see cref="AppRoleName"/>: `NOSUPERUSER NOBYPASSRLS`, not the table owner) — the Testcontainers
/// bootstrap role is always a superuser, and superusers unconditionally bypass row security, so
/// asserting cross-tenant isolation over that connection would pass vacuously.
/// </summary>
public sealed class QuoteUploadServiceTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_quote_upload_app";
    private const string AppRolePassword = "raffa_quote_upload_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());

        await using (var adminDb = new QuotesDbContext(adminOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity (covers quote/quote_extraction_job/
            // quote_line — see that migration's TenantScopedTables list).
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

    private QuotesDbContext CreateAppContext(ITenantContext tenantContext)
    {
        var optionsBuilder = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(optionsBuilder, _appConnectionString, tenantContext);
        return new QuotesDbContext(optionsBuilder.Options);
    }

    [Fact]
    public async Task Upload_persists_quote_and_a_queued_extraction_job()
    {
        var tenantId = TenantId.New();
        var now = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.4 sample quote bytes");
        var expectedChecksum = Convert.ToHexString(SHA256.HashData(bytes));
        var storage = new RecordingDocumentStorage();
        var auditWriter = new RecordingAuditWriter();

        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new QuoteUploadService(db, storage, tenantContext, new FixedClock(now), auditWriter);

        using var content = new MemoryStream(bytes);
        var result = await service.UploadAsync(tenantId, "quote.pdf", "application/pdf", content);

        Assert.True(result.IsSuccess);
        var uploaded = result.Value;
        Assert.Equal("quote.pdf", uploaded.FileName);
        Assert.Equal("application/pdf", uploaded.MimeType);
        Assert.Equal(QuoteProcessingStatus.Uploaded, uploaded.ProcessingStatus);
        Assert.Equal(now, uploaded.CreatedAt);

        // AC-1: bytes actually reached storage, under a tenant-prefixed path.
        var saved = Assert.Single(storage.Saved);
        Assert.StartsWith($"{tenantId.Value:D}/", saved.Path, StringComparison.Ordinal);
        Assert.Equal(bytes, saved.Content);

        var auditEntry = Assert.Single(auditWriter.Written);
        Assert.Equal(tenantId, auditEntry.TenantId);
        Assert.Equal("quote.uploaded", auditEntry.Action);
        Assert.Equal("quote", auditEntry.ResourceType);
        Assert.Equal(uploaded.QuoteId.Value.ToString(), auditEntry.ResourceId);
        Assert.Equal(now, auditEntry.Timestamp);

        using (tenantContext.BeginScope(tenantId))
        {
            await using var readDb = CreateAppContext(tenantContext);

            var quote = await readDb.Quotes.SingleAsync(q => q.Id == uploaded.QuoteId);
            Assert.Equal(tenantId, quote.TenantId);
            Assert.Equal(saved.Path, quote.StoragePath);
            Assert.Equal(expectedChecksum, quote.Checksum);
            Assert.Equal(QuoteProcessingStatus.Uploaded, quote.ProcessingStatus);

            // AC-1 "...and creates an extraction job".
            var job = await readDb.QuoteExtractionJobs.SingleAsync(j => j.QuoteId == uploaded.QuoteId);
            Assert.Equal(QuoteExtractionJobStatus.Queued, job.Status);
            Assert.Equal(now, job.QueuedAt);
            Assert.Null(job.CompletedAt);
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
        var service = new QuoteUploadService(db, storage, tenantContext, new FixedClock(DateTimeOffset.UtcNow), auditWriter);

        using var emptyContent = new MemoryStream();
        var result = await service.UploadAsync(tenantId, "empty.pdf", "application/pdf", emptyContent);

        Assert.True(result.IsFailure);
        Assert.Empty(storage.Saved);
        Assert.Empty(auditWriter.Written);

        using (tenantContext.BeginScope(tenantId))
        {
            await using var readDb = CreateAppContext(tenantContext);
            Assert.Empty(await readDb.Quotes.ToListAsync());
        }
    }

    [Fact]
    public async Task A_different_tenant_cannot_see_the_uploaded_quote_or_job()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var storage = new RecordingDocumentStorage();
        var tenantContext = new TenantContext();

        await using (var db = CreateAppContext(tenantContext))
        {
            var service = new QuoteUploadService(
                db, storage, tenantContext, new FixedClock(DateTimeOffset.UtcNow), new RecordingAuditWriter());
            using var content = new MemoryStream("owned-by-tenant-a"u8.ToArray());
            var result = await service.UploadAsync(tenantA, "quote.pdf", "application/pdf", content);
            Assert.True(result.IsSuccess);
        }

        using (tenantContext.BeginScope(tenantB))
        {
            await using var dbAsTenantB = CreateAppContext(tenantContext);

            Assert.Empty(await dbAsTenantB.Quotes.ToListAsync());
            Assert.Empty(await dbAsTenantB.QuoteExtractionJobs.ToListAsync());
        }
    }
}
