using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Proves AC-4 of task E16/F02/US01/T01 (async-processing-schema, ADR-027 §D3): the
/// conditional-<c>UPDATE</c> claim is atomic — two concurrent claims of the same
/// <see cref="ExtractionJobStatus.Queued"/> job leave exactly one winner, and the loser observes
/// that it lost (0 rows affected).
///
/// Deliberately a real Postgres proof (Testcontainers), not an in-memory one: the property under
/// test is row-level locking, which no in-memory EF provider models, and the EF InMemory provider
/// cannot even execute <see cref="ExtractionJobClaimStore"/>'s raw SQL at all.
/// </summary>
public sealed class ExtractionJobClaimStoreTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    private DocumentsContractsDbContext CreateContext()
    {
        var optionsBuilder = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(optionsBuilder, _postgres.GetConnectionString());
        return new DocumentsContractsDbContext(optionsBuilder.Options);
    }

    private async Task<EntityId> SeedQueuedJobAsync()
    {
        var tenantId = TenantId.New();
        var document = new Document
        {
            TenantId = tenantId,
            FileName = "contract.pdf",
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value}/contract.pdf",
            Checksum = "checksum",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        var job = new ExtractionJob
        {
            TenantId = tenantId,
            DocumentId = document.Id,
            Stage = ExtractionStage.Classification,
            QueuedAt = DateTimeOffset.UtcNow,
        };

        await using var db = CreateContext();
        db.Documents.Add(document);
        db.ExtractionJobs.Add(job);
        await db.SaveChangesAsync();
        return job.Id;
    }

    [Fact]
    public async Task Two_concurrent_claims_of_the_same_job_leave_exactly_one_winner()
    {
        var jobId = await SeedQueuedJobAsync();

        // Two independent DbContexts/connections stand in for two worker instances racing the
        // same message (redelivery, or a second replica). Task.WhenAll starts both statements
        // together; Postgres's own row lock on the UPDATE serializes them regardless of thread
        // interleaving, so this is deterministic, not a timing-dependent flake.
        await using var dbA = CreateContext();
        await using var dbB = CreateContext();
        var storeA = new ExtractionJobClaimStore(dbA, new SystemClock());
        var storeB = new ExtractionJobClaimStore(dbB, new SystemClock());

        var results = await Task.WhenAll(
            storeA.TryClaimAsync(jobId, "worker-a", CancellationToken.None),
            storeB.TryClaimAsync(jobId, "worker-b", CancellationToken.None));

        Assert.Equal(1, results.Sum());
        Assert.Contains(1, results);
        Assert.Contains(0, results);

        await using var verifyDb = CreateContext();
        var reloaded = await verifyDb.ExtractionJobs.SingleAsync(j => j.Id == jobId);

        // The loser must not have double-incremented or overwritten the winner's claim.
        Assert.Equal(1, reloaded.AttemptCount);
        Assert.NotNull(reloaded.ClaimedAt);
        Assert.True(reloaded.ClaimedBy is "worker-a" or "worker-b");
        // instant-identity-ingest: TryClaimAsync now sets status = Running immediately.
        Assert.Equal(ExtractionJobStatus.Running, reloaded.Status);
    }

    [Fact]
    public async Task A_third_claim_after_the_job_is_already_claimed_affects_zero_rows()
    {
        var jobId = await SeedQueuedJobAsync();

        await using var firstDb = CreateContext();
        var firstStore = new ExtractionJobClaimStore(firstDb, new SystemClock());
        var firstResult = await firstStore.TryClaimAsync(jobId, "worker-a", CancellationToken.None);
        Assert.Equal(1, firstResult);

        // A later delivery of the same message (or a lease-holder still alive) must see it lost —
        // this task's claim never re-opens on a plain retry, only us-02's lease/reclaim logic may.
        await using var secondDb = CreateContext();
        var secondStore = new ExtractionJobClaimStore(secondDb, new SystemClock());
        var secondResult = await secondStore.TryClaimAsync(jobId, "worker-b", CancellationToken.None);
        Assert.Equal(0, secondResult);

        await using var verifyDb = CreateContext();
        var reloaded = await verifyDb.ExtractionJobs.SingleAsync(j => j.Id == jobId);
        Assert.Equal(1, reloaded.AttemptCount);
        Assert.Equal("worker-a", reloaded.ClaimedBy);
    }

    [Fact]
    public async Task Claiming_an_unknown_job_id_affects_zero_rows()
    {
        await using var db = CreateContext();
        var store = new ExtractionJobClaimStore(db, new SystemClock());

        var result = await store.TryClaimAsync(EntityId.New(), "worker-a", CancellationToken.None);

        Assert.Equal(0, result);
    }
}
