using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Quotes.Application.Extraction;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Jobs;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Api.Tests;

/// <summary>
/// Task F6-T01: with a durable job queue configured, <c>POST /api/quotes</c> and
/// <c>POST /api/quotes/{id}/reprocess</c> only queue the work, and the Worker's
/// <see cref="IJobHandler"/> runs it. The publisher is a durable fake, so the host behaves as it does
/// with a Service Bus namespace; the handler is resolved from the same container and driven by hand
/// with the message the API published, as the Worker's consumer would. In-memory host, scripted
/// <c>extract</c> role: no Postgres, Docker, broker or Foundry. Without a durable queue the unchanged
/// inline behaviour is covered by <c>QuoteUploadRobustnessTests</c> and <c>QuoteReprocessTests</c>.
/// </summary>
public sealed class QuoteExtractionQueueTests : IClassFixture<RaffaApiFactory>
{
    private const string QuoteText =
        "QUOTE number Q-2026-0417 from Northwind Traders for Fabrikam Inc. " +
        "Line 1: Cloud Suite Enterprise, 250 seats, unit price EUR 96.00 per seat per year.";

    private const string Payload = """
        {"items":[
            {"sku":"CS-ENT","description":"Cloud Suite Enterprise","quantity":250,"unitPrice":96,"term":"Annual","confidence":0.95}
        ]}
        """;

    private readonly WebApplicationFactory<Program> _baseFactory;

    public QuoteExtractionQueueTests(RaffaApiFactory factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task Upload_only_queues_the_job_and_runs_no_extraction_in_the_request()
    {
        var host = Host.Create(_baseFactory);
        var tenantId = Guid.NewGuid();

        var body = await UploadAsync(host, tenantId, BuildPdf(QuoteText));

        Assert.Equal("Uploaded", body.GetProperty("processingStatus").GetString());
        Assert.True(body.GetProperty("extractionQueued").GetBoolean());
        Assert.Equal(0, body.GetProperty("lineItemCount").GetInt32());
        // Nothing extracted: no model call, no OCR call, no lines; one queued job, one pointer.
        Assert.Empty(host.Gateway.Calls);
        var state = await host.ReadStateAsync();
        Assert.Empty(state.Lines);
        var job = Assert.Single(state.Jobs);
        Assert.Equal(QuoteExtractionJobStatus.Queued, job.Status);
        var message = Assert.Single(host.Publisher.Published);
        Assert.Equal(JobKinds.QuoteExtraction, message.Kind);
        Assert.Equal(tenantId, message.TenantId);
        Assert.Equal(body.GetProperty("id").GetGuid(), message.ResourceId);
        Assert.Equal(job.Id.Value, message.JobId);
    }

    [Fact]
    public async Task The_handler_runs_the_published_job_to_completion_without_an_http_request()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(Payload));
        var tenantId = Guid.NewGuid();
        await UploadAsync(host, tenantId, BuildPdf(QuoteText));

        var outcome = await host.DeliverAsync(host.Publisher.Published.Single(), delivery: 1);

        Assert.Equal(JobHandleOutcome.Handled, outcome);
        var state = await host.ReadStateAsync();
        Assert.Equal(QuoteProcessingStatus.Completed, state.Quotes.Single().ProcessingStatus);
        Assert.Equal(QuoteExtractionJobStatus.Completed, state.Jobs.Single().Status);
        Assert.Equal(24000m, Assert.Single(state.Lines).ExtendedPrice);
        Assert.Equal(1, host.Gateway.Calls.Count(c => c == "ExtractAsync"));
        Assert.Contains(host.Audit.Entries, e => e.Action == "quote.extraction.completed");
    }

    [Fact]
    public async Task A_duplicate_delivery_of_a_finished_job_does_nothing()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(Payload));
        await UploadAsync(host, Guid.NewGuid(), BuildPdf(QuoteText));
        var message = host.Publisher.Published.Single();
        await host.DeliverAsync(message, delivery: 1);

        var again = await host.DeliverAsync(message, delivery: 2);

        Assert.Equal(JobHandleOutcome.ClaimLost, again);
        Assert.Equal(1, host.Gateway.Calls.Count(c => c == "ExtractAsync"));
        Assert.Single((await host.ReadStateAsync()).Lines);
    }

    [Fact]
    public async Task A_pointer_to_a_job_that_does_not_exist_is_reported_not_found()
    {
        var host = Host.Create(_baseFactory);
        var message = new JobRequested(
            JobKinds.QuoteExtraction, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), JobRequested.CurrentSchemaVersion);

        Assert.Equal(JobHandleOutcome.JobNotFound, await host.DeliverAsync(message, delivery: 1));
    }

    [Fact]
    public async Task A_pointer_with_an_empty_id_is_dropped_without_touching_the_database()
    {
        var host = Host.Create(_baseFactory);
        var message = new JobRequested(
            JobKinds.QuoteExtraction, Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), JobRequested.CurrentSchemaVersion);

        Assert.Equal(JobHandleOutcome.Handled, await host.DeliverAsync(message, delivery: 1));
        Assert.Empty((await host.ReadStateAsync()).Jobs);
    }

    [Fact]
    public async Task A_pointer_naming_another_tenant_finds_nothing()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(Payload));
        await UploadAsync(host, Guid.NewGuid(), BuildPdf(QuoteText));
        var forged = host.Publisher.Published.Single() with { TenantId = Guid.NewGuid() };

        Assert.Equal(JobHandleOutcome.JobNotFound, await host.DeliverAsync(forged, delivery: 1));
        Assert.Empty(host.Gateway.Calls);
    }

    [Fact]
    public async Task A_failing_extraction_is_data_on_the_row_and_the_message_is_still_handled()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => throw new InvalidOperationException("model outage");
        await UploadAsync(host, Guid.NewGuid(), BuildPdf(QuoteText));

        var outcome = await host.DeliverAsync(host.Publisher.Published.Single(), delivery: 1);

        Assert.Equal(JobHandleOutcome.Handled, outcome);
        var state = await host.ReadStateAsync();
        Assert.Equal(QuoteProcessingStatus.Failed, state.Quotes.Single().ProcessingStatus);
        Assert.Equal(QuoteExtractionJobStatus.Failed, state.Jobs.Single().Status);
        Assert.DoesNotContain(
            state.Jobs, j => j.Status is QuoteExtractionJobStatus.Queued or QuoteExtractionJobStatus.Running);
    }

    [Fact]
    public async Task A_stored_file_that_has_gone_ends_the_job_failed_with_a_typed_error()
    {
        var host = Host.Create(_baseFactory);
        var tenantId = Guid.NewGuid();
        await UploadAsync(host, tenantId, BuildPdf(QuoteText));
        var quote = (await host.ReadStateAsync()).Quotes.Single();
        await host.Storage.DeleteAsync(new TenantId(tenantId), quote.StoragePath);

        var outcome = await host.DeliverAsync(host.Publisher.Published.Single(), delivery: 1);

        Assert.Equal(JobHandleOutcome.Handled, outcome);
        var state = await host.ReadStateAsync();
        Assert.Equal(QuoteProcessingStatus.Failed, state.Quotes.Single().ProcessingStatus);
        var job = state.Jobs.Single();
        Assert.Equal(QuoteExtractionJobStatus.Failed, job.Status);
        Assert.True(QuoteExtractionFailure.TryParseKind(job.ErrorDetail, out var kind));
        Assert.Equal(QuoteExtractionFailureKind.SourceMissing, kind);
        Assert.Empty(host.Gateway.Calls);
    }

    [Fact]
    public async Task A_host_shutdown_mid_run_gives_the_job_back_instead_of_failing_the_quote()
    {
        var host = Host.Create(_baseFactory);
        using var shutdown = new CancellationTokenSource();
        host.Gateway.ExtractOverride = (_, _) =>
        {
            shutdown.Cancel();
            throw new OperationCanceledException(shutdown.Token);
        };
        await UploadAsync(host, Guid.NewGuid(), BuildPdf(QuoteText));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => host.DeliverAsync(host.Publisher.Published.Single(), delivery: 1, shutdown.Token));

        var state = await host.ReadStateAsync();
        Assert.Equal(QuoteExtractionJobStatus.Queued, state.Jobs.Single().Status);
        Assert.Equal(QuoteProcessingStatus.Uploaded, state.Quotes.Single().ProcessingStatus);

        // The redelivery then succeeds.
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(Payload));
        Assert.Equal(JobHandleOutcome.Handled, await host.DeliverAsync(host.Publisher.Published.Single(), delivery: 2));
        Assert.Equal(QuoteProcessingStatus.Completed, (await host.ReadStateAsync()).Quotes.Single().ProcessingStatus);
    }

    [Fact]
    public async Task The_same_bytes_uploaded_again_while_the_first_is_queued_are_not_queued_twice()
    {
        var host = Host.Create(_baseFactory);
        var tenantId = Guid.NewGuid();
        var pdf = BuildPdf(QuoteText);

        var first = await UploadAsync(host, tenantId, pdf);
        var second = await UploadAsync(host, tenantId, pdf);

        Assert.True(second.GetProperty("deduplicated").GetBoolean());
        Assert.Equal(first.GetProperty("id").GetGuid(), second.GetProperty("id").GetGuid());
        Assert.False(second.GetProperty("extractionQueued").GetBoolean());
        Assert.Single(host.Publisher.Published);
        Assert.Single((await host.ReadStateAsync()).Jobs);
    }

    [Fact]
    public async Task Reprocess_queues_a_new_job_answers_202_and_the_handler_then_replaces_the_lines()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(Payload));
        var tenantId = Guid.NewGuid();
        var uploaded = await UploadAsync(host, tenantId, BuildPdf(QuoteText));
        await host.DeliverAsync(host.Publisher.Published.Single(), delivery: 1);
        var quoteId = uploaded.GetProperty("id").GetGuid();

        var client = host.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/quotes/{quoteId}/reprocess");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        // Still one model call: the reprocess itself ran nothing.
        Assert.Equal(1, host.Gateway.Calls.Count(c => c == "ExtractAsync"));
        Assert.Equal(2, host.Publisher.Published.Count);
        Assert.Equal(QuoteProcessingStatus.Uploaded, (await host.ReadStateAsync()).Quotes.Single().ProcessingStatus);

        await host.DeliverAsync(host.Publisher.Published.Last(), delivery: 1);

        var state = await host.ReadStateAsync();
        Assert.Equal(QuoteProcessingStatus.Completed, state.Quotes.Single().ProcessingStatus);
        Assert.Single(state.Lines);
        Assert.Equal(2, state.Jobs.Count);
        Assert.Equal(2, host.Gateway.Calls.Count(c => c == "ExtractAsync"));
    }

    // ----- helpers -----

    private static Result<AiExtractionResult> Extracted(string payloadJson) =>
        Result<AiExtractionResult>.Success(new AiExtractionResult(
            payloadJson,
            new AiCallMetadata("test-model", "1", "test", DateTimeOffset.UtcNow, "hash")));

    private static async Task<JsonElement> UploadAsync(Host host, Guid tenantId, byte[] bytes)
    {
        var client = host.Factory.CreateClient();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var content = new MultipartFormDataContent { { file, "file", "quote.pdf" } };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes") { Content = content };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static byte[] BuildPdf(string text) =>
        Encoding.Latin1.GetBytes(
            "%PDF-1.4\n1 0 obj << /Type /Page >> endobj\n2 0 obj << /Length 0 >>\nstream\n" +
            $"BT ({text}) Tj ET\nendstream\nendobj\n%%EOF\n");

    private sealed record QuoteState(
        IReadOnlyList<Quote> Quotes, IReadOnlyList<QuoteExtractionJob> Jobs, IReadOnlyList<QuoteLine> Lines);

    /// <summary>A publisher that says a broker is configured, and records what is published.</summary>
    private sealed class DurablePublisher : IJobQueuePublisher
    {
        public bool IsDurable => true;

        public List<JobRequested> Published { get; } = [];

        public Task PublishAsync(JobRequested message, CancellationToken cancellationToken = default)
        {
            Published.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class Host
    {
        private static readonly DateTimeOffset Now = new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);

        public required WebApplicationFactory<Program> Factory { get; init; }
        public required RecordingAiGateway Gateway { get; init; }
        public required RecordingAuditWriter Audit { get; init; }
        public required RecordingDocumentStorage Storage { get; init; }
        public required DurablePublisher Publisher { get; init; }

        public static Host Create(WebApplicationFactory<Program> baseFactory)
        {
            var gateway = new RecordingAiGateway(
                new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now), new AiGatewayOcrOptions()));
            var storage = new RecordingDocumentStorage();
            var audit = new RecordingAuditWriter();
            var publisher = new DurablePublisher();
            var quotesDbName = $"quotes-{Guid.NewGuid()}";

            var factory = baseFactory
                .WithPresentedCallersAsMembers()
                .WithInMemoryAskEngine(gateway, new FixedClock(Now), storage, audit)
                .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<DbContextOptions<QuotesDbContext>>();
                    services.RemoveAll<QuotesDbContext>();
                    services.AddDbContext<QuotesDbContext>(o => o
                        .UseInMemoryDatabase(quotesDbName)
                        .UseInternalServiceProvider(InMemoryAskEngineFactory.InMemoryProviderServices));
                    services.RemoveAll<IJobQueuePublisher>();
                    services.AddSingleton<IJobQueuePublisher>(publisher);
                    services.RemoveAll<IQuoteExtractionJobClaimStore>();
                    services.AddScoped<IQuoteExtractionJobClaimStore, InMemoryQuoteExtractionJobClaimStore>();
                }));

            return new Host { Factory = factory, Gateway = gateway, Audit = audit, Storage = storage, Publisher = publisher };
        }

        /// <summary>What the Worker's consumer does with one delivery: a fresh scope, the handler that
        /// owns the kind, the delivery count.</summary>
        public async Task<JobHandleOutcome> DeliverAsync(
            JobRequested message, long delivery, CancellationToken cancellationToken = default)
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            var handler = scope.ServiceProvider.GetServices<IJobHandler>()
                .Single(h => h.Kind == message.Kind);
            return await handler.HandleAsync(message, delivery, cancellationToken);
        }

        public async Task<QuoteState> ReadStateAsync()
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
            return new QuoteState(
                await db.Quotes.AsNoTracking().ToListAsync(),
                await db.QuoteExtractionJobs.AsNoTracking().ToListAsync(),
                await db.QuoteLines.AsNoTracking().ToListAsync());
        }
    }
}
