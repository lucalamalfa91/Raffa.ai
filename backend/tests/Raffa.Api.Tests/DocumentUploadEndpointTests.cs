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
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// <c>POST /api/documents</c> (task E13/F04/US01/T01, documents-admission, story us-01-documents-v2
/// AC-1/AC-2/AC-6): the 400 / 413 / 415 / 422 / 201 paths in order, and — the point of ADR-024
/// "gate before persistence" — that a rejected upload leaves no blob, no <c>document</c> row and
/// exactly one <c>document.rejected</c> audit row. Runs on the in-memory host
/// (<see cref="InMemoryAskEngineFactory"/>) with the real <see cref="FixtureAiGateway"/> behind a
/// call-recording decorator, so "zero gateway calls" on a 415 is proven, not assumed.
/// </summary>
public sealed class DocumentUploadEndpointTests : IClassFixture<WebApplicationFactory<Program>>
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

    public DocumentUploadEndpointTests(WebApplicationFactory<Program> factory)
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
    public async Task Recipe_pdf_returns_422_not_a_contract_and_persists_nothing()
    {
        var host = Host.Create(_baseFactory);
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var bytes = BuildPdf(RecipeText);
        using var content = Multipart(bytes, "carbonara.pdf", "application/pdf");
        using var request = Upload(content, tenantId.ToString(), userId: "chef@acme.example");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("rejected").GetBoolean());
        Assert.Equal("Other", body.RootElement.GetProperty("detectedType").GetString());
        Assert.Equal(0.5, body.RootElement.GetProperty("confidence").GetDouble());
        Assert.Equal("not_a_contract", body.RootElement.GetProperty("reason").GetString());
        Assert.Equal(DocumentAdmissionGate.Hint, body.RootElement.GetProperty("hint").GetString());

        // Gate before persistence: no blob, no document row, no embedding, no extraction job.
        Assert.Empty(host.Storage.Saved);
        using (var scope = host.Factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            Assert.Equal(0, await dbContext.Documents.CountAsync());
            Assert.Equal(0, await dbContext.Embeddings.CountAsync());
            Assert.Equal(0, await dbContext.ExtractionJobs.CountAsync());
        }

        // Exactly one audit row, content-free: SHA-256 of the bytes as the resource id, no file name.
        var audit = Assert.Single(host.Audit.Entries);
        Assert.Equal(new TenantId(tenantId), audit.TenantId);
        Assert.Equal("chef@acme.example", audit.Actor);
        Assert.Equal("document.rejected", audit.Action);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), audit.ResourceId);
        Assert.NotNull(audit.Detail);
        Assert.Contains("not_a_contract", audit.Detail);
        Assert.DoesNotContain("carbonara", audit.Detail, StringComparison.OrdinalIgnoreCase);

        // Classified exactly once; nothing embedded or extracted.
        Assert.Equal(["OcrAsync", "ClassifyAsync"], host.Gateway.Calls);
    }

    [Fact]
    public async Task Photo_without_readable_text_returns_422_no_readable_text_with_confidence_zero()
    {
        var host = Host.Create(_baseFactory);
        var client = host.Factory.CreateClient();
        // A PNG signature followed by a few bytes of "text": far below Documents:MinReadableChars.
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. "nonna"u8.ToArray()];
        using var content = Multipart(png, "nonna.png", "image/png");
        using var request = Upload(content, Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("no_readable_text", body.RootElement.GetProperty("reason").GetString());
        Assert.Equal("Other", body.RootElement.GetProperty("detectedType").GetString());
        Assert.Equal(0, body.RootElement.GetProperty("confidence").GetDouble());
        Assert.Equal(["OcrAsync"], host.Gateway.Calls);
        Assert.Empty(host.Storage.Saved);
        var audit = Assert.Single(host.Audit.Entries);
        Assert.Equal("unattributed", audit.Actor);
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

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var documentId = body.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("msa-acme.pdf", body.RootElement.GetProperty("fileName").GetString());
        Assert.Equal("application/pdf", body.RootElement.GetProperty("mimeType").GetString());
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("contractId").ValueKind);
        Assert.NotEqual("Failed", body.RootElement.GetProperty("processingStatus").GetString());
        Assert.Equal($"/api/documents/{documentId}", response.Headers.Location?.ToString());

        // Two objects: the document blob and the first-page preview task E13/F04/US01/T02 renders.
        Assert.Equal(2, host.Storage.Saved.Count);
        var saved = host.Storage.Saved.Single(s => s.Path.EndsWith("msa-acme.pdf", StringComparison.Ordinal));
        Assert.Equal(bytes, saved.Content);
        Assert.Contains(host.Storage.Saved, s => s.Path.EndsWith("/preview/page-1.png", StringComparison.Ordinal));
        Assert.Equal(1, host.Gateway.Calls.Count(call => call == "ClassifyAsync"));
        // Every PDF is read by the `ocr` role exactly once (ADR-017 amendment 2026-09-09).
        Assert.Equal(1, host.Gateway.Calls.Count(call => call == "OcrAsync"));
        Assert.Contains(host.Audit.Entries, e => e.Action == "document.uploaded");
        Assert.DoesNotContain(host.Audit.Entries, e => e.Action == "document.rejected");

        using var get = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/{documentId}");
        get.Headers.Add("X-Tenant-Id", tenantId.ToString());
        var getResponse = await client.SendAsync(get);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        using var metadata = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        Assert.Equal("Msa", metadata.RootElement.GetProperty("documentType").GetString());
        Assert.Equal("application/pdf", metadata.RootElement.GetProperty("mimeType").GetString());
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
        Assert.Equal(1, host.Gateway.Calls.Count(call => call == "OcrAsync"));
        Assert.Equal(1, host.Gateway.Calls.Count(call => call == "ClassifyAsync"));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("image/jpeg", body.RootElement.GetProperty("mimeType").GetString());
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
                .WithInMemoryAskEngine(gateway, new FixedClock(Now), storage, audit);

            return new Host { Factory = factory, Gateway = gateway, Storage = storage, Audit = audit };
        }
    }
}
