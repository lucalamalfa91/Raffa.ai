using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Tools;

/// <summary>
/// Task E20/F02/US02/T01 (w17, NW-73): the bulk-reprocess loop for one tenant.
///
/// <para>
/// <b>Worklist.</b> Runs the same predicate as
/// <c>.github/workflows/verify-tenant-corpus.yml:151-160</c> — documents with
/// <c>processing_status = 'Failed'</c> or at least one embedding whose
/// <c>chunk_text</c> starts with <c>%PDF</c> — never a second predicate, so
/// "what needs reprocessing" and "what got reprocessed" cannot drift apart.
/// </para>
///
/// <para>
/// <b>Tenant binding (ADR-009 w17 clause 1).</b> The worklist query runs in the
/// caller's already-open <see cref="ITenantContext"/> scope (<c>BeginScope</c> called
/// by <c>Program.cs</c> before <see cref="RunAsync"/>). The DbContext was configured
/// with the three-argument <see cref="DocumentsContractsDbContextOptions.Configure"/>,
/// which registers <see cref="TenantRlsConnectionInterceptor"/> — the interceptor sets
/// <c>app.tenant_id</c> on every Postgres connection from <c>ITenantContext.Current</c>.
/// With the two-argument form the interceptor is absent, <c>app.tenant_id</c> is never
/// set, the RLS policy's <c>nullif(…)</c> evaluates to NULL, and the query silently
/// returns zero rows. <see cref="BulkReprocessTenantBindingTests"/> proves both paths.
/// </para>
///
/// <para>
/// <b>Stop-at-first-publish-failure (ADR-011 w17 clause 26).</b> Between
/// <see cref="EmbeddingRetrievalService.RemoveChunksAsync"/> (which commits its own
/// <c>SaveChangesAsync</c>) and <c>PublishAsync</c> there is a window where the chunks
/// are gone but the pointer is not yet on the topic. A publish failure in that window
/// leaves the document with no embeddings and no queued job. Continuing the loop past
/// that failure would remove chunks from every subsequent document and repair none —
/// the entire tenant corpus ends up empty. The loop therefore stops at the first
/// exception and exits non-zero.
/// </para>
///
/// <para>
/// <b>Exit codes.</b> Zero only when every document in the worklist was queued.
/// Non-zero when the worklist is empty (the S17-3 symptom — a valid tenant with an
/// empty worklist should not look like a silent success), when a publish failure occurs,
/// or when a per-document failure is returned.
/// </para>
/// </summary>
public sealed class BulkReprocessRunner(
    DocumentsContractsDbContext dbContext,
    DocumentReprocessService reprocessService,
    IAuditWriter auditWriter,
    IClock clock)
{
    /// <summary>Fixed actor literal (ADR-011 w17 clause 20b). No CI-controlled string ever
    /// reaches this value — <c>requestedBy</c> goes in <see cref="AuditEntry.Detail"/>.</summary>
    public const string BulkReprocessActor = "system:bulk-reprocess";

    /// <summary>Action name for the one run-scoped row written before the loop, following the
    /// existing <c>document.*</c> vocabulary.</summary>
    public const string RunStartedAction = "document.bulk-reprocess.started";

    /// <summary>
    /// Runs the bulk-reprocess loop for <paramref name="tenantId"/>. Returns an exit code:
    /// 0 = all documents queued; non-zero = empty worklist, publish failure, or partial run.
    /// </summary>
    /// <param name="tenantId">The tenant whose worklist is processed.</param>
    /// <param name="requestedBy">The human or CI actor that triggered the run (goes into
    /// <see cref="AuditEntry.Detail"/>, never into <see cref="AuditEntry.Actor"/>).</param>
    /// <param name="runId">Stable identifier for this run (job ID or a fresh GUID).</param>
    /// <param name="cancellationToken">Propagated to every async call.</param>
    public async Task<int> RunAsync(
        TenantId tenantId,
        string requestedBy,
        string runId,
        CancellationToken cancellationToken = default)
    {
        // Caller (Program.cs) already called tenantContext.BeginScope(tenantId), so
        // ITenantContext.Current is set. The DbContext's TenantRlsConnectionInterceptor
        // reads Current on every new connection and sets app.tenant_id — the three-argument
        // Configure path. Querying here without that scope active returns zero rows silently
        // (the two-argument form). See BulkReprocessTenantBindingTests.
        var worklist = await BuildWorklistAsync(tenantId, cancellationToken).ConfigureAwait(false);

        if (worklist.Count == 0)
        {
            // ADR-009 w17 clause 1 §5 / task DoD: zero rows under a valid tenant is the S17-3
            // symptom (verify-tenant-corpus would already have said "empty"), not a success.
            Console.Error.WriteLine(
                $"Worklist is empty for tenant {tenantId.Value} — " +
                "no document has processing_status = 'Failed' or a %%PDF embedding. " +
                "Run verify-tenant-corpus to confirm the corpus is already clean.");
            return 1;
        }

        // One run-scoped audit row before the loop (ADR-011 w17 clause 20b).
        // Actor is the fixed literal — never requestedBy, never a CI input (ADR-011 clause 20b).
        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                BulkReprocessActor,
                RunStartedAction,
                "bulk-reprocess",
                runId,
                clock.UtcNow,
                $"requestedBy={requestedBy}; run={runId}; tenant={tenantId.Value}; count={worklist.Count}"),
            cancellationToken).ConfigureAwait(false);

        Console.WriteLine(
            $"Starting bulk reprocess: run={runId}, tenant={tenantId.Value}, documents={worklist.Count}.");

        int processed = 0;
        foreach (var documentId in worklist)
        {
            // ADR-011 w17 clause 26: stop at the first publish failure.
            // ReprocessAsync calls RemoveChunksAsync (commits its own SaveChanges) before
            // PublishAsync. A publish failure leaves that document's chunks deleted and
            // nothing queued — continuing would repeat that damage for every subsequent document.
            try
            {
                var result = await reprocessService
                    .ReprocessAsync(tenantId, documentId, BulkReprocessActor, cancellationToken)
                    .ConfigureAwait(false);

                if (result is null)
                {
                    Console.Error.WriteLine(
                        $"Document {documentId.Value} not found for tenant {tenantId.Value} (worklist inconsistency). " +
                        $"Processed {processed}/{worklist.Count}.");
                    return 1;
                }

                if (result.IsFailure)
                {
                    Console.Error.WriteLine(
                        $"Document {documentId.Value}: {result.Error}. " +
                        $"Processed {processed}/{worklist.Count}.");
                    return 1;
                }

                processed++;
                Console.WriteLine($"Queued document {documentId.Value} ({processed}/{worklist.Count}).");
            }
            catch (Exception ex)
            {
                // Stop at the first publish failure (ADR-011 w17 clause 26).
                // Never "log and continue" — past the first failure every iteration destroys
                // embeddings and repairs nothing.
                Console.Error.WriteLine(
                    $"Publish failure on document {documentId.Value}: {ex.GetType().Name}: {ex.Message}. " +
                    $"Processed {processed}/{worklist.Count}.");
                return 1;
            }
        }

        Console.WriteLine($"Bulk reprocess complete: processed {processed}/{worklist.Count}.");
        return 0;
    }

    /// <summary>
    /// The same predicate as <c>verify-tenant-corpus.yml:151-160</c>: documents whose
    /// <c>processing_status</c> is <c>Failed</c>, or that still have at least one embedding
    /// whose <c>chunk_text</c> starts with <c>%PDF</c> (raw-byte soup from a scanned page
    /// indexed before the OCR role was wired, R-DOC-07 AC-1).
    ///
    /// <para>Only the <see cref="EntityId"/> of each matched document is returned — the loop
    /// passes it verbatim to <see cref="DocumentReprocessService.ReprocessAsync"/>, which
    /// re-reads the document row itself. No file names, bytes or vault values are logged here
    /// (task DoD AC-9).</para>
    /// </summary>
    internal async Task<IReadOnlyList<EntityId>> BuildWorklistAsync(
        TenantId tenantId,
        CancellationToken cancellationToken = default)
    {
        return await dbContext.Documents
            .Where(d => d.TenantId == tenantId
                && (d.ProcessingStatus == DocumentProcessingStatus.Failed
                    || dbContext.Embeddings.Any(e =>
                        e.TenantId == tenantId
                        && e.SourceType == "Document"
                        && e.SourceId == d.Id
                        && e.ChunkText.StartsWith("%PDF"))))
            .OrderBy(d => d.FileName)
            .Select(d => d.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
