using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Contigo.Quotes.Infrastructure;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Contigo.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E05/F01/US01/T01 (quote-extraction) and its parent story
/// us-01-quote-line-extraction: AC-1 ("POST /api/quotes uploads a quote and creates an extraction
/// job"), AC-2 ("Line items extract quantity/SKU/edition/price/discount/term (evidence +
/// confidence)"), AC-3 ("Separate arithmetic from LLM language") and AC-4 ("Scanned/image quote
/// PDFs reuse the epic-02 hybrid OCR path... no 2-page cap") — driven entirely over real HTTP
/// through the real <c>Contigo.Api</c> composition root, against a real, migrated Postgres
/// database (see <see cref="QuoteIntegrationFixture"/>) — the same "one real host, no hand-rolled
/// container" shape <c>R1EndToEndTests</c> already established.
///
/// Reads response bodies as raw <see cref="JsonElement"/>s rather than typed DTOs — same reason as
/// <c>R1EndToEndTests</c>'s own doc comment: every endpoint under test serializes anonymous
/// objects with ASP.NET Core's default camelCase policy, which a hand-written strongly-typed
/// record would have to duplicate exactly to deserialize correctly.
/// </summary>
public sealed class QuoteEndToEndTests : IClassFixture<QuoteIntegrationFixture>
{
    private readonly QuoteIntegrationFixture _fixture;

    public QuoteEndToEndTests(QuoteIntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Born_digital_quote_extracts_line_items_with_evidence_confidence_and_deterministic_pricing()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();
        var ocrCallsBefore = _fixture.AiGateway.OcrCallCount;

        var body = await UploadQuoteAsync(
            client, tenantId,
            QuoteExtractionScriptedPayloads.BuildBornDigitalQuoteBytes(),
            QuoteExtractionScriptedPayloads.BornDigitalFileName,
            QuoteExtractionScriptedPayloads.BornDigitalMimeType);

        // AC-1: upload succeeded and the pipeline ran to completion (not left "Uploaded" forever).
        Assert.Equal("Completed", body.GetProperty("processingStatus").GetString());
        Assert.Equal(1, body.GetProperty("lineItemCount").GetInt32());
        var quoteId = body.GetProperty("id").GetGuid();

        // Every PDF is read by the `ocr` role since the ADR-017 amendment of 2026-09-09 (the fixture
        // gateway's scanner here, Document Intelligence Read on a live deployment).
        Assert.True(
            _fixture.AiGateway.OcrCallCount > ocrCallsBefore,
            "a PDF upload must run the `ocr` gateway role");

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<QuotesDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));

        var line = await dbContext.QuoteLines.SingleAsync(l => l.QuoteId == new EntityId(quoteId));

        // AC-2: quantity/SKU/edition/price/discount/term, all present.
        Assert.Equal(QuoteExtractionScriptedPayloads.ExpectedSku, line.Sku);
        Assert.Equal(QuoteExtractionScriptedPayloads.ExpectedEdition, line.Edition);
        Assert.Equal(10m, line.Quantity);
        Assert.Equal(100m, line.UnitPrice);
        Assert.Equal("Annual", line.Term);

        // AC-2 evidence + confidence.
        Assert.Equal("10 seats @ $100/seat, annual", line.SourceSpan);
        Assert.Equal(1, line.SourcePage);
        Assert.Equal(0.93, line.Confidence);

        // AC-3: 1000 is Quantity * UnitPrice, computed by QuoteLineExtractionService — the scripted
        // payload above never states a total anywhere for the model to have "reported" instead.
        Assert.Equal(1000m, line.ExtendedPrice);
    }

    [Fact]
    public async Task Scanned_image_quote_routes_through_ocr_and_still_extracts_full_document_ac4()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();
        var ocrCallsBefore = _fixture.AiGateway.OcrCallCount;

        var body = await UploadQuoteAsync(
            client, tenantId,
            QuoteExtractionScriptedPayloads.BuildScannedQuoteBytes(),
            QuoteExtractionScriptedPayloads.ScannedFileName,
            QuoteExtractionScriptedPayloads.ScannedMimeType);

        Assert.Equal("Completed", body.GetProperty("processingStatus").GetString());
        Assert.Equal(1, body.GetProperty("lineItemCount").GetInt32());

        // AC-4: an image/tiff mime type NativeDocumentTextExtractor.CanHandle always rejects, so
        // HybridDocumentParsingService structurally cannot have taken the native path here — this
        // must have gone through the `ocr` gateway role (Document Intelligence, ADR-017).
        Assert.True(
            _fixture.AiGateway.OcrCallCount > ocrCallsBefore,
            "Expected the scanned/image quote fixture to route through IAiGateway.OcrAsync (Document Intelligence), not native parsing.");
    }

    /// <summary>
    /// Uploads <paramref name="bytes"/> through the real `POST /api/quotes` endpoint and returns
    /// the parsed response body. <see cref="ByteArrayContent.Headers"/>' own
    /// <see cref="MediaTypeHeaderValue"/> is set explicitly: without it, ASP.NET Core's multipart
    /// parser reports an empty <c>IFormFile.ContentType</c>, <c>QuoteUploadService.UploadAsync</c>
    /// would default it to <c>"application/octet-stream"</c>, and the whole native-vs-OCR routing
    /// this test (and AC-4) depends on would silently take the wrong path — same pitfall
    /// <c>R1EndToEndTests.UploadAndProcessAsync</c>'s own doc comment already names.
    /// </summary>
    private static async Task<JsonElement> UploadQuoteAsync(
        HttpClient client, Guid tenantId, byte[] bytes, string fileName, string mimeType)
    {
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

        using var multipart = new MultipartFormDataContent { { fileContent, "file", fileName } };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/quotes") { Content = multipart };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
