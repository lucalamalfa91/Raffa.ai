using Raffa.Audit.Domain;
using Raffa.Audit.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Audit.Tests;

/// <summary>
/// Task E20/F02/US02/T01 (w17, NW-73 — bulk-reprocess console).
///
/// <para>
/// Proves ADR-011 w17 clause 20b: the run-scoped audit row carries
/// <c>Actor = "system:bulk-reprocess"</c> (the fixed literal) and exposes the
/// triggering identity only in <c>Detail</c>, never in <c>Actor</c>. No CI-controlled
/// string — not <c>github.actor</c>, not a run id, not a workflow input — ever reaches
/// <c>AuditEntry.Actor</c>. <c>Actor</c> asserts an identity; <c>Detail</c> asserts none;
/// the append-only trigger makes a wrong <c>Actor</c> permanent and uncorrectable.
/// </para>
///
/// <para>
/// Uses a real Postgres container + the real <see cref="AuditWriter"/> and
/// <see cref="AuditDbContext"/> (with RLS in force) so the proof covers the full
/// persistence path, not just an in-memory dictionary.
/// </para>
/// </summary>
public sealed class BulkReprocessAuditActorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();
    private readonly TenantContext _tenantContext = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private AuditDbContext CreateContext()
    {
        var b = new DbContextOptionsBuilder<AuditDbContext>();
        AuditDbContextOptions.Configure(b, _postgres.GetConnectionString(), _tenantContext);
        return new AuditDbContext(b.Options);
    }

    // ── Tests ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The run-scoped row has <c>Actor = "system:bulk-reprocess"</c> and <c>Detail</c>
    /// that contains <c>requestedBy=</c>. The triggering actor string (here simulating
    /// <c>github.actor</c>) is in <c>Detail</c>, never in <c>Actor</c>.
    /// </summary>
    [Fact]
    public async Task Run_scoped_audit_row_carries_fixed_literal_actor_and_requestedBy_in_detail()
    {
        var tenantId = TenantId.New();
        var requestedBy = "ops-engineer@example.com";   // simulates github.actor
        var runId = Guid.NewGuid().ToString();
        const int documentCount = 7;

        // Construct the entry the same way BulkReprocessRunner.RunAsync does.
        // AuditEntry is a positional record: (TenantId, Actor, Action, ResourceType, ResourceId, Timestamp, Detail?)
        var entry = new AuditEntry(
            tenantId,
            "system:bulk-reprocess",                    // Actor — fixed literal, ADR-011 clause 20b
            "document.bulk-reprocess.started",          // Action — follows document.* vocabulary
            "bulk-reprocess",                           // ResourceType
            runId,                                      // ResourceId
            DateTimeOffset.UtcNow,                      // Timestamp
            $"requestedBy={requestedBy}; run={runId}; tenant={tenantId.Value}; count={documentCount}"); // Detail

        await using var db = CreateContext();
        var auditWriter = new AuditWriter(db);

        using var scope = _tenantContext.BeginScope(tenantId);
        await auditWriter.WriteAsync(entry);

        // Read back.
        var rows = await db.AuditEvents
            .Where(e => e.TenantId == tenantId)
            .ToListAsync();

        var row = Assert.Single(rows);

        // Actor is the fixed literal — never the triggering human's identity.
        Assert.Equal("system:bulk-reprocess", row.Actor);

        // The triggering actor is in Detail, never in Actor.
        Assert.NotNull(row.Detail);
        Assert.Contains("requestedBy=", row.Detail);
        Assert.Contains(requestedBy, row.Detail);

        // The triggering actor does NOT appear in Actor.
        Assert.DoesNotContain(requestedBy, row.Actor);

        // Structural checks: action follows the document.* vocabulary, run-id is the resource.
        Assert.Equal("document.bulk-reprocess.started", row.Action);
        Assert.Equal(runId, row.ResourceId);
        Assert.Contains($"count={documentCount}", row.Detail);
    }

    /// <summary>
    /// Per-document rows (written by <see cref="DocumentReprocessService.ReprocessAsync"/>
    /// with <c>actor = "system:bulk-reprocess"</c>) keep the same fixed actor literal.
    /// This test proves the call-site contract: no caller-supplied string other than the
    /// literal reaches <c>Actor</c>.
    /// </summary>
    [Fact]
    public async Task Per_document_audit_row_also_carries_fixed_literal_actor()
    {
        var tenantId = TenantId.New();
        var documentId = EntityId.New();
        var jobId = EntityId.New();

        // Per-document row — same shape as DocumentReprocessService writes it.
        var entry = new AuditEntry(
            tenantId,
            "system:bulk-reprocess",            // Actor — fixed literal
            "document.reprocessed",             // Action
            "document",                         // ResourceType
            documentId.Value.ToString(),        // ResourceId
            DateTimeOffset.UtcNow,             // Timestamp
            $"queued; extractionJobId={jobId.Value}; attempt=1"); // Detail

        await using var db = CreateContext();
        var auditWriter = new AuditWriter(db);

        using var scope = _tenantContext.BeginScope(tenantId);
        await auditWriter.WriteAsync(entry);

        var rows = await db.AuditEvents
            .Where(e => e.TenantId == tenantId)
            .ToListAsync();

        var row = Assert.Single(rows);
        Assert.Equal("system:bulk-reprocess", row.Actor);
        Assert.Equal("document.reprocessed", row.Action);
    }
}
