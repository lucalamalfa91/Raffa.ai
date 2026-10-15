using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Api.Tests;

/// <summary>
/// Task F6-T03 (reprocess of quotes: replace, never add): <c>POST /api/quotes/{id}/reprocess</c>
/// re-runs the extraction over the stored bytes and swaps the quote's lines for the new ones. Runs
/// on the in-memory host with a scripted <c>extract</c> role, so no Postgres, Docker or Foundry.
/// </summary>
public sealed class QuoteReprocessTests : IClassFixture<RaffaApiFactory>
{
    private const string QuoteText =
        "QUOTE number Q-2026-0417 from Northwind Traders for Fabrikam Inc. " +
        "Line 1: Cloud Suite Enterprise, 250 seats, unit price EUR 96.00 per seat per year.";

    private const string TwoLines = """
        {"items":[
            {"sku":"CS-ENT","description":"Cloud Suite Enterprise","quantity":250,"unitPrice":96,"term":"Annual","confidence":0.95},
            {"sku":"SUP","description":"Premium support","quantity":1,"unitPrice":4800,"term":"Annual","confidence":0.95}
        ]}
        """;

    private const string OneLine = """
        {"items":[
            {"sku":"CS-ENT","description":"Cloud Suite Enterprise (re-read)","quantity":250,"unitPrice":96,"term":"Annual","confidence":0.95}
        ]}
        """;

    private readonly WebApplicationFactory<Program> _baseFactory;

    public QuoteReprocessTests(RaffaApiFactory factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task Reprocess_replaces_the_lines_instead_of_adding_to_them()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(TwoLines));
        var tenantId = Guid.NewGuid();
        var quoteId = await UploadAsync(host, tenantId);
        Assert.Equal(2, (await host.ReadStateAsync()).Lines.Count);

        // The re-read finds one line: after the reprocess the quote has that one line, not 3.
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(OneLine));
        var response = await ReprocessAsync(host, tenantId, quoteId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await host.ReadStateAsync();
        var line = Assert.Single(state.Lines);
        Assert.Equal("Cloud Suite Enterprise (re-read)", line.Description);
        Assert.Equal(quoteId, state.Quotes.Single().Id.Value);
        Assert.Equal(QuoteProcessingStatus.Completed, state.Quotes.Single().ProcessingStatus);
        Assert.Contains(host.Audit.Entries, e => e.Action == "quote.reprocess.queued");
    }

    [Fact]
    public async Task Reprocessing_twice_in_a_row_leaves_exactly_one_set_of_lines_and_no_open_job()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(TwoLines));
        var tenantId = Guid.NewGuid();
        var quoteId = await UploadAsync(host, tenantId);

        await ReprocessAsync(host, tenantId, quoteId);
        await ReprocessAsync(host, tenantId, quoteId);

        var state = await host.ReadStateAsync();
        Assert.Equal(2, state.Lines.Count);
        Assert.Equal(3, state.Jobs.Count);
        Assert.All(state.Jobs, j => Assert.Equal(QuoteExtractionJobStatus.Completed, j.Status));
        Assert.Single(state.Quotes);
        Assert.Equal(3, host.Gateway.Calls.Count(c => c == "ExtractAsync"));
    }

    [Fact]
    public async Task A_failed_reprocess_keeps_the_previous_lines_and_marks_the_quote_failed()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(TwoLines));
        var tenantId = Guid.NewGuid();
        var quoteId = await UploadAsync(host, tenantId);

        host.Gateway.ExtractOverride = (_, _) => throw new InvalidOperationException("model outage");
        var response = await ReprocessAsync(host, tenantId, quoteId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await host.ReadStateAsync();
        Assert.Equal(2, state.Lines.Count);
        Assert.Equal(QuoteProcessingStatus.Failed, state.Quotes.Single().ProcessingStatus);
        Assert.DoesNotContain(
            state.Jobs, j => j.Status is QuoteExtractionJobStatus.Queued or QuoteExtractionJobStatus.Running);
    }

    [Fact]
    public async Task A_malformed_answer_on_reprocess_does_not_cost_the_quote_its_lines()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(TwoLines));
        var tenantId = Guid.NewGuid();
        var quoteId = await UploadAsync(host, tenantId);

        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted("{\"items\": [ not json"));
        await ReprocessAsync(host, tenantId, quoteId);

        var state = await host.ReadStateAsync();
        Assert.Equal(2, state.Lines.Count);
        Assert.Equal(QuoteProcessingStatus.Failed, state.Quotes.Single().ProcessingStatus);
    }

    [Fact]
    public async Task Reprocess_of_an_unknown_quote_is_404_and_a_non_guid_is_400()
    {
        var host = Host.Create(_baseFactory);
        var tenantId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await ReprocessAsync(host, tenantId, Guid.NewGuid())).StatusCode);

        var client = host.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes/not-a-guid/reprocess");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Reprocess_of_another_tenants_quote_is_404()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(TwoLines));
        var quoteId = await UploadAsync(host, Guid.NewGuid());

        var response = await ReprocessAsync(host, Guid.NewGuid(), quoteId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(2, (await host.ReadStateAsync()).Lines.Count);
    }

    [Fact]
    public async Task A_quote_with_a_fresh_open_job_is_refused_and_one_with_a_stale_open_job_is_superseded()
    {
        var host = Host.Create(_baseFactory);
        host.Gateway.ExtractOverride = (_, _) => Task.FromResult(Extracted(TwoLines));
        var tenantId = Guid.NewGuid();
        var quoteId = await UploadAsync(host, tenantId);

        var open = await host.AddOpenJobAsync(tenantId, quoteId, queuedAt: Host.Now);
        var refused = await ReprocessAsync(host, tenantId, quoteId);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal(2, (await host.ReadStateAsync()).Jobs.Count); // upload job + the open one

        await host.AgeJobAsync(open, Host.Now.AddHours(-2));
        var accepted = await ReprocessAsync(host, tenantId, quoteId);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);

        var state = await host.ReadStateAsync();
        var superseded = Assert.Single(state.Jobs, j => j.Id == open);
        Assert.Equal(QuoteExtractionJobStatus.Failed, superseded.Status);
        Assert.DoesNotContain(
            state.Jobs, j => j.Status is QuoteExtractionJobStatus.Queued or QuoteExtractionJobStatus.Running);
    }

    // ----- helpers -----

    private static Result<AiExtractionResult> Extracted(string payloadJson) =>
        Result<AiExtractionResult>.Success(new AiExtractionResult(
            payloadJson,
            new AiCallMetadata("test-model", "1", "test", DateTimeOffset.UtcNow, "hash")));

    private static async Task<Guid> UploadAsync(Host host, Guid tenantId)
    {
        var client = host.Factory.CreateClient();
        var file = new ByteArrayContent(BuildPdf(QuoteText));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var content = new MultipartFormDataContent { { file, "file", "quote.pdf" } };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes") { Content = content };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> ReprocessAsync(Host host, Guid tenantId, Guid quoteId)
    {
        var client = host.Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/quotes/{quoteId}/reprocess");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        return await client.SendAsync(request);
    }

    private static byte[] BuildPdf(string text) =>
        Encoding.Latin1.GetBytes(
            "%PDF-1.4\n1 0 obj << /Type /Page >> endobj\n2 0 obj << /Length 0 >>\nstream\n" +
            $"BT ({text}) Tj ET\nendstream\nendobj\n%%EOF\n");

    private sealed record QuoteState(
        IReadOnlyList<Quote> Quotes, IReadOnlyList<QuoteExtractionJob> Jobs, IReadOnlyList<QuoteLine> Lines);

    private sealed class Host
    {
        /// <summary>The host's fixed clock.</summary>
        public static readonly DateTimeOffset Now = new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);

        public required WebApplicationFactory<Program> Factory { get; init; }
        public required RecordingAiGateway Gateway { get; init; }
        public required RecordingAuditWriter Audit { get; init; }

        public static Host Create(WebApplicationFactory<Program> baseFactory)
        {
            var gateway = new RecordingAiGateway(
                new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now), new AiGatewayOcrOptions()));
            var storage = new RecordingDocumentStorage();
            var audit = new RecordingAuditWriter();
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
                }));

            return new Host { Factory = factory, Gateway = gateway, Audit = audit };
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

        public async Task<EntityId> AddOpenJobAsync(Guid tenantId, Guid quoteId, DateTimeOffset queuedAt)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
            var job = new QuoteExtractionJob
            {
                TenantId = new TenantId(tenantId),
                QuoteId = new EntityId(quoteId),
                Status = QuoteExtractionJobStatus.Running,
                QueuedAt = queuedAt,
                StartedAt = queuedAt,
            };
            db.QuoteExtractionJobs.Add(job);
            await db.SaveChangesAsync();
            return job.Id;
        }

        public async Task AgeJobAsync(EntityId jobId, DateTimeOffset when)
        {
            using var scope = Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
            var job = await db.QuoteExtractionJobs.SingleAsync(j => j.Id == jobId);
            job.QueuedAt = when;
            job.StartedAt = when;
            await db.SaveChangesAsync();
        }
    }
}
