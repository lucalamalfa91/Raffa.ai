using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E02/F06/US01/T01 (r1-integration) and its parent story
/// us-01-final-integration: AC-1 ("Upload -&gt; parse/OCR -&gt; classify -&gt; extract -&gt;
/// portfolio -&gt; 360 -&gt; Ask Raffa (with citations) works end-to-end"), AC-2 ("Low-confidence
/// field correction preserves original extraction + history") and AC-4 ("At least one scanned or
/// image-based contract extracts via Document Intelligence, full document, ADR-017") — driven
/// entirely over real HTTP through the real <c>Raffa.Api</c> composition root (including the
/// <see cref="Raffa.Documents.Contracts.Application.Extraction.DocumentProcessingPipeline"/> this
/// task adds), against a real, migrated Postgres+pgvector+RLS database
/// (see <see cref="R1IntegrationFixture"/>) — the same "one real host, no hand-rolled container"
/// shape <c>R0EndToEndTests</c> already established for R0. AC-3 (cross-tenant isolation) is proved
/// separately by <see cref="R1CrossTenantIsolationTests"/>, mirroring how R0 split
/// <c>R0EndToEndTests</c>/<c>R0CrossTenantIsolationTests</c>.
///
/// Reads response bodies as raw <see cref="JsonElement"/>s rather than typed DTOs — same reason as
/// <c>R0EndToEndTests</c>'s own doc comment: every endpoint under test serializes anonymous objects
/// with ASP.NET Core's default camelCase policy, which a hand-written strongly-typed record would
/// have to duplicate exactly to deserialize correctly.
/// </summary>
public sealed class R1EndToEndTests : IClassFixture<R1IntegrationFixture>
{
    private readonly R1IntegrationFixture _fixture;

    public R1EndToEndTests(R1IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Full_r1_path_upload_to_extract_to_portfolio_to_360_to_ask_raffa_to_correction()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();

        // 1. Upload + process the born-digital fixture (AC-1 "upload -> parse/OCR -> classify ->
        //    extract"; a PDF is read by the `ocr` role, here the fixture gateway's scanner — see
        //    R1ExtractionFixtures.BuildBornDigitalPdfBytes's own doc comment).
        var (documentId, contractId) = await UploadAndProcessAsync(
            client, tenantId, R1ExtractionFixtures.BuildBornDigitalPdfBytes(),
            R1ExtractionFixtures.BornDigitalFileName, R1ExtractionFixtures.BornDigitalMimeType);

        // 2. GET /api/documents/{id}: classification and the document->contract link really
        //    happened, not just that the upload itself succeeded.
        var documentResponse = await GetAsync(client, $"/api/documents/{documentId}", tenantId);
        Assert.Equal(HttpStatusCode.OK, documentResponse.StatusCode);
        var documentBody = await ParseAsync(documentResponse);
        Assert.Equal("Msa", documentBody.GetProperty("documentType").GetString());
        Assert.Equal(contractId.ToString(), documentBody.GetProperty("contractId").GetString());
        // CommercialTerms' own annualSpend fact is deliberately low-confidence (see
        // R1ExtractionFixtures.PayloadsByStage) so the document lands in NeedsReview, not
        // Completed — a real signal AC-2's correction step below responds to, not a fabricated one.
        Assert.Equal("NeedsReview", documentBody.GetProperty("processingStatus").GetString());

        // 3. Portfolio: the newly-extracted contract is listed (AC-1 "portfolio").
        var portfolioResponse = await GetAsync(client, "/api/contracts", tenantId);
        Assert.Equal(HttpStatusCode.OK, portfolioResponse.StatusCode);
        var portfolioBody = await ParseAsync(portfolioResponse);
        var portfolioRow = Assert.Single(
            portfolioBody.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("contractId").GetString() == contractId.ToString());

        // Task E13/F03/US01/T02 (parent story us-01-supplier-identity AC-3/AC-4, requirements
        // R-SUP-01/R-SUP-02/R-SUP-04): the whole chain — `supplier` critical fact -> resolver ->
        // Contract.SupplierId -> ISupplierNameLookup -> the portfolio row — ran end-to-end through
        // the real composition root, so the user sees a supplier *name*, never a bare guid.
        Assert.Equal(JsonValueKind.String, portfolioRow.GetProperty("supplierId").ValueKind);
        Assert.Equal(
            R1ExtractionFixtures.ExpectedSupplierDisplayName,
            portfolioRow.GetProperty("supplierName").GetString());

        // 4. Contract 360: header + every extraction-derived tab (AC-1 "360").
        var contract360Response = await GetAsync(client, $"/api/contracts/{contractId}", tenantId);
        Assert.Equal(HttpStatusCode.OK, contract360Response.StatusCode);
        var contract360Body = await ParseAsync(contract360Response);
        Assert.Equal("Msa", contract360Body.GetProperty("header").GetProperty("type").GetString());
        Assert.Equal(
            R1ExtractionFixtures.ExpectedSupplierDisplayName,
            contract360Body.GetProperty("header").GetProperty("supplierName").GetString());
        Assert.Equal(
            decimal.Parse(R1ExtractionFixtures.OriginalAnnualSpend),
            contract360Body.GetProperty("tabs").GetProperty("commercials").GetProperty("annualSpend").GetDecimal());
        Assert.Contains(
            contract360Body.GetProperty("tabs").GetProperty("products").EnumerateArray(),
            p => p.GetProperty("sku").GetString() == R1ExtractionFixtures.ExpectedLineItemSku);
        Assert.Contains(
            contract360Body.GetProperty("tabs").GetProperty("clauses").EnumerateArray(),
            c => c.GetProperty("clauseType").GetString() == R1ExtractionFixtures.ExpectedClauseType);
        Assert.Contains(
            contract360Body.GetProperty("tabs").GetProperty("obligations").EnumerateArray(),
            o => o.GetProperty("party").GetString() == R1ExtractionFixtures.ExpectedObligationParty);
        Assert.Contains(
            contract360Body.GetProperty("tabs").GetProperty("risks").EnumerateArray(),
            r => r.GetProperty("riskType").GetString() == R1ExtractionFixtures.ExpectedRiskType);
        Assert.Contains(
            contract360Body.GetProperty("tabs").GetProperty("documents").EnumerateArray(),
            d => d.GetProperty("documentId").GetString() == documentId.ToString());

        // 5. Ask Raffa: a semantic question gets a grounded answer with a citation pointing back
        //    at this document (AC-1 "Ask Raffa (with citations)"; spec §8.3/§8.4). Task
        //    E13/F06/US01/T01 (ask-engine) replaced the old `{ intent, canDetermine, citations:
        //    [{documentId}] }` shape with the ADR-024 §6 reply contract (`kind`/`answerMarkdown`/
        //    `citations[]` — see ChatEndpointTests' own doc comment on the supersession) and made
        //    `POST /api/chat/query` resolve caller identity, so this call now needs an X-User-Id
        //    header too. `citations[].documentId` now echoes the pack's own citationKey
        //    (`Application.Pack.PackItem.CitationKey`, `AskCopilotService.BuildClausePackAsync`'s
        //    own `fact:{sourceId}:chunk[{index}]` shape for a clause hit), not a bare
        //    `Document:{id}` — still traceable back to this document by substring, same convention
        //    `AskRaffaRagCrossTenantIsolationTests` already uses for the identical new shape.
        var chatResponse = await PostAsync(
            client, "/api/chat/query", tenantId, "alice@example.com",
            new { question = "What does the master services agreement cover?" });
        Assert.Equal(HttpStatusCode.OK, chatResponse.StatusCode);
        var chatBody = await ParseAsync(chatResponse);
        Assert.Equal("answer", chatBody.GetProperty("kind").GetString());
        var citations = chatBody.GetProperty("citations").EnumerateArray().ToList();
        Assert.NotEmpty(citations);
        Assert.Contains(citations, c => c.GetProperty("documentId").GetString()!.Contains(documentId.ToString()));

        // 6. Correction: PATCH the low-confidence annualSpend field (AC-2).
        var correctResponse = await PatchAsync(
            client, $"/api/contracts/{contractId}", tenantId,
            new
            {
                corrections = new Dictionary<string, string?> { ["annualSpend"] = R1ExtractionFixtures.CorrectedAnnualSpend },
                reason = "Verified against the signed order form.",
            });
        Assert.Equal(HttpStatusCode.OK, correctResponse.StatusCode);
        var correctBody = await ParseAsync(correctResponse);
        Assert.Contains(
            correctBody.GetProperty("correctedFields").EnumerateArray(),
            f => f.GetString() == "annualSpend");

        // AC-2 "... + history": the correction is queryable, and it recorded the *original*
        // extracted value as previousValue, not just the new one.
        var historyResponse = await GetAsync(client, $"/api/contracts/{contractId}/corrections", tenantId);
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var historyBody = await ParseAsync(historyResponse);
        var annualSpendHistoryEntry = Assert.Single(
            historyBody.EnumerateArray(), e => e.GetProperty("fieldName").GetString() == "annualSpend");
        Assert.Equal(R1ExtractionFixtures.OriginalAnnualSpendAsStoredInDb, annualSpendHistoryEntry.GetProperty("previousValue").GetString());
        Assert.Equal(R1ExtractionFixtures.CorrectedAnnualSpend, annualSpendHistoryEntry.GetProperty("newValue").GetString());

        // 360 again: the correction is now the value Contract 360 reports.
        var contract360AfterResponse = await GetAsync(client, $"/api/contracts/{contractId}", tenantId);
        var contract360AfterBody = await ParseAsync(contract360AfterResponse);
        Assert.Equal(
            decimal.Parse(R1ExtractionFixtures.CorrectedAnnualSpend),
            contract360AfterBody.GetProperty("tabs").GetProperty("commercials").GetProperty("annualSpend").GetDecimal());

        // AC-2 "preserves original extraction": the ExtractionEvidence row the staged pipeline
        // wrote before any human correction still carries the original value and confidence,
        // untouched by ContractCorrectionService (Appendix C rule 5 — never destructively
        // overwrite contract history or human corrections; this is the extraction-time half of
        // that rule, not the correction-history half GET .../corrections already proved above).
        using (var scope = _fixture.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));

            var evidence = await dbContext.ExtractionEvidences.SingleAsync(
                e => e.ContractId == new EntityId(contractId) && e.FieldName == "annualSpend");
            Assert.Equal(R1ExtractionFixtures.OriginalAnnualSpend, evidence.Value);
            Assert.Equal(0.35, evidence.Confidence);
        }

        // 6b. Review sign-off: with the weak annualSpend corrected, the reviewer marks the document
        //     validated (POST /api/documents/{id}/validate) — the one write that moves it from
        //     needs_review to completed, so Ask/Portfolio/Renewals start reading it. Before this
        //     endpoint existed the screen's "Mark as validated" was a client-side navigation only.
        var validateResponse = await PostAsync(
            client, $"/api/documents/{documentId}/validate", tenantId,
            new { acceptedFields = new[] { "currency", "autoRenewal" } });
        Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);
        var validateBody = await ParseAsync(validateResponse);
        Assert.Equal("Completed", validateBody.GetProperty("processingStatus").GetString());
        Assert.False(validateBody.GetProperty("alreadyValidated").GetBoolean());

        var validatedDocumentResponse = await GetAsync(client, $"/api/documents/{documentId}", tenantId);
        Assert.Equal(
            "Completed",
            (await ParseAsync(validatedDocumentResponse)).GetProperty("processingStatus").GetString());

        // The per-field evidence behind the review is readable too: the original 0.35 annualSpend
        // proposal, untouched by the correction above (Appendix C rule 5), with its page and span.
        var evidenceResponse = await GetAsync(client, $"/api/contracts/{contractId}/evidence", tenantId);
        Assert.Equal(HttpStatusCode.OK, evidenceResponse.StatusCode);
        var annualSpendEvidence = Assert.Single(
            (await ParseAsync(evidenceResponse)).EnumerateArray(),
            e => e.GetProperty("fieldName").GetString() == "annualSpend");
        Assert.Equal(R1ExtractionFixtures.OriginalAnnualSpend, annualSpendEvidence.GetProperty("value").GetString());
        Assert.Equal(0.35, annualSpendEvidence.GetProperty("confidence").GetDouble());
        Assert.Equal(R1ExtractionFixtures.BornDigitalFileName, annualSpendEvidence.GetProperty("sourceFileName").GetString());

        // 7. Scanned/image fixture (AC-4): routes through the `ocr` gateway role (Document
        //    Intelligence), not native parsing, and still extracts end-to-end.
        var ocrCallsBefore = _fixture.AiGateway.OcrCallCount;
        var (scannedDocumentId, scannedContractId) = await UploadAndProcessAsync(
            client, tenantId, R1ExtractionFixtures.BuildScannedImageOcrBytes(),
            R1ExtractionFixtures.ScannedFileName, R1ExtractionFixtures.ScannedMimeType);
        Assert.True(
            _fixture.AiGateway.OcrCallCount > ocrCallsBefore,
            "Expected the scanned/image fixture to route through IAiGateway.OcrAsync (Document Intelligence), not native parsing.");

        var scannedDocumentResponse = await GetAsync(client, $"/api/documents/{scannedDocumentId}", tenantId);
        Assert.Equal(HttpStatusCode.OK, scannedDocumentResponse.StatusCode);
        var scannedDocumentBody = await ParseAsync(scannedDocumentResponse);
        Assert.Equal("Msa", scannedDocumentBody.GetProperty("documentType").GetString());
        Assert.Equal(scannedContractId.ToString(), scannedDocumentBody.GetProperty("contractId").GetString());

        // "full document" (ADR-017, no 2-page cap): both OCR pages were indexed, not just one.
        using (var scope = _fixture.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));

            var chunkCount = await dbContext.Embeddings.CountAsync(
                e => e.SourceType == "Document" && e.SourceId == new EntityId(scannedDocumentId));
            Assert.Equal(2, chunkCount);
        }
    }

    /// <summary>
    /// Task E13/F03/US01/T02 (requirements R-SUP-03, "a one-off job resolves suppliers for existing
    /// contracts"): re-processing a contract that carries no <c>SupplierId</c> back-fills it, so the
    /// existing corpus picks supplier identity up without a bespoke migration job. The "existing
    /// contract" is built by uploading normally and then clearing the link straight in the database
    /// — the resulting row is byte-for-byte what a contract extracted before this feature existed
    /// looks like, and there is no HTTP surface that could produce one (nothing un-links a
    /// supplier).
    ///
    /// <para>
    /// Re-processing itself goes through the real <c>DocumentProcessingPipeline</c> resolved from
    /// this host's own container — the same "resolve the real service from the host's service
    /// provider, skip HTTP" shape <see cref="Full_r1_path_upload_to_extract_to_portfolio_to_360_to_ask_raffa_to_correction"/>
    /// already uses to read <c>ExtractionEvidence</c> back. The reprocess *endpoint* is a sibling
    /// task's surface (E13/F04/US01/T02); this test deliberately asserts the pipeline behaviour that
    /// endpoint drives, not the endpoint itself, so the two do not have to land together.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Reprocessing_a_contract_stored_without_a_supplier_back_fills_the_link()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();

        var (documentId, contractId) = await UploadAndProcessAsync(
            client, tenantId, R1ExtractionFixtures.BuildBornDigitalPdfBytes(),
            R1ExtractionFixtures.BornDigitalFileName, R1ExtractionFixtures.BornDigitalMimeType);

        // Rewind to "before supplier identity existed".
        using (var scope = _fixture.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));

            var contract = await dbContext.Contracts.SingleAsync(c => c.Id == new EntityId(contractId));
            Assert.NotNull(contract.SupplierId);

            contract.SupplierId = null;
            await dbContext.SaveChangesAsync();
        }

        var beforeResponse = await GetAsync(client, $"/api/contracts/{contractId}", tenantId);
        var beforeHeader = (await ParseAsync(beforeResponse)).GetProperty("header");
        Assert.Equal(JsonValueKind.Null, beforeHeader.GetProperty("supplierId").ValueKind);
        Assert.Equal(JsonValueKind.Null, beforeHeader.GetProperty("supplierName").ValueKind);

        using (var scope = _fixture.Services.CreateScope())
        {
            var pipeline = scope.ServiceProvider.GetRequiredService<DocumentProcessingPipeline>();

            var reprocessed = await pipeline.ProcessAsync(
                new TenantId(tenantId),
                new EntityId(documentId),
                R1ExtractionFixtures.BornDigitalFileName,
                R1ExtractionFixtures.BornDigitalMimeType,
                R1ExtractionFixtures.BuildBornDigitalPdfBytes());

            Assert.True(reprocessed.IsSuccess);

            // Re-processing stages into the same contract the first pass created, so the back-fill
            // lands on the existing row rather than orphaning it behind a second one.
            Assert.Equal(new EntityId(contractId), reprocessed.Value.ContractId);
        }

        var afterResponse = await GetAsync(client, $"/api/contracts/{contractId}", tenantId);
        var afterHeader = (await ParseAsync(afterResponse)).GetProperty("header");
        Assert.Equal(JsonValueKind.String, afterHeader.GetProperty("supplierId").ValueKind);
        Assert.Equal(
            R1ExtractionFixtures.ExpectedSupplierDisplayName,
            afterHeader.GetProperty("supplierName").GetString());
    }

    /// <summary>
    /// Uploads <paramref name="bytes"/> through the real `POST /api/documents` endpoint (which now
    /// also runs <see cref="Raffa.Documents.Contracts.Application.Extraction.DocumentProcessingPipeline"/>
    /// synchronously — task E02/F06/US01/T01) and returns the resulting document/contract ids.
    /// <see cref="ByteArrayContent.Headers"/>' <see cref="MediaTypeHeaderValue"/> is set explicitly:
    /// without it, ASP.NET Core's multipart parser reports an empty
    /// <c>IFormFile.ContentType</c>, <c>DocumentUploadService.UploadAsync</c> would default it to
    /// <c>"application/octet-stream"</c>, and the whole native-vs-OCR routing this test (and AC-4)
    /// depends on would silently take the wrong path.
    /// </summary>
    internal static async Task<(Guid DocumentId, Guid ContractId)> UploadAndProcessAsync(
        HttpClient client, Guid tenantId, byte[] bytes, string fileName, string mimeType)
    {
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);

        using var multipart = new MultipartFormDataContent { { fileContent, "file", fileName } };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/documents") { Content = multipart };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await ParseAsync(response);
        var documentId = body.GetProperty("id").GetGuid();

        Assert.Equal(
            JsonValueKind.String, body.GetProperty("contractId").ValueKind);
        var contractId = body.GetProperty("contractId").GetGuid();

        return (documentId, contractId);
    }

    internal static async Task<HttpResponseMessage> GetAsync(HttpClient client, string url, Guid tenantId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        return await client.SendAsync(request);
    }

    internal static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, Guid tenantId, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        return await client.SendAsync(request);
    }

    /// <summary>
    /// Task E13/F06/US01/T01 (ask-engine): `POST /api/chat/query` now creates a conversation and
    /// delegates into `AskCopilotService` (see `ChatEndpointExtensions`' own doc comment), so it now
    /// resolves a caller identity the same way `POST/GET /api/conversations` already do — the
    /// required `X-User-Id` header (ADR-022 posture, OQ-askv2-005) — where the plain
    /// <see cref="PostAsync(HttpClient, string, Guid, object)"/> overload above never needed one.
    /// </summary>
    internal static async Task<HttpResponseMessage> PostAsync(
        HttpClient client, string url, Guid tenantId, string userId, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        request.Headers.Add("X-User-Id", userId);
        return await client.SendAsync(request);
    }

    internal static async Task<HttpResponseMessage> PatchAsync(HttpClient client, string url, Guid tenantId, object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        return await client.SendAsync(request);
    }

    internal static async Task<JsonElement> ParseAsync(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
