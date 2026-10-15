using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Raffa.Quotes.Application;
using Raffa.Quotes.Application.Extraction;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.Quotes.Tests.TestSupport;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Jobs;
using Raffa.SharedKernel.Tenancy;
using Testcontainers.PostgreSql;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Task F6-T01 (quote extraction on the durable queue), the Quotes side, against a real Postgres with
/// RLS (unprivileged role, as the other tests of this module): what the upload and the reprocess
/// publish and when, the duplicate check for an upload that is still in flight, and the claim that
/// turns at-least-once delivery into one run per job.
/// </summary>
public sealed class QuoteExtractionQueueTests : IAsyncLifetime
{
    private const string AppRoleName = "raffa_quote_queue_app";
    private const string AppRolePassword = "raffa_quote_queue_app_test_password";

    private static readonly DateTimeOffset T0 = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var adminOptions = new DbContextOptionsBuilder<QuotesDbContext>();
        QuotesDbContextOptions.Configure(adminOptions, _postgres.GetConnectionString());
        await using var adminDb = new QuotesDbContext(adminOptions.Options);
        await adminDb.Database.MigrateAsync();
        await adminDb.Database.ExecuteSqlRawAsync(
            $"""
            CREATE ROLE {AppRoleName} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS;
            GRANT USAGE ON SCHEMA public TO {AppRoleName};
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRoleName};
            """);

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

    private sealed class RecordingPublisher(bool durable, Exception? throws = null) : IJobQueuePublisher
    {
        public bool IsDurable { get; } = durable;

        public List<JobRequested> Published { get; } = [];

        public Task PublishAsync(JobRequested message, CancellationToken cancellationToken = default)
        {
            if (throws is not null)
            {
                return Task.FromException(throws);
            }

            Published.Add(message);
            return Task.CompletedTask;
        }
    }

    private async Task<QuoteUploadResult> UploadAsync(
        TenantId tenantId, byte[] bytes, IJobQueuePublisher? publisher, DateTimeOffset? at = null)
    {
        var tenantContext = new TenantContext();
        await using var db = CreateAppContext(tenantContext);
        var service = new QuoteUploadService(
            db, new RecordingDocumentStorage(), tenantContext, new FixedClock(at ?? T0), new RecordingAuditWriter(), publisher);
        using var content = new MemoryStream(bytes);
        var result = await service.UploadAsync(tenantId, "quote.pdf", "application/pdf", content, "actor@example.com");
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private async Task<(Quote Quote, IReadOnlyList<QuoteExtractionJob> Jobs)> ReadAsync(TenantId tenantId, EntityId quoteId)
    {
        var tenantContext = new TenantContext();
        using var scope = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);
        return (
            await db.Quotes.AsNoTracking().SingleAsync(q => q.Id == quoteId),
            await db.QuoteExtractionJobs.AsNoTracking().Where(j => j.QuoteId == quoteId).ToListAsync());
    }

    private async Task UpdateJobAsync(
        TenantId tenantId, EntityId jobId, QuoteExtractionJobStatus status, DateTimeOffset? startedAt)
    {
        var tenantContext = new TenantContext();
        using var scope = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);
        var job = await db.QuoteExtractionJobs.SingleAsync(j => j.Id == jobId);
        job.Status = status;
        job.StartedAt = startedAt;
        await db.SaveChangesAsync();
    }

    private async Task<QuoteJobClaimResult> ClaimAsync(
        TenantId tenantId, EntityId quoteId, EntityId jobId, long delivery, DateTimeOffset? at = null)
    {
        var tenantContext = new TenantContext();
        using var scope = tenantContext.BeginScope(tenantId);
        await using var db = CreateAppContext(tenantContext);
        return await new QuoteExtractionJobClaimStore(db, new FixedClock(at ?? T0))
            .TryClaimAsync(tenantId, quoteId, jobId, delivery, CancellationToken.None);
    }

    // ----- upload publishes -----

    [Fact]
    public async Task A_durable_queue_gets_the_job_pointer_and_the_upload_reports_it_queued()
    {
        var tenantId = TenantId.New();
        var publisher = new RecordingPublisher(durable: true);

        var uploaded = await UploadAsync(tenantId, Encoding.UTF8.GetBytes("%PDF-1.4 queued"), publisher);

        Assert.True(uploaded.ExtractionQueued);
        var message = Assert.Single(publisher.Published);
        var (quote, jobs) = await ReadAsync(tenantId, uploaded.QuoteId);
        var job = Assert.Single(jobs);
        Assert.Equal(JobKinds.QuoteExtraction, message.Kind);
        Assert.Equal(tenantId.Value, message.TenantId);
        Assert.Equal(quote.Id.Value, message.ResourceId);
        Assert.Equal(job.Id.Value, message.JobId);
        Assert.Equal(JobRequested.CurrentSchemaVersion, message.SchemaVersion);
        Assert.Equal(QuoteExtractionJobStatus.Queued, job.Status);
    }

    [Fact]
    public async Task Without_a_durable_queue_nothing_is_published_and_the_caller_runs_the_extraction()
    {
        var tenantId = TenantId.New();
        var publisher = new RecordingPublisher(durable: false);

        var uploaded = await UploadAsync(tenantId, Encoding.UTF8.GetBytes("%PDF-1.4 inline"), publisher);
        var withoutPublisher = await UploadAsync(tenantId, Encoding.UTF8.GetBytes("%PDF-1.4 no publisher"), null);

        Assert.False(uploaded.ExtractionQueued);
        Assert.False(withoutPublisher.ExtractionQueued);
        Assert.Empty(publisher.Published);
    }

    [Fact]
    public async Task A_publish_failure_fails_the_upload_and_leaves_no_quote_or_job()
    {
        var tenantId = TenantId.New();
        var tenantContext = new TenantContext();
        await using (var db = CreateAppContext(tenantContext))
        {
            var service = new QuoteUploadService(
                db, new RecordingDocumentStorage(), tenantContext, new FixedClock(T0), new RecordingAuditWriter(),
                new RecordingPublisher(durable: true, throws: new InvalidOperationException("broker down")));
            using var content = new MemoryStream("%PDF-1.4 broker down"u8.ToArray());

            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.UploadAsync(tenantId, "quote.pdf", "application/pdf", content, "actor@example.com"));
        }

        using (tenantContext.BeginScope(tenantId))
        {
            await using var readDb = CreateAppContext(tenantContext);
            Assert.Empty(await readDb.Quotes.ToListAsync());
            Assert.Empty(await readDb.QuoteExtractionJobs.ToListAsync());
        }
    }

    // ----- duplicate while in flight -----

    [Fact]
    public async Task With_a_durable_queue_the_same_bytes_uploaded_while_the_first_is_in_flight_is_a_duplicate()
    {
        var tenantId = TenantId.New();
        var publisher = new RecordingPublisher(durable: true);
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.4 double click");

        var first = await UploadAsync(tenantId, bytes, publisher, T0);
        var second = await UploadAsync(tenantId, bytes, publisher, T0.AddMinutes(1));

        Assert.True(second.IsDuplicate);
        Assert.Equal(first.QuoteId, second.QuoteId);
        Assert.False(second.ExtractionQueued);
        Assert.Single(publisher.Published);
    }

    [Fact]
    public async Task An_in_flight_quote_older_than_the_orphan_window_never_blocks_a_retry()
    {
        var tenantId = TenantId.New();
        var publisher = new RecordingPublisher(durable: true);
        var bytes = Encoding.UTF8.GetBytes("%PDF-1.4 orphan");

        var first = await UploadAsync(tenantId, bytes, publisher, T0);
        var second = await UploadAsync(tenantId, bytes, publisher, T0 + QuoteReprocessService.StaleJobAge + TimeSpan.FromMinutes(1));

        Assert.False(second.IsDuplicate);
        Assert.NotEqual(first.QuoteId, second.QuoteId);
        Assert.Equal(2, publisher.Published.Count);
    }

    // ----- claim -----

    [Fact]
    public async Task A_queued_job_is_claimed_once_and_the_quote_becomes_processing()
    {
        var tenantId = TenantId.New();
        var uploaded = await UploadAsync(tenantId, Encoding.UTF8.GetBytes("%PDF-1.4 claim"), new RecordingPublisher(true));
        var (_, jobs) = await ReadAsync(tenantId, uploaded.QuoteId);
        var jobId = jobs.Single().Id;

        var first = await ClaimAsync(tenantId, uploaded.QuoteId, jobId, delivery: 1);
        var second = await ClaimAsync(tenantId, uploaded.QuoteId, jobId, delivery: 1);

        Assert.Equal(QuoteJobClaimResult.Claimed, first);
        Assert.Equal(QuoteJobClaimResult.Lost, second);
        var (quote, after) = await ReadAsync(tenantId, uploaded.QuoteId);
        Assert.Equal(QuoteExtractionJobStatus.Running, after.Single().Status);
        Assert.Equal(T0, after.Single().StartedAt);
        Assert.Equal(QuoteProcessingStatus.Processing, quote.ProcessingStatus);
    }

    [Fact]
    public async Task Concurrent_claims_of_one_job_have_exactly_one_winner()
    {
        var tenantId = TenantId.New();
        var uploaded = await UploadAsync(tenantId, Encoding.UTF8.GetBytes("%PDF-1.4 race"), new RecordingPublisher(true));
        var (_, jobs) = await ReadAsync(tenantId, uploaded.QuoteId);
        var jobId = jobs.Single().Id;

        var results = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => Task.Run(() => ClaimAsync(tenantId, uploaded.QuoteId, jobId, delivery: 1))));

        Assert.Equal(1, results.Count(r => r == QuoteJobClaimResult.Claimed));
        Assert.Equal(5, results.Count(r => r == QuoteJobClaimResult.Lost));
    }

    [Fact]
    public async Task An_unknown_job_a_wrong_quote_and_another_tenants_job_are_not_found()
    {
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var uploaded = await UploadAsync(tenantA, Encoding.UTF8.GetBytes("%PDF-1.4 scoped"), new RecordingPublisher(true));
        var (_, jobs) = await ReadAsync(tenantA, uploaded.QuoteId);
        var jobId = jobs.Single().Id;

        Assert.Equal(QuoteJobClaimResult.NotFound, await ClaimAsync(tenantA, uploaded.QuoteId, EntityId.New(), 1));
        Assert.Equal(QuoteJobClaimResult.NotFound, await ClaimAsync(tenantA, EntityId.New(), jobId, 1));
        Assert.Equal(QuoteJobClaimResult.NotFound, await ClaimAsync(tenantB, uploaded.QuoteId, jobId, 1));
        // None of those touched the job.
        Assert.Equal(QuoteExtractionJobStatus.Queued, (await ReadAsync(tenantA, uploaded.QuoteId)).Jobs.Single().Status);
    }

    [Fact]
    public async Task A_redelivery_takes_over_a_run_that_started_long_enough_ago_and_a_first_delivery_never_does()
    {
        var tenantId = TenantId.New();
        var uploaded = await UploadAsync(tenantId, Encoding.UTF8.GetBytes("%PDF-1.4 hung"), new RecordingPublisher(true));
        var (_, jobs) = await ReadAsync(tenantId, uploaded.QuoteId);
        var jobId = jobs.Single().Id;
        var now = T0.AddMinutes(30);
        await UpdateJobAsync(tenantId, jobId, QuoteExtractionJobStatus.Running, startedAt: T0.AddMinutes(20));

        // Same delivery count as the original: someone is alive and holds the message.
        Assert.Equal(QuoteJobClaimResult.Lost, await ClaimAsync(tenantId, uploaded.QuoteId, jobId, 1, now));
        // A redelivery means the previous consumer is gone: the hung run is taken over.
        Assert.Equal(QuoteJobClaimResult.Claimed, await ClaimAsync(tenantId, uploaded.QuoteId, jobId, 2, now));
        Assert.Equal(now, (await ReadAsync(tenantId, uploaded.QuoteId)).Jobs.Single().StartedAt);
    }

    [Fact]
    public async Task A_redelivery_does_not_race_a_run_that_has_only_just_started()
    {
        var tenantId = TenantId.New();
        var uploaded = await UploadAsync(tenantId, Encoding.UTF8.GetBytes("%PDF-1.4 fresh"), new RecordingPublisher(true));
        var (_, jobs) = await ReadAsync(tenantId, uploaded.QuoteId);
        var jobId = jobs.Single().Id;
        await UpdateJobAsync(tenantId, jobId, QuoteExtractionJobStatus.Running, startedAt: T0.AddSeconds(-30));

        Assert.Equal(QuoteJobClaimResult.Lost, await ClaimAsync(tenantId, uploaded.QuoteId, jobId, 3));
    }

    [Theory]
    [InlineData(QuoteExtractionJobStatus.Completed)]
    [InlineData(QuoteExtractionJobStatus.NeedsReview)]
    [InlineData(QuoteExtractionJobStatus.Failed)]
    public async Task A_finished_job_is_never_claimed_again_not_even_by_a_redelivery(QuoteExtractionJobStatus status)
    {
        var tenantId = TenantId.New();
        var uploaded = await UploadAsync(tenantId, Encoding.UTF8.GetBytes($"%PDF-1.4 done {status}"), new RecordingPublisher(true));
        var (_, jobs) = await ReadAsync(tenantId, uploaded.QuoteId);
        var jobId = jobs.Single().Id;
        await UpdateJobAsync(tenantId, jobId, status, startedAt: T0.AddHours(-3));

        Assert.Equal(QuoteJobClaimResult.Lost, await ClaimAsync(tenantId, uploaded.QuoteId, jobId, 5));
    }

    [Fact]
    public async Task Release_puts_a_running_or_failed_job_back_to_queued_and_its_quote_to_uploaded()
    {
        var tenantId = TenantId.New();
        var uploaded = await UploadAsync(tenantId, Encoding.UTF8.GetBytes("%PDF-1.4 release"), new RecordingPublisher(true));
        var (_, jobs) = await ReadAsync(tenantId, uploaded.QuoteId);
        var jobId = jobs.Single().Id;
        Assert.Equal(QuoteJobClaimResult.Claimed, await ClaimAsync(tenantId, uploaded.QuoteId, jobId, 1));

        var tenantContext = new TenantContext();
        using (tenantContext.BeginScope(tenantId))
        {
            await using var db = CreateAppContext(tenantContext);
            await new QuoteExtractionJobClaimStore(db, new FixedClock(T0))
                .ReleaseAsync(tenantId, uploaded.QuoteId, jobId, CancellationToken.None);
        }

        var (quote, after) = await ReadAsync(tenantId, uploaded.QuoteId);
        Assert.Equal(QuoteExtractionJobStatus.Queued, after.Single().Status);
        Assert.Null(after.Single().StartedAt);
        Assert.Equal(QuoteProcessingStatus.Uploaded, quote.ProcessingStatus);
        Assert.Equal(QuoteJobClaimResult.Claimed, await ClaimAsync(tenantId, uploaded.QuoteId, jobId, 2));
    }

    [Fact]
    public async Task Release_leaves_a_finished_quote_alone()
    {
        var tenantId = TenantId.New();
        var uploaded = await UploadAsync(tenantId, Encoding.UTF8.GetBytes("%PDF-1.4 finished"), new RecordingPublisher(true));
        var (_, jobs) = await ReadAsync(tenantId, uploaded.QuoteId);
        var jobId = jobs.Single().Id;
        await UpdateJobAsync(tenantId, jobId, QuoteExtractionJobStatus.Completed, startedAt: T0);

        var tenantContext = new TenantContext();
        using (tenantContext.BeginScope(tenantId))
        {
            await using var db = CreateAppContext(tenantContext);
            await new QuoteExtractionJobClaimStore(db, new FixedClock(T0))
                .ReleaseAsync(tenantId, uploaded.QuoteId, jobId, CancellationToken.None);
        }

        Assert.Equal(QuoteExtractionJobStatus.Completed, (await ReadAsync(tenantId, uploaded.QuoteId)).Jobs.Single().Status);
    }
}
