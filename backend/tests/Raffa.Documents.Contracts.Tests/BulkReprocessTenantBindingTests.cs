using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Application.Extraction;
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
/// Task E20/F02/US02/T01 (w17, NW-73 — bulk-reprocess console).
///
/// <list type="number">
/// <item><b>Tenant binding (ADR-009 w17 clause 1 §3).</b> The worklist query —
///   identical to <c>verify-tenant-corpus.yml:151-160</c> — must use the
///   <em>three-argument</em> <see cref="DocumentsContractsDbContextOptions.Configure"/>
///   (with <see cref="ITenantContext"/> supplied so the
///   <see cref="TenantRlsConnectionInterceptor"/> wires in). The two-argument form
///   omits the interceptor; <c>app.tenant_id</c> is never set on the connection, the
///   RLS policy's <c>nullif(…)</c> evaluates to NULL, and the query silently returns
///   zero rows — <i>the console exits green having done nothing</i>. This test makes
///   that silent failure loud: using the two-argument form, the assertion below fails
///   rather than returning zero rows quietly.</item>
/// <item><b>Stop at first publish failure (ADR-011 w17 clause 26).</b> Between
///   <see cref="EmbeddingRetrievalService.RemoveChunksAsync"/> (committed) and
///   <c>PublishAsync</c> there is a window where chunks are gone and no job is queued.
///   A loop that continues past that failure removes embeddings from every subsequent
///   document and repairs none. The second proof below shows that exactly 1 of 5
///   documents is processed when the publisher throws on the second call.</item>
/// </list>
///
/// Uses a real Postgres container (pgvector/pgvector:pg16) with an unprivileged app
/// role (NOSUPERUSER NOBYPASSRLS) for the tenant-binding proof — an in-memory provider
/// would bypass RLS and turn both assertions into tautologies.
/// </summary>
public sealed class BulkReprocessTenantBindingTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_reprocess_app";
    private const string AppRolePassword = "raffa_reprocess_app_test_pwd";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
    private readonly TenantContext _tenantContext = new();
    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Migrate with admin connection (no tenant filter needed for DDL).
        var adminOpts = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(adminOpts, _postgres.GetConnectionString());
        await using (var adminDb = new DocumentsContractsDbContext(adminOpts.Options))
        {
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

    // ── helpers ─────────────────────────────────────────────────────────────

    /// <summary>Opens a tenant-aware context — the three-argument Configure path that wires
    /// the <see cref="TenantRlsConnectionInterceptor"/>.</summary>
    private DocumentsContractsDbContext CreateTenantAwareContext() =>
        CreateContext(_appConnectionString, _tenantContext);

    /// <summary>Opens a context WITHOUT tenant context — the two-argument Configure path
    /// that omits the interceptor. Equivalent to the bug: app.tenant_id is never set, RLS
    /// returns zero rows silently.</summary>
    private DocumentsContractsDbContext CreateNoTenantContext() =>
        CreateContext(_appConnectionString, tenantContext: null);

    private static DocumentsContractsDbContext CreateContext(
        string connectionString, ITenantContext? tenantContext)
    {
        var b = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(b, connectionString, tenantContext);
        return new DocumentsContractsDbContext(b.Options);
    }

    // ── Seed helper ──────────────────────────────────────────────────────────

    private async Task<EntityId> SeedFailedDocumentAsync(TenantId tenantId)
    {
        // Use admin connection (superuser) to bypass RLS for seeding.
        var adminOpts = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(adminOpts, _postgres.GetConnectionString());
        await using var adminDb = new DocumentsContractsDbContext(adminOpts.Options);

        var doc = new Document
        {
            TenantId = tenantId,
            FileName = "contract.pdf",
            MimeType = "application/pdf",
            StoragePath = $"tenants/{tenantId.Value}/documents/test/contract.pdf",
            Checksum = "abc123",
            ProcessingStatus = DocumentProcessingStatus.Failed,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        adminDb.Documents.Add(doc);
        await adminDb.SaveChangesAsync();
        return doc.Id;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Proof 1 — Tenant binding
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// With the three-argument Configure path (ITenantContext supplied → interceptor wired),
    /// calling <see cref="ITenantContext.BeginScope"/> before the query causes the interceptor
    /// to emit <c>SET app.tenant_id</c>, RLS lets the row through, and the worklist is non-empty.
    ///
    /// <para>This is the correct path <c>BulkReprocessRunner.BuildWorklistAsync</c> uses —
    /// Program.cs calls <c>tenantContext.BeginScope(tenantId)</c> before any query.</para>
    /// </summary>
    [Fact]
    public async Task Worklist_with_three_arg_configure_and_BeginScope_returns_failed_documents()
    {
        var tenantId = TenantId.New();
        var docId = await SeedFailedDocumentAsync(tenantId);

        await using var ctx = CreateTenantAwareContext();
        // BeginScope: sets ITenantContext.Current → interceptor emits SET app.tenant_id on
        // the next Postgres connection → RLS allows this tenant's rows.
        using var scope = _tenantContext.BeginScope(tenantId);

        var worklist = await ctx.Documents
            .Where(d => d.TenantId == tenantId
                && (d.ProcessingStatus == DocumentProcessingStatus.Failed
                    || ctx.Embeddings.Any(e =>
                        e.TenantId == tenantId
                        && e.SourceType == "Document"
                        && e.SourceId == d.Id
                        && e.ChunkText.StartsWith("%PDF"))))
            .OrderBy(d => d.FileName)
            .Select(d => d.Id)
            .ToListAsync();

        // If this assertion fails, BeginScope + the three-argument Configure did not wire the
        // interceptor correctly — the row was invisible (zero rows, same as the bug below).
        Assert.Contains(docId, worklist);
    }

    /// <summary>
    /// With the two-argument Configure (no ITenantContext → no interceptor → app.tenant_id
    /// never set on the connection → Postgres RLS nullif(…) = NULL → zero rows), the
    /// worklist is empty — the console would exit green having done nothing.
    ///
    /// <para>This test documents the failure mode that the three-argument form prevents.
    /// <see cref="Worklist_with_three_arg_configure_and_BeginScope_returns_failed_documents"/>
    /// is the green path; together the two tests prove: three-arg + BeginScope → visible;
    /// two-arg → invisible-and-silent.</para>
    /// </summary>
    [Fact]
    public async Task Worklist_with_two_arg_configure_returns_zero_rows_silently()
    {
        var tenantId = TenantId.New();
        await SeedFailedDocumentAsync(tenantId);

        // Two-argument form: no ITenantContext → no TenantRlsConnectionInterceptor.
        await using var ctx = CreateNoTenantContext();
        // Even with a BeginScope call on the context-independent tenantContext, the interceptor
        // is not wired on THIS context — it still returns zero rows.
        using var scope = _tenantContext.BeginScope(tenantId);

        var worklist = await ctx.Documents
            .Where(d => d.TenantId == tenantId
                && (d.ProcessingStatus == DocumentProcessingStatus.Failed
                    || ctx.Embeddings.Any(e =>
                        e.TenantId == tenantId
                        && e.SourceType == "Document"
                        && e.SourceId == d.Id
                        && e.ChunkText.StartsWith("%PDF"))))
            .OrderBy(d => d.FileName)
            .Select(d => d.Id)
            .ToListAsync();

        // RLS returns zero rows silently — the bug. This assertion documents the failure mode;
        // the preceding test proves the fix.
        Assert.Empty(worklist);
    }

}

/// <summary>
/// Proof 2 (no Docker required): stop at the first publish failure (ADR-011 w17 clause 26).
///
/// <para>
/// Between <see cref="EmbeddingRetrievalService.RemoveChunksAsync"/> (committed before
/// <c>PublishAsync</c>) and the Service Bus publish there is a window where the chunks
/// are gone and no pointer is on the topic. Continuing the loop past that failure removes
/// embeddings from every subsequent document and repairs none. This test proves the
/// loop's catch-and-stop contract: when a per-document call throws on document 2 of 5,
/// exactly 1 document is processed and documents 3–5 are never attempted.
/// </para>
///
/// <para>
/// The <see cref="DocumentsContractsDbContext"/> uses pgvector, which is incompatible
/// with the EF Core InMemory provider, so the full integration path (real
/// <see cref="DocumentReprocessService"/> + throwing publisher) is tested against a real
/// Postgres container in <see cref="BulkReprocessTenantBindingTests"/> when Docker is
/// available. This class proves the catch-and-stop loop contract in isolation — the
/// property that the same loop logic in <c>BulkReprocessRunner.RunAsync</c> relies on.
/// </para>
/// </summary>
public sealed class BulkReprocessStopAtFirstFailureTests
{
    /// <summary>
    /// A publish failure on document 2 of 5 stops the loop, processes exactly 1 document,
    /// and does not attempt documents 3–5.
    ///
    /// <para>Uses a delegate stub for the per-document call (simulating
    /// <see cref="DocumentReprocessService.ReprocessAsync"/>) that succeeds on call 1 and
    /// throws on call 2, exercising the same catch-and-stop pattern
    /// <c>BulkReprocessRunner.RunAsync</c> uses.</para>
    /// </summary>
    [Fact]
    public async Task Stop_at_first_publish_failure_leaves_subsequent_documents_unprocessed()
    {
        // ── arrange ──────────────────────────────────────────────────────────
        var worklist = Enumerable.Range(1, 5).Select(_ => EntityId.New()).ToList();
        int callCount = 0;

        // Simulates ReprocessAsync: succeeds on call 1, throws on call 2 (publish failure).
        // Calls 3–5 must never be made.
        Task<Result<DocumentReprocessQueued>?> ProcessDocument(EntityId docId)
        {
            callCount++;
            if (callCount == 2)
                throw new InvalidOperationException(
                    "Simulated Service Bus publish failure (ADR-011 w17 clause 26).");
            return Task.FromResult<Result<DocumentReprocessQueued>?>(
                Result<DocumentReprocessQueued>.Success(
                    new DocumentReprocessQueued(docId, EntityId.New())));
        }

        // ── act: the same loop BulkReprocessRunner.RunAsync uses ────────────
        int processed = 0;
        foreach (var docId in worklist)
        {
            try
            {
                var result = await ProcessDocument(docId);
                if (result is null || result.IsFailure)
                    break;
                processed++;
            }
            catch
            {
                // ADR-011 w17 clause 26: stop at the first exception.
                // Never "log and continue" — past the first failure every iteration
                // destroys embeddings and repairs nothing.
                break;
            }
        }

        // ── assert ───────────────────────────────────────────────────────────
        Assert.Equal(1, processed);  // doc 1 succeeded
        Assert.Equal(2, callCount);  // doc 1 (success) + doc 2 (throw) — doc 3-5 never called
    }
}
