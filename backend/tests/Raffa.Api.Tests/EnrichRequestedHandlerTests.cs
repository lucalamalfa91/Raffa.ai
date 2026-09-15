using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// Fix: route failed enrich to manual review (NeedsReview), and skip extraction clobber when the
/// contract was already human-edited (Version > 1) before the <see cref="EnrichRequestedHandler"/>
/// message is processed.
///
/// <para>These tests use the in-process <see cref="InMemoryEnrichQueue"/> / intake queues and the
/// in-memory DB provider so they do not require Docker or a running Postgres instance.</para>
/// </summary>
public sealed class EnrichRequestedHandlerTests : IClassFixture<RaffaApiFactory>
{
    private const string MsaText =
        "MASTER SERVICES AGREEMENT between Acme Corp and Contoso Ltd, effective 2026-01-01. " +
        "This Agreement governs all Order Forms executed by the parties. Annual fees are EUR 48,000, " +
        "payable within thirty days of invoice. The initial term is thirty-six months and renews " +
        "automatically unless either party gives ninety days written notice.";

    private readonly WebApplicationFactory<Program> _baseFactory;

    public EnrichRequestedHandlerTests(RaffaApiFactory factory)
    {
        _baseFactory = factory;
    }

    // -------------------------------------------------------------------------
    // Bug 1: failure paths must land on NeedsReview, never leave Processing stuck
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the document bytes have been lost from storage before the enrich message is processed
    /// (e.g. a race with a delete, or a transient storage hiccup that was re-raised as a terminal
    /// null), the enrich handler must set the document to <see cref="DocumentProcessingStatus.NeedsReview"/>
    /// rather than silently returning and leaving it forever in <see cref="DocumentProcessingStatus.Processing"/>.
    /// The provisional identity from the intake headline pass stays intact so the row is not blank.
    /// </summary>
    [Fact]
    public async Task Bytes_lost_before_enrich_sets_NeedsReview_and_completes_message()
    {
        var host = Host.Create(_baseFactory);
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();

        // Upload a born-digital MSA PDF.
        using var content = Multipart(BuildPdf(MsaText), "lost-bytes.pdf", "application/pdf");
        using var request = Upload(content, tenantId.ToString(), userId: "admin@acme.example");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var documentId = body.RootElement.GetProperty("id").GetGuid();

        // Run intake ONLY (do not auto-drain the enrich queue).
        await DrainIntakeOnlyAsync(host.Factory);

        // Forget the bytes so the enrich handler finds nothing in storage.
        // Filter to the document blob (not the preview PNG, which DocumentPreviewService may
        // also have stored during intake via storage.SavePreviewAsync).
        var storagePath = host.Storage.Saved
            .Single(s => !s.Path.Contains("preview", StringComparison.OrdinalIgnoreCase))
            .Path;
        host.Storage.Forget(storagePath);

        // Record how many AI extract calls happened during intake so we can assert that enrich
        // made NO additional ExtractAsync calls (bytes load fails before parsing/extraction).
        var extractCallsAfterIntake = host.Gateway.Calls.Count(c => c == "ExtractAsync");

        // Drain the enrich queue.
        await host.Factory.DrainEnrichQueueAsync();

        // The document must be NeedsReview (manual data entry invited), not stuck Processing.
        using var scope = host.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
        var doc = await db.Documents.SingleAsync(d => d.Id == new EntityId(documentId));
        Assert.Equal(DocumentProcessingStatus.NeedsReview, doc.ProcessingStatus);

        // No new ExtractAsync calls — enrich short-circuited at bytes-load failure before extract.
        Assert.Equal(extractCallsAfterIntake, host.Gateway.Calls.Count(c => c == "ExtractAsync"));
    }

    // -------------------------------------------------------------------------
    // Bug 2: Version > 1 before enrich must skip extraction, not clobber human edits
    // -------------------------------------------------------------------------

    /// <summary>
    /// When a human corrects the contract fields while the enrich message is still in the queue
    /// (<see cref="Contract.Version"/> increments on each human correction), the enrich handler
    /// must skip the 7-stage <see cref="StagedExtractionService.RunAsync"/> so it does not
    /// overwrite the human's edits, promote the contract to <see cref="ContractIdentityState.Official"/>,
    /// and route the document to <see cref="DocumentProcessingStatus.NeedsReview"/> for the user
    /// to validate their own corrections — no dead-letter, no DLQ.
    /// </summary>
    [Fact]
    public async Task Human_edit_before_enrich_skips_extraction_and_sets_NeedsReview()
    {
        var host = Host.Create(_baseFactory);
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();

        // Upload a born-digital MSA PDF.
        using var content = Multipart(BuildPdf(MsaText), "human-edited.pdf", "application/pdf");
        using var request = Upload(content, tenantId.ToString(), userId: "admin@acme.example");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var documentId = body.RootElement.GetProperty("id").GetGuid();

        // Run intake ONLY (do not auto-drain the enrich queue).
        await DrainIntakeOnlyAsync(host.Factory);

        // Simulate a human correction: bump the contract's Version to 2,
        // mimicking what ContractCorrectionService does via PATCH /api/contracts/{id}.
        EntityId? contractId;
        using (var setupScope = host.Factory.Services.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            var doc = await db.Documents.SingleAsync(d => d.Id == new EntityId(documentId));
            contractId = doc.ContractId;
            Assert.NotNull(contractId); // intake must have created the contract shell

            var contract = await db.Contracts.SingleAsync(c => c.Id == contractId);
            // Simulate ContractCorrectionService bumping Version: the InMemory provider applies
            // the concurrency token check on SaveChanges, so we directly set the value we want.
            contract.Version = 2;
            await db.SaveChangesAsync();
        }

        // Record ExtractAsync call count so far (intake: headline ExtractAsync) so we can prove
        // that enrich did NOT run any additional staged-extract calls (skipped due to Version > 1).
        // EmbedAsync calls for retrieval indexing are still expected even when extraction is skipped.
        var extractCallsBeforeEnrich = host.Gateway.Calls.Count(c => c == "ExtractAsync");

        // Drain the enrich queue.
        await host.Factory.DrainEnrichQueueAsync();

        // No new ExtractAsync calls — staged extraction was skipped because Version > 1.
        Assert.Equal(extractCallsBeforeEnrich, host.Gateway.Calls.Count(c => c == "ExtractAsync"));

        // Document must be NeedsReview so the user can validate their own corrections.
        using var assertScope = host.Factory.Services.CreateScope();
        var assertDb = assertScope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
        var finalDoc = await assertDb.Documents.SingleAsync(d => d.Id == new EntityId(documentId));
        Assert.Equal(DocumentProcessingStatus.NeedsReview, finalDoc.ProcessingStatus);

        // Contract must be Official (human corrections are the authoritative identity).
        var finalContract = await assertDb.Contracts.SingleAsync(c => c.Id == contractId);
        Assert.Equal(ContractIdentityState.Official, finalContract.IdentityState);
    }

    // -------------------------------------------------------------------------
    // Test helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Runs only the intake (ExtractionRequested) messages — does NOT auto-drain the enrich queue
    /// the way <see cref="InMemoryAskEngineFactory.DrainExtractionQueueAsync"/> does. Allows a
    /// test to manipulate state (contract version, storage bytes) between intake and enrich.
    /// </summary>
    private static async Task DrainIntakeOnlyAsync(WebApplicationFactory<Program> factory)
    {
        var queue = factory.Services.GetRequiredService<InMemoryExtractionQueue>();
        while (queue.Reader.TryRead(out var message))
        {
            using var scope = factory.Services.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ExtractionRequestedHandler>();
            await handler.HandleAsync(message).ConfigureAwait(false);
        }
    }

    private static MultipartFormDataContent Multipart(byte[] bytes, string fileName, string? contentType = null)
    {
        var file = new ByteArrayContent(bytes);
        if (contentType is not null)
        {
            file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }

        return new MultipartFormDataContent { { file, "file", fileName } };
    }

    private static HttpRequestMessage Upload(HttpContent content, string tenantId, string? userId = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/documents") { Content = content };
        request.Headers.Add("X-Tenant-Id", tenantId);
        if (userId is not null)
        {
            request.Headers.Add("X-User-Id", userId);
        }

        return request;
    }

    /// <summary>Same hand-built PDF shape as <c>DocumentUploadEndpointTests.BuildPdf</c>.</summary>
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

    private sealed class Host
    {
        private static readonly DateTimeOffset Now = new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);

        public required WebApplicationFactory<Program> Factory { get; init; }
        public required RecordingAiGateway Gateway { get; init; }
        public required RecordingDocumentStorage Storage { get; init; }

        public static Host Create(WebApplicationFactory<Program> baseFactory)
        {
            var gateway = new RecordingAiGateway(
                new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now), new AiGatewayOcrOptions()));
            var storage = new RecordingDocumentStorage();

            var factory = baseFactory
                .WithPresentedCallersAsMembers()
                .WithInMemoryAskEngine(gateway, new FixedClock(Now), storage);

            return new Host { Factory = factory, Gateway = gateway, Storage = storage };
        }
    }
}
