using System.Text;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Proves the Definition of Done for task E02/F01/US02/T02 (hybrid-ocr): native text is used
/// directly (and the `ocr` gateway role is never billed) when the native extractor trusts its own
/// result; anything else — insufficient native text, an unrecognized mime type, empty content —
/// routes through the full-document `ocr` role, honouring ADR-017's "no 2-page cap" and page
/// budget. Since the ADR-017 amendment of 2026-09-09 the native half handles DOCX/XLSX only, so
/// every PDF takes the `ocr` route.
///
/// Uses the real <see cref="FixtureAiGateway"/> (already proven by
/// <c>FixtureAiGatewayOcrTests</c>) as <see cref="IAiGateway"/> whenever a test actually wants OCR
/// to run — only the native-extraction side needs a scripted fake, mirroring how
/// <c>StagedExtractionServiceTests</c> scripts <c>IAiGateway</c> rather than depending on a live
/// model.
/// </summary>
public sealed class HybridDocumentParsingServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    /// <summary>Scripted <see cref="INativeDocumentTextExtractor"/>. <see cref="Extract"/> throws
    /// unless a result was scripted, so a test proving "native extraction is skipped" fails loudly
    /// if <see cref="HybridDocumentParsingService"/> ever calls it anyway.</summary>
    private sealed class ScriptedNativeTextExtractor(bool canHandle, NativeTextExtractionResult? extractResult = null)
        : INativeDocumentTextExtractor
    {
        public bool CanHandle(string mimeType) => canHandle;

        public NativeTextExtractionResult Extract(string mimeType, ReadOnlyMemory<byte> content) =>
            extractResult ?? throw new InvalidOperationException(
                "Extract must not be called for this test scenario (CanHandle is false, or no result was scripted).");
    }

    /// <summary>Minimal <see cref="IAiGateway"/> fake used only to prove the gateway is (or is
    /// not) reached and, when it is, to script its response — mirrors
    /// <c>StagedExtractionServiceTests.ScriptedAiGateway</c>'s "throw for every method this
    /// service does not call" shape.</summary>
    private sealed class OcrOnlyAiGateway(Func<AiOcrRequest, Result<AiOcrResult>>? onOcr = null) : IAiGateway
    {
        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("HybridDocumentParsingService does not call ClassifyAsync.");

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("HybridDocumentParsingService does not call ExtractAsync.");

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("HybridDocumentParsingService does not call EmbedAsync.");

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("HybridDocumentParsingService does not call AnswerAsync.");

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(onOcr is not null
                ? onOcr(request)
                : throw new InvalidOperationException(
                    "OcrAsync must not be called for this test scenario (native text was sufficient)."));
    }

    [Fact]
    public async Task Sufficient_native_text_is_used_directly_and_the_gateway_is_never_called()
    {
        var nativePages = new List<DocumentPageText> { new(1, "Native contract text.") };
        var extractor = new ScriptedNativeTextExtractor(
            canHandle: true, new NativeTextExtractionResult(nativePages, IsSufficient: true));
        var service = new HybridDocumentParsingService(new OcrOnlyAiGateway(), extractor);

        var result = await service.ParseAsync("contract.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "irrelevant"u8.ToArray());

        Assert.True(result.IsSuccess);
        var page = Assert.Single(result.Value);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal("Native contract text.", page.Text);
    }

    [Fact]
    public async Task A_pdf_goes_to_the_ocr_role_even_when_it_is_born_digital()
    {
        // ADR-017 amendment (2026-09-09): the real native extractor handles DOCX/XLSX only, so a
        // PDF — born-digital or scanned — is read by the `ocr` role; the fixture gateway's own
        // scanner stands in for Document Intelligence here.
        var gateway = new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now));
        var service = new HybridDocumentParsingService(gateway, new NativeDocumentTextExtractor());
        var pdf =
            "%PDF-1.4\n" +
            "1 0 obj << /Type /Page >> endobj\n" +
            "2 0 obj << /Length 0 >>\n" +
            "stream\n" +
            "BT (MASTER SERVICES AGREEMENT between Acme Corp and Contoso Ltd.) Tj ET\n" +
            "endstream\n" +
            "endobj\n" +
            "%%EOF\n";

        var result = await service.ParseAsync("contract.pdf", "application/pdf", Encoding.Latin1.GetBytes(pdf));

        Assert.True(result.IsSuccess);
        var page = Assert.Single(result.Value);
        Assert.Equal(1, page.PageNumber);
        Assert.Equal("MASTER SERVICES AGREEMENT between Acme Corp and Contoso Ltd.", page.Text);
    }

    [Fact]
    public async Task Insufficient_native_text_falls_back_to_the_full_document_ocr_role()
    {
        var extractor = new ScriptedNativeTextExtractor(
            canHandle: true, new NativeTextExtractionResult([], IsSufficient: false));
        var gateway = new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now));
        var service = new HybridDocumentParsingService(gateway, extractor);

        var content = Encoding.UTF8.GetBytes("page one\fpage two");
        var result = await service.ParseAsync("scanned.pdf", "application/pdf", content);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Count);
        Assert.Equal("page one", result.Value[0].Text);
        Assert.Equal("page two", result.Value[1].Text);
    }

    [Fact]
    public async Task Unrecognized_mime_type_skips_native_extraction_and_goes_straight_to_ocr()
    {
        // CanHandle=false and no scripted Extract result: if HybridDocumentParsingService called
        // Extract anyway, this fake would throw and fail the test.
        var extractor = new ScriptedNativeTextExtractor(canHandle: false);
        var gateway = new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now));
        var service = new HybridDocumentParsingService(gateway, extractor);

        var content = Encoding.UTF8.GetBytes("scanned page text");
        var result = await service.ParseAsync("photo.png", "image/png", content);

        Assert.True(result.IsSuccess);
        var page = Assert.Single(result.Value);
        Assert.Equal("scanned page text", page.Text);
    }

    [Fact]
    public async Task Ocr_page_budget_exceeded_fails_the_parse_instead_of_truncating()
    {
        var extractor = new ScriptedNativeTextExtractor(canHandle: false);
        var gateway = new FixtureAiGateway(
            new AiGatewayModelOptions(), new FixedClock(Now), new AiGatewayOcrOptions { MaxPagesPerDocument = 1 });
        var service = new HybridDocumentParsingService(gateway, extractor);

        var content = Encoding.UTF8.GetBytes("page one\fpage two\fpage three");
        var result = await service.ParseAsync("huge.pdf", "application/pdf", content);

        Assert.True(result.IsFailure);
        Assert.Contains("budget", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ocr_failure_is_propagated_as_a_parse_failure()
    {
        var extractor = new ScriptedNativeTextExtractor(canHandle: false);
        var gateway = new OcrOnlyAiGateway(onOcr: _ => Result<AiOcrResult>.Failure("simulated OCR provider failure"));
        var service = new HybridDocumentParsingService(gateway, extractor);

        var result = await service.ParseAsync("broken.pdf", "application/pdf", "bytes"u8.ToArray());

        Assert.True(result.IsFailure);
        Assert.Equal("simulated OCR provider failure", result.Error);
    }

    [Fact]
    public async Task Ocr_producing_zero_pages_is_a_failure_not_an_empty_success()
    {
        var extractor = new ScriptedNativeTextExtractor(canHandle: false);
        var metadata = new AiCallMetadata("test-model", "1", "test-v1", Now, "hash");
        var gateway = new OcrOnlyAiGateway(onOcr: _ => Result<AiOcrResult>.Success(new AiOcrResult([], metadata)));
        var service = new HybridDocumentParsingService(gateway, extractor);

        var result = await service.ParseAsync("empty-scan.pdf", "application/pdf", "bytes"u8.ToArray());

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task Empty_content_fails_fast_before_touching_native_extractor_or_gateway()
    {
        // Both fakes throw if reached at all — proves the empty-content check runs first.
        var extractor = new ScriptedNativeTextExtractor(canHandle: false);
        var gateway = new OcrOnlyAiGateway();
        var service = new HybridDocumentParsingService(gateway, extractor);

        var result = await service.ParseAsync("empty.pdf", "application/pdf", ReadOnlyMemory<byte>.Empty);

        Assert.True(result.IsFailure);
    }
}
