using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// Task E16/F03/US02/T01 (priority by claim; ADR-027 w15 footer C12), built by hand on 2026-09-14.
/// <c>POST /api/documents/{id}/prioritise</c> over the real HTTP pipeline, and the Worker's
/// queue-jump over the real <c>ExtractionRequestedHandler</c> played by
/// <see cref="InMemoryAskEngineFactory.DrainExtractionQueueAsync(WebApplicationFactory{Program}, int)"/>.
/// Order is proven on the <see cref="ClaimLog"/> the in-memory claim store writes, never on
/// timestamps: this host runs a <see cref="FixedClock"/>.
/// </summary>
public sealed class DocumentPriorityEndpointTests : IClassFixture<RaffaApiFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 18, 5, 0, TimeSpan.Zero);

    /// <summary>The same MSA <see cref="DocumentUploadEndpointTests"/> proves the in-memory gate
    /// admits and the fixture gateway classifies as <c>Msa</c>: long enough for the readable-text
    /// floor, keyword on the first line.</summary>
    private const string MsaText =
        "MASTER SERVICES AGREEMENT between Acme Corp and Contoso Ltd, effective 2026-01-01. " +
        "This Agreement governs all Order Forms executed by the parties. Annual fees are EUR 48,000, " +
        "payable within thirty days of invoice. The initial term is thirty-six months and renews " +
        "automatically unless either party gives ninety days written notice.";

    private readonly WebApplicationFactory<Program> _baseFactory;

    public DocumentPriorityEndpointTests(RaffaApiFactory factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task Opening_a_queued_document_stamps_its_job_and_audits_once()
    {
        var host = CreateHost();
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var documentId = await UploadAsync(client, tenantId, "queued.pdf");

        var first = await PrioritiseAsync(client, tenantId, documentId);
        var second = await PrioritiseAsync(client, tenantId, documentId);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        using (var scope = host.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            using var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(new TenantId(tenantId));
            var job = await db.ExtractionJobs.SingleAsync(j => j.DocumentId == new EntityId(documentId));
            Assert.Equal(Now, job.PrioritisedAt);
            Assert.Null(job.ClaimedAt);
        }

        Assert.Single(host.Audit.Entries, e =>
            e.Action == DocumentPriorityService.PrioritisedAuditAction && e.ResourceId == documentId.ToString());
    }

    [Fact]
    public async Task An_unknown_document_is_404_and_a_non_guid_is_400()
    {
        var client = CreateHost().Factory.CreateClient();
        var tenantId = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await PrioritiseAsync(client, tenantId, Guid.NewGuid())).StatusCode);

        using var malformed = new HttpRequestMessage(HttpMethod.Post, "/api/documents/not-a-guid/prioritise");
        malformed.Headers.Add("X-Tenant-Id", tenantId.ToString());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.SendAsync(malformed)).StatusCode);
    }

    [Fact]
    public async Task A_prioritised_document_is_taken_by_the_next_delivery_ahead_of_the_fifo_and_never_twice()
    {
        var host = CreateHost();
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var a = await UploadAsync(client, tenantId, "a.pdf");
        var b = await UploadAsync(client, tenantId, "b.pdf");
        var c = await UploadAsync(client, tenantId, "c.pdf");

        // The user opens C while A, B, C are all still queued.
        Assert.Equal(HttpStatusCode.NoContent, (await PrioritiseAsync(client, tenantId, c)).StatusCode);

        // ONE delivery (A's message): it must take C first, then A, and leave B untouched.
        Assert.Equal(1, await host.Factory.DrainExtractionQueueAsync(maxMessages: 1));
        var claimLog = host.Factory.Services.GetRequiredService<ClaimLog>();
        var jobIds = await JobIdsAsync(host, tenantId, a, b, c);
        Assert.Equal([jobIds[c], jobIds[a]], claimLog.ClaimedInOrder.Select(j => j).ToArray());
        await AssertTerminalAsync(host, tenantId, c);
        await AssertTerminalAsync(host, tenantId, a);
        await AssertStillQueuedAsync(host, tenantId, b);
        var callsAfterFirstDelivery = host.Gateway.Calls.Count;

        // The rest of the channel: B's delivery runs B; C's own delivery loses the claim to a row
        // that exists and is completed -- C is never processed a second time.
        Assert.Equal(2, await host.Factory.DrainExtractionQueueAsync());
        Assert.Equal([jobIds[c], jobIds[a], jobIds[b]], claimLog.ClaimedInOrder.ToArray());
        await AssertTerminalAsync(host, tenantId, b);
        using (var scope = host.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            using var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(new TenantId(tenantId));
            var cJob = await db.ExtractionJobs.SingleAsync(j => j.Id == jobIds[c]);
            Assert.Equal(1, cJob.AttemptCount);
        }

        // B's delivery cost exactly one document's worth of gateway calls; C's own delivery cost none.
        var callsPerDocument = callsAfterFirstDelivery / 2;
        Assert.Equal(callsAfterFirstDelivery + callsPerDocument, host.Gateway.Calls.Count);
    }

    [Fact]
    public async Task A_prioritised_document_whose_bytes_are_gone_fails_alone_and_the_deliverys_own_job_still_runs()
    {
        var host = CreateHost();
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var a = await UploadAsync(client, tenantId, "a.pdf");
        var c = await UploadAsync(client, tenantId, "c.pdf");
        Assert.Equal(HttpStatusCode.NoContent, (await PrioritiseAsync(client, tenantId, c)).StatusCode);

        // C's blob vanishes before the Worker gets to it.
        using (var scope = host.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            using var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(new TenantId(tenantId));
            var cDocument = await db.Documents.SingleAsync(d => d.Id == new EntityId(c));
            host.Storage.Forget(cDocument.StoragePath);
        }

        Assert.Equal(1, await host.Factory.DrainExtractionQueueAsync(maxMessages: 1));

        await AssertStatusAsync(host, tenantId, c, DocumentProcessingStatus.Failed);
        await AssertTerminalAsync(host, tenantId, a);
    }

    /// <summary>The classification job's id for each document -- the only stage the queue-jump ever
    /// touches. A fully processed document also carries one <see cref="ExtractionJob"/> row per
    /// later stage (<see cref="StagedExtractionService"/>), so the lookup must name the stage: an
    /// unfiltered query throws once any of these documents has finished classification.</summary>
    private static async Task<Dictionary<Guid, EntityId>> JobIdsAsync(Host host, Guid tenantId, params Guid[] documentIds)
    {
        using var scope = host.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(new TenantId(tenantId));
        var result = new Dictionary<Guid, EntityId>();
        foreach (var documentId in documentIds)
        {
            result[documentId] = (await db.ExtractionJobs.SingleAsync(
                j => j.DocumentId == new EntityId(documentId) && j.Stage == ExtractionStage.Classification)).Id;
        }

        return result;
    }

    private static async Task AssertTerminalAsync(Host host, Guid tenantId, Guid documentId)
    {
        using var scope = host.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(new TenantId(tenantId));
        var document = await db.Documents.SingleAsync(d => d.Id == new EntityId(documentId));
        Assert.Contains(document.ProcessingStatus, new[] { DocumentProcessingStatus.Completed, DocumentProcessingStatus.NeedsReview });
    }

    private static async Task AssertStatusAsync(Host host, Guid tenantId, Guid documentId, DocumentProcessingStatus expected)
    {
        using var scope = host.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(new TenantId(tenantId));
        var document = await db.Documents.SingleAsync(d => d.Id == new EntityId(documentId));
        Assert.Equal(expected, document.ProcessingStatus);
    }

    private static async Task AssertStillQueuedAsync(Host host, Guid tenantId, Guid documentId)
    {
        using var scope = host.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
        using var tenantScope = scope.ServiceProvider.GetRequiredService<ITenantContext>().BeginScope(new TenantId(tenantId));
        var document = await db.Documents.SingleAsync(d => d.Id == new EntityId(documentId));
        Assert.Equal(DocumentProcessingStatus.Uploaded, document.ProcessingStatus);
        var job = await db.ExtractionJobs.SingleAsync(j => j.DocumentId == new EntityId(documentId));
        Assert.Null(job.ClaimedAt);
    }

    private static async Task<HttpResponseMessage> PrioritiseAsync(HttpClient client, Guid tenantId, Guid documentId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/documents/{documentId}/prioritise");
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        return await client.SendAsync(request);
    }

    private static async Task<Guid> UploadAsync(HttpClient client, Guid tenantId, string fileName)
    {
        var file = new ByteArrayContent(BuildPdf(MsaText));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var content = new MultipartFormDataContent { { file, "file", fileName } };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/documents") { Content = content };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.Created, $"HTTP {(int)response.StatusCode}: {body}");
        using var json = JsonDocument.Parse(body);
        return json.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Same hand-built shape as <c>DocumentUploadEndpointTests.BuildPdf</c>: one page
    /// object, one text-bearing content stream, Latin-1 bytes.</summary>
    private static byte[] BuildPdf(string text)
    {
        var pdf =
            "%PDF-1.4\n" +
            "1 0 obj << /Type /Page >> endobj\n" +
            "2 0 obj << /Length 0 >>\n" +
            "stream\n" +
            $"BT ({text}) Tj ET\n" +
            "endstream\n" +
            "endobj\n" +
            "%%EOF\n";
        return Encoding.Latin1.GetBytes(pdf);
    }

    private Host CreateHost()
    {
        var gateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now), new AiGatewayOcrOptions()));
        var storage = new RecordingDocumentStorage();
        var audit = new RecordingAuditWriter();
        var factory = _baseFactory.WithInMemoryAskEngine(gateway, new FixedClock(Now), storage, audit);
        return new Host(factory, gateway, storage, audit);
    }

    private sealed record Host(
        WebApplicationFactory<Program> Factory, RecordingAiGateway Gateway, RecordingDocumentStorage Storage, RecordingAuditWriter Audit);
}
