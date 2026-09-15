using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// <c>POST /api/documents</c> (task E13/F04/US01/T01, documents-admission, story us-01-documents-v2
/// AC-1/AC-2/AC-6): the 400 / 413 / 415 / 201 paths in order. Runs on the in-memory host
/// (<see cref="InMemoryAskEngineFactory"/>) with the real <see cref="FixtureAiGateway"/> behind a
/// call-recording decorator, so "zero gateway calls" on a 415 is proven, not assumed.
///
/// <para>
/// Since task E16/F02/US03/T01 (wave w15, ADR-027 §D1) the request returns 201 at the store and
/// the content gate — parse/OCR, readable-text floor, <c>classify</c>, threshold — runs on the
/// Worker. The former 422 paths are therefore two-halved here: a 201 with <b>zero</b> gateway
/// calls and one stored blob, then <see cref="InMemoryAskEngineFactory.DrainExtractionQueueAsync"/>
/// playing the Worker, after which the refusal is a <c>Rejected</c> <b>row</b> carrying its reason
/// code, the blob is deleted and the gate's <c>document.rejected</c> audit row is written as
/// before. "Gate before persistence" (ADR-024) became "gate before <em>counting</em>": a rejected
/// document is listed, never counted in <c>counts.all</c>, never askable (ADR-027 §D6/§D7).
/// </para>
/// </summary>
public sealed class DocumentUploadEndpointTests : IClassFixture<RaffaApiFactory>
{
    private const string MsaText =
        "MASTER SERVICES AGREEMENT between Acme Corp and Contoso Ltd, effective 2026-01-01. " +
        "This Agreement governs all Order Forms executed by the parties. Annual fees are EUR 48,000, " +
        "payable within thirty days of invoice. The initial term is thirty-six months and renews " +
        "automatically unless either party gives ninety days written notice.";

    private const string RecipeText =
        "Spaghetti alla carbonara for four. Boil 400 g of spaghetti in salted water. Meanwhile fry " +
        "150 g of guanciale until crisp. Whisk four egg yolks with 100 g of grated pecorino and " +
        "plenty of black pepper. Drain the pasta, toss with the guanciale off the heat, then fold in " +
        "the egg mixture until creamy. Serve immediately with extra pecorino.";

    private const string QuoteText =
        "QUOTE number Q-2026-0417 from Northwind Traders for Fabrikam Inc, valid for thirty days. " +
        "Line 1: Cloud Suite Enterprise, 250 seats, unit price EUR 96.00 per seat per year, " +
        "discount 12 percent. Line 2: Premium support, one year, EUR 4,800. Payment terms net thirty. " +
        "Prices exclude VAT and are subject to the attached terms of sale.";

    private readonly WebApplicationFactory<Program> _baseFactory;

    public DocumentUploadEndpointTests(RaffaApiFactory factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task Missing_tenant_header_returns_400()
    {
        var host = Host.Create(_baseFactory);
        var client = host.Factory.CreateClient();
        using var content = Multipart([1, 2, 3], "contract.pdf");

        var response = await client.PostAsync("/api/documents", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Gateway.Calls);
    }

    [Fact]
    public async Task Invalid_tenant_header_returns_400()
    {
        var host = Host.Create(_baseFactory);
        var client = host.Factory.CreateClient();
        using var content = Multipart([1, 2, 3], "contract.pdf");
        using var request = Upload(content, tenantId: "not-a-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Missing_file_field_returns_400()
    {
        var host = Host.Create(_baseFactory);
        var client = host.Factory.CreateClient();
        using var content = new MultipartFormDataContent { { new StringContent("not-a-file"), "note" } };
        using var request = Upload(content, Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Zip_renamed_pdf_returns_415_without_touching_the_ai_gateway_or_storage()
    {
        var host = Host.Create(_baseFactory);
        var client = host.Factory.CreateClient();
        var zipBytes = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x08, 0x00 };
        using var content = Multipart(zipBytes, "contract.pdf", "application/pdf");
        using var request = Upload(content, Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(DocumentFormatSniffer.RejectionMessage, await response.Content.ReadFromJsonAsync<string>());
        Assert.Empty(host.Gateway.Calls);
        Assert.Empty(host.Storage.Saved);
        Assert.Empty(host.Audit.Entries);
    }

    [Theory]
    [InlineData("contract.tiff")]
    [InlineData("contract.txt")]
    [InlineData("contract")]
    public async Task Unsupported_extension_returns_415_even_with_pdf_bytes(string fileName)
    {
        var host = Host.Create(_baseFactory);
        var client = host.Factory.CreateClient();
        using var content = Multipart(BuildPdf(MsaText), fileName);
        using var request = Upload(content, Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(host.Gateway.Calls);
    }

    [Fact]
    public async Task File_over_the_configured_limit_returns_413_before_any_parse_or_model_call()
    {
        var host = Host.Create(_baseFactory, configure: settings => settings["Documents:MaxFileBytes"] = "2097152");
        var client = host.Factory.CreateClient();
        var threeMegabytes = new byte[3 * 1024 * 1024];
        "%PDF-1.4\n"u8.CopyTo(threeMegabytes);
        using var content = Multipart(threeMegabytes, "huge.pdf", "application/pdf");
        using var request = Upload(content, Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("Raffa accepts files up to 2 MB. This file is larger.", await response.Content.ReadFromJsonAsync<string>());
        Assert.Empty(host.Gateway.Calls);
        Assert.Empty(host.Storage.Saved);
    }

    [Fact]
    public async Task Recipe_pdf_is_stored_at_201_then_rejected_by_the_worker_with_a_reason_code()
    {
        var host = Host.Create(_baseFactory);
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var bytes = BuildPdf(RecipeText);
        using var content = Multipart(bytes, "carbonara.pdf", "application/pdf");
        using var request = Upload(content, tenantId.ToString(), userId: "chef@acme.example");

        var response = await client.SendAsync(request);

        // ADR-027 §D1 (task E16/F02/US03/T01): the request stores and returns. No model has been
        // called yet -- the content verdict is the Worker's, so a recipe is 201 like anything else.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var documentId = body.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("Uploaded", body.RootElement.GetProperty("processingStatus").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("contractId").ValueKind);
        Assert.Empty(host.Gateway.Calls);
        Assert.Single(host.Storage.Saved);
        Assert.DoesNotContain(host.Audit.Entries, e => e.Action == "document.rejected");

        // The Worker's half: parse, classify, refuse -- and the refusal is a ROW, not a 422
        // (ADR-027 §D6): status Rejected, the reason as a code, the detected type and confidence
        // for the screen to render, the blob deleted, the classification job completed.
        Assert.Equal(1, await host.Factory.DrainExtractionQueueAsync());
        Assert.Equal(["OcrAsync", "ClassifyAsync"], host.Gateway.Calls);
        using (var scope = host.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            var document = await dbContext.Documents.SingleAsync(d => d.Id == new EntityId(documentId));
            Assert.Equal(DocumentProcessingStatus.Rejected, document.ProcessingStatus);
            Assert.Equal(AdmissionRejectionReason.NotAContract, document.RejectionReason);
            Assert.Equal(ContractDocumentType.Other, document.RejectionDetectedType);
            Assert.Equal(0.5, document.RejectionConfidence);
            Assert.Equal(0, await dbContext.Embeddings.CountAsync());
            var job = Assert.Single(await dbContext.ExtractionJobs.ToListAsync());
            Assert.Equal(ExtractionJobStatus.Completed, job.Status);
        }
        Assert.Contains(host.Storage.Deleted, path => path.EndsWith("carbonara.pdf", StringComparison.Ordinal));

        // The gate's own audit row, exactly as before, now attributed to the Worker: content-free,
        // SHA-256 of the bytes as the resource id, no file name.
        var audit = Assert.Single(host.Audit.Entries, e => e.Action == "document.rejected");
        Assert.Equal(new TenantId(tenantId), audit.TenantId);
        Assert.Equal(ExtractionRequestedHandler.WorkerActor, audit.Actor);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), audit.ResourceId);
        Assert.NotNull(audit.Detail);
        Assert.Contains("not_a_contract", audit.Detail);
        Assert.DoesNotContain("carbonara", audit.Detail, StringComparison.OrdinalIgnoreCase);

        // And the list tells the screen: a Rejected row is listed, never counted in `all`.
        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/documents");
        list.Headers.Add("X-Tenant-Id", tenantId.ToString());
        using var listBody = JsonDocument.Parse(await (await client.SendAsync(list)).Content.ReadAsStringAsync());
        var item = Assert.Single(listBody.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Rejected", item.GetProperty("processingStatus").GetString());
        Assert.Equal("not_a_contract", item.GetProperty("rejectionReason").GetString());
        var counts = listBody.RootElement.GetProperty("counts");
        Assert.Equal(0, counts.GetProperty("all").GetInt32());
        Assert.Equal(1, counts.GetProperty("rejected").GetInt32());
    }

    [Fact]
    public async Task Photo_without_readable_text_is_stored_at_201_then_rejected_no_readable_text_with_confidence_zero()
    {
        var host = Host.Create(_baseFactory);
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();
        // A PNG signature followed by a few bytes of "text": far below Documents:MinReadableChars.
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. "nonna"u8.ToArray()];
        using var content = Multipart(png, "nonna.png", "image/png");
        using var request = Upload(content, tenantId.ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Empty(host.Gateway.Calls);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var documentId = body.RootElement.GetProperty("id").GetGuid();

        // Worker: the OCR role reads nothing usable -> Rejected with the no_readable_text code and
        // a zero confidence the screen can show; the classify role is never reached.
        Assert.Equal(1, await host.Factory.DrainExtractionQueueAsync());
        Assert.Equal(["OcrAsync"], host.Gateway.Calls);
        using (var scope = host.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            var document = await dbContext.Documents.SingleAsync(d => d.Id == new EntityId(documentId));
            Assert.Equal(DocumentProcessingStatus.Rejected, document.ProcessingStatus);
            Assert.Equal(AdmissionRejectionReason.NoReadableText, document.RejectionReason);
            Assert.Equal(ContractDocumentType.Other, document.RejectionDetectedType);
            Assert.Equal(0, document.RejectionConfidence);
        }
        var audit = Assert.Single(host.Audit.Entries, e => e.Action == "document.rejected");
        Assert.Equal(ExtractionRequestedHandler.WorkerActor, audit.Actor);
        Assert.Contains("no_readable_text", audit.Detail);
    }

    [Fact]
    public async Task Msa_pdf_returns_201_is_stored_once_and_classified_once()
    {
        var host = Host.Create(_baseFactory);
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var bytes = BuildPdf(MsaText);
        // Deliberately the browser's own vague content type: the sniffed canonical one must win.
        using var content = Multipart(bytes, "msa-acme.pdf", "application/octet-stream");
        using var request = Upload(content, tenantId.ToString());

        var response = await client.SendAsync(request);

        // The request's own contract (ADR-027 §D1): 201 at the store, before any model call. No
        // contractId yet -- classification has not run, and a fabricated link is the guessing this
        // wave removes -- and the status is the row's, Uploaded.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var documentId = body.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("msa-acme.pdf", body.RootElement.GetProperty("fileName").GetString());
        Assert.Equal("application/pdf", body.RootElement.GetProperty("mimeType").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("contractId").ValueKind);
        Assert.Equal("Uploaded", body.RootElement.GetProperty("processingStatus").GetString());
        Assert.Equal($"/api/documents/{documentId}", response.Headers.Location?.ToString());
        Assert.Empty(host.Gateway.Calls);
        var saved = Assert.Single(host.Storage.Saved);
        Assert.EndsWith("msa-acme.pdf", saved.Path, StringComparison.Ordinal);
        Assert.Equal(bytes, saved.Content);
        Assert.Contains(host.Audit.Entries, e => e.Action == "document.uploaded");

        // The Worker's half, played by the test host: the model is called once per role, the
        // first-page preview task E13/F04/US01/T02 renders is the second stored object.
        Assert.Equal(1, await host.Factory.DrainExtractionQueueAsync());
        Assert.Equal(2, host.Storage.Saved.Count);
        Assert.Contains(host.Storage.Saved, s => s.Path.EndsWith("/preview/page-1.png", StringComparison.Ordinal));
        Assert.Equal(1, host.Gateway.Calls.Count(call => call == "ClassifyAsync"));
        // instant-identity-ingest two-queue split: intake parses (OCR for this minimal test PDF
        // that PdfPig cannot parse) and the enrich pass re-parses for the 7-stage extraction —
        // so OCR is called once per pass = 2 for a scanned/non-native-readable PDF.
        // Born-digital PDFs that PdfPig CAN parse will only see 1 OCR (intake only).
        Assert.InRange(host.Gateway.Calls.Count(call => call == "OcrAsync"), 1, 2);
        Assert.DoesNotContain(host.Audit.Entries, e => e.Action == "document.rejected");

        using var get = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/{documentId}");
        get.Headers.Add("X-Tenant-Id", tenantId.ToString());
        var getResponse = await client.SendAsync(get);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var metadata = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        Assert.Equal("Msa", metadata.RootElement.GetProperty("documentType").GetString());
        Assert.Equal("application/pdf", metadata.RootElement.GetProperty("mimeType").GetString());
    }

    /// <summary>
    /// S-T28 (task E18/F03/US02/T01, NW-32; ADR-011 w16 clause 15): a signed POST names its actor —
    /// the resolved token subject, never a placeholder — on both <c>DocumentVersion.CreatedBy</c>
    /// and the <c>document.uploaded</c> audit row. <see cref="PresentedCallersAsMembersPolicy"/>
    /// makes the presented caller a member of the tenant it names, so this is a real, non-Admin-
    /// implicit round trip: <c>X-User-Id</c> becomes the authenticated <c>oid</c>
    /// (<see cref="TestUserIdAuthenticationHandler"/>), never the retired header, and never the
    /// deleted <c>"unattributed"</c> literal.
    /// </summary>
    [Fact]
    public async Task Signed_upload_records_the_callers_resolved_subject_never_a_placeholder()
    {
        var host = Host.Create(_baseFactory);
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();
        const string presentedActor = "reviewer@acme.example";
        using var content = Multipart(BuildPdf(MsaText), "msa-signed.pdf", "application/pdf");
        using var request = Upload(content, tenantId.ToString(), userId: presentedActor);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var documentId = body.RootElement.GetProperty("id").GetGuid();

        using (var scope = host.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            var version = await dbContext.DocumentVersions.SingleAsync(v => v.DocumentId == new EntityId(documentId));
            Assert.Equal(presentedActor, version.CreatedBy);
        }

        var audit = Assert.Single(host.Audit.Entries, e => e.Action == "document.uploaded");
        Assert.Equal(presentedActor, audit.Actor);
    }

    [Fact]
    public async Task Quote_pdf_is_admitted_and_stored_as_its_own_type()
    {
        var host = Host.Create(_baseFactory);
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();
        using var content = Multipart(BuildPdf(QuoteText), "quote.pdf", "application/pdf");
        using var request = Upload(content, tenantId.ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var documentId = body.RootElement.GetProperty("id").GetGuid();

        // The type is the Worker's verdict (ADR-027 §D1), so play the Worker before reading it.
        Assert.Equal(1, await host.Factory.DrainExtractionQueueAsync());
        using var get = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/{documentId}");
        get.Headers.Add("X-Tenant-Id", tenantId.ToString());
        var getResponse = await client.SendAsync(get);
        using var metadata = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        Assert.Equal("Quote", metadata.RootElement.GetProperty("documentType").GetString());
    }

    [Fact]
    public async Task Scanned_msa_jpeg_is_admitted_through_the_ocr_path()
    {
        var host = Host.Create(_baseFactory);
        var client = host.Factory.CreateClient();
        byte[] scanned = [0xFF, 0xD8, 0xFF, .. Encoding.UTF8.GetBytes(MsaText)];
        using var content = Multipart(scanned, "scan.jpg", "image/jpeg");
        using var request = Upload(content, Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("image/jpeg", body.RootElement.GetProperty("mimeType").GetString());
        Assert.Empty(host.Gateway.Calls);

        // instant-identity-ingest two-queue split: intake OCRs (classify runs once), then the
        // enrich pass re-parses and re-OCRs the image to get text for the 7-stage extraction.
        // OcrAsync: 2 (one per pass for images that PdfPig cannot read).
        // ClassifyAsync: 1 (intake only — enrich uses the already-set DocumentType).
        Assert.Equal(1, await host.Factory.DrainExtractionQueueAsync());
        Assert.Equal(2, host.Gateway.Calls.Count(call => call == "OcrAsync"));
        Assert.Equal(1, host.Gateway.Calls.Count(call => call == "ClassifyAsync"));
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

    /// <summary>Same hand-built shape as <c>R1ExtractionFixtures.BuildBornDigitalPdfBytes</c>: one
    /// page object, one text-bearing content stream, Latin-1 bytes.</summary>
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
        public required RecordingAuditWriter Audit { get; init; }

        public static Host Create(
            WebApplicationFactory<Program> baseFactory, Action<Dictionary<string, string?>>? configure = null)
        {
            var settings = new Dictionary<string, string?>();
            configure?.Invoke(settings);

            var gateway = new RecordingAiGateway(
                new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now), new AiGatewayOcrOptions()));
            var storage = new RecordingDocumentStorage();
            var audit = new RecordingAuditWriter();

            var factory = baseFactory
                .WithWebHostBuilder(builder =>
                {
                    foreach (var (key, value) in settings)
                    {
                        builder.UseSetting(key, value);
                    }
                })
                .WithPresentedCallersAsMembers()
                .WithInMemoryAskEngine(gateway, new FixedClock(Now), storage, audit);

            return new Host { Factory = factory, Gateway = gateway, Storage = storage, Audit = audit };
        }
    }
}
