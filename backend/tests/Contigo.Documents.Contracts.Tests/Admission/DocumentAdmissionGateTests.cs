using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Contigo.AiGateway;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Contracts;
using Contigo.AiGateway.Fixtures;
using Contigo.Documents.Contracts.Application.Admission;
using Contigo.Documents.Contracts.Application.Extraction;
using Contigo.Documents.Contracts.Domain;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Tenancy;

namespace Contigo.Documents.Contracts.Tests.Admission;

/// <summary>
/// Task E13/F04/US01/T01 (documents-admission): <see cref="DocumentAdmissionGate"/> decisions per
/// type / threshold / readable-text floor, the single <c>document.rejected</c> audit row, and the
/// "classified once" hand-off (<c>inputs/requirements.md</c> R-DOC-03 AC-1/AC-2/AC-3/AC-6).
/// Runs against the real <see cref="HybridDocumentParsingService"/> +
/// <see cref="NativeDocumentTextExtractor"/> and the real <see cref="FixtureAiGateway"/> — the
/// same components the API host wires — so "recipe → Other → rejected" and "MASTER SERVICES
/// AGREEMENT → Msa → admitted" are proven on the production path, not on a stub of it.
/// </summary>
public sealed class DocumentAdmissionGateTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Tenant = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private const string Actor = "procurement@acme.example";

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

    [Fact]
    public async Task Msa_pdf_is_admitted_with_the_classification_carried_for_reuse()
    {
        var harness = Harness.WithFixtureGateway();

        var decision = await harness.Gate.EvaluateAsync(
            Tenant, Actor, "msa.pdf", "application/pdf", BuildPdf(MsaText));

        Assert.Equal(AdmissionOutcome.Admitted, decision.Outcome);
        Assert.True(decision.IsAdmitted);
        Assert.Equal(ContractDocumentType.Msa, decision.DetectedType);
        Assert.Equal(0.99, decision.Confidence);
        Assert.Null(decision.Reason);
        Assert.Single(decision.Pages);
        Assert.True(decision.ReadableChars >= 200);
        Assert.NotNull(decision.Classification);
        Assert.Equal(ContractDocumentType.Msa, decision.Classification!.DocumentType);
        Assert.False(string.IsNullOrWhiteSpace(decision.Classification.Metadata.ModelId));
        Assert.Equal(1, harness.Gateway.ClassifyCalls);
        Assert.Empty(harness.Audit.Entries);
    }

    [Fact]
    public async Task Recipe_pdf_is_rejected_as_not_a_contract_with_one_content_free_audit_row()
    {
        var harness = Harness.WithFixtureGateway();
        var bytes = BuildPdf(RecipeText);

        var decision = await harness.Gate.EvaluateAsync(Tenant, Actor, "carbonara.pdf", "application/pdf", bytes);

        Assert.Equal(AdmissionOutcome.Rejected, decision.Outcome);
        Assert.Equal(AdmissionRejectionReason.NotAContract, decision.Reason);
        Assert.Equal("not_a_contract", decision.Reason!.Value.ToApiValue());
        Assert.Equal(ContractDocumentType.Other, decision.DetectedType);
        Assert.Equal(0.5, decision.Confidence);
        Assert.Null(decision.Classification);

        var audit = Assert.Single(harness.Audit.Entries);
        Assert.Equal(Tenant, audit.TenantId);
        Assert.Equal(Actor, audit.Actor);
        Assert.Equal(DocumentAdmissionGate.RejectedAuditAction, audit.Action);
        Assert.Equal(DocumentAdmissionGate.RejectedAuditResourceType, audit.ResourceType);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)), audit.ResourceId);
        Assert.Equal(Now, audit.Timestamp);
        Assert.NotNull(audit.Detail);
        using var detail = JsonDocument.Parse(audit.Detail!);
        Assert.Equal("Other", detail.RootElement.GetProperty("detectedType").GetString());
        Assert.Equal(0.5, detail.RootElement.GetProperty("confidence").GetDouble());
        Assert.Equal("not_a_contract", detail.RootElement.GetProperty("reason").GetString());
        Assert.Equal("application/pdf", detail.RootElement.GetProperty("mimeType").GetString());
        Assert.Equal(bytes.Length, detail.RootElement.GetProperty("bytes").GetInt64());
        Assert.DoesNotContain("carbonara", audit.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("guanciale", audit.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Too_little_readable_text_is_rejected_without_calling_the_classify_role()
    {
        var harness = Harness.WithFixtureGateway();
        // Enough per-page text for the native extractor to trust the page (>= 40 chars), far
        // below the 200-character admission floor.
        var bytes = BuildPdf("A short note that is not really a document at all.");

        var decision = await harness.Gate.EvaluateAsync(Tenant, Actor, "note.pdf", "application/pdf", bytes);

        Assert.Equal(AdmissionOutcome.Rejected, decision.Outcome);
        Assert.Equal(AdmissionRejectionReason.NoReadableText, decision.Reason);
        Assert.Equal("no_readable_text", decision.Reason!.Value.ToApiValue());
        Assert.Equal(ContractDocumentType.Other, decision.DetectedType);
        Assert.Equal(0, decision.Confidence);
        Assert.True(decision.ReadableChars < 200);
        Assert.Equal(0, harness.Gateway.ClassifyCalls);

        var audit = Assert.Single(harness.Audit.Entries);
        Assert.Equal(DocumentAdmissionGate.RejectedAuditAction, audit.Action);
        Assert.Contains("no_readable_text", audit.Detail);
    }

    [Fact]
    public async Task Photo_without_text_goes_through_ocr_and_is_rejected_for_no_readable_text()
    {
        var harness = Harness.WithFixtureGateway();
        // A real binary image: the fixture OCR cannot decode it and answers with its placeholder.
        var photo = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0xC3, 0x28, 0xFE };

        var decision = await harness.Gate.EvaluateAsync(Tenant, Actor, "nonna.jpg", "image/jpeg", photo);

        Assert.Equal(AdmissionOutcome.Rejected, decision.Outcome);
        Assert.Equal(AdmissionRejectionReason.NoReadableText, decision.Reason);
        Assert.Equal(1, harness.Gateway.OcrCalls);
        Assert.Equal(0, harness.Gateway.ClassifyCalls);
    }

    [Fact]
    public async Task Scanned_msa_image_is_admitted_through_the_ocr_path()
    {
        var harness = Harness.WithFixtureGateway();
        var scanned = FixtureScannedImage(MsaText);

        var decision = await harness.Gate.EvaluateAsync(Tenant, Actor, "scan.jpg", "image/jpeg", scanned);

        Assert.Equal(AdmissionOutcome.Admitted, decision.Outcome);
        Assert.Equal(ContractDocumentType.Msa, decision.DetectedType);
        Assert.Equal(1, harness.Gateway.OcrCalls);
        Assert.Equal(1, harness.Gateway.ClassifyCalls);
    }

    [Fact]
    public async Task Quote_is_admitted_as_its_own_type()
    {
        var harness = Harness.WithFixtureGateway();

        var decision = await harness.Gate.EvaluateAsync(
            Tenant, Actor, "quote.pdf", "application/pdf", BuildPdf(QuoteText));

        Assert.Equal(AdmissionOutcome.Admitted, decision.Outcome);
        Assert.Equal(ContractDocumentType.Quote, decision.DetectedType);
    }

    [Theory]
    [InlineData(AiDocumentType.Msa, ContractDocumentType.Msa)]
    [InlineData(AiDocumentType.OrderForm, ContractDocumentType.OrderForm)]
    [InlineData(AiDocumentType.Sow, ContractDocumentType.Sow)]
    [InlineData(AiDocumentType.Amendment, ContractDocumentType.Amendment)]
    [InlineData(AiDocumentType.Quote, ContractDocumentType.Quote)]
    [InlineData(AiDocumentType.Invoice, ContractDocumentType.Invoice)]
    [InlineData(AiDocumentType.PriceList, ContractDocumentType.PriceList)]
    [InlineData(AiDocumentType.Nda, ContractDocumentType.Nda)]
    [InlineData(AiDocumentType.Dpa, ContractDocumentType.Dpa)]
    public async Task Every_contract_related_type_is_admitted_at_the_threshold(
        AiDocumentType aiType, ContractDocumentType expected)
    {
        var harness = Harness.WithScriptedClassification(aiType, confidence: 0.6);

        var decision = await harness.Gate.EvaluateAsync(
            Tenant, Actor, "doc.pdf", "application/pdf", BuildPdf(RecipeText));

        Assert.Equal(AdmissionOutcome.Admitted, decision.Outcome);
        Assert.Equal(expected, decision.DetectedType);
        Assert.Equal(0.6, decision.Confidence);
        Assert.Empty(harness.Audit.Entries);
    }

    [Fact]
    public async Task Contract_type_below_the_threshold_is_rejected_but_still_reports_the_detected_type()
    {
        var harness = Harness.WithScriptedClassification(AiDocumentType.Msa, confidence: 0.55);

        var decision = await harness.Gate.EvaluateAsync(
            Tenant, Actor, "doc.pdf", "application/pdf", BuildPdf(MsaText));

        Assert.Equal(AdmissionOutcome.Rejected, decision.Outcome);
        Assert.Equal(AdmissionRejectionReason.NotAContract, decision.Reason);
        Assert.Equal(ContractDocumentType.Msa, decision.DetectedType);
        Assert.Equal(0.55, decision.Confidence);
        Assert.Single(harness.Audit.Entries);
    }

    [Fact]
    public async Task Threshold_and_readable_text_floor_are_configuration()
    {
        var relaxed = new DocumentAdmissionOptions { AdmissionThreshold = 0.5, MinReadableChars = 20 };
        var harness = Harness.WithScriptedClassification(AiDocumentType.Msa, confidence: 0.55, relaxed);

        var decision = await harness.Gate.EvaluateAsync(
            Tenant, Actor, "doc.pdf", "application/pdf", BuildPdf("A short note that is not really a document at all."));

        Assert.Equal(AdmissionOutcome.Admitted, decision.Outcome);
    }

    [Fact]
    public async Task Ai_type_other_is_rejected_even_at_full_confidence()
    {
        var harness = Harness.WithScriptedClassification(AiDocumentType.Other, confidence: 1.0);

        var decision = await harness.Gate.EvaluateAsync(
            Tenant, Actor, "doc.pdf", "application/pdf", BuildPdf(RecipeText));

        Assert.Equal(AdmissionOutcome.Rejected, decision.Outcome);
        Assert.Equal(AdmissionRejectionReason.NotAContract, decision.Reason);
        Assert.Equal(ContractDocumentType.Other, decision.DetectedType);
    }

    [Fact]
    public async Task Parse_failure_is_a_failed_decision_not_a_rejection()
    {
        var harness = Harness.WithScriptedGateway(new ScriptedAiGateway(
            classify: _ => Result<AiClassificationResult>.Failure("must not be called"),
            ocr: _ => Result<AiOcrResult>.Failure("OCR page budget exceeded")));

        var decision = await harness.Gate.EvaluateAsync(
            Tenant, Actor, "scan.png", "image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01 });

        Assert.Equal(AdmissionOutcome.Failed, decision.Outcome);
        Assert.False(decision.IsAdmitted);
        Assert.Null(decision.Reason);
        Assert.Contains("page budget", decision.Error);
        Assert.Empty(harness.Audit.Entries);
    }

    [Fact]
    public async Task Classify_failure_is_a_failed_decision_not_a_rejection()
    {
        var harness = Harness.WithScriptedGateway(new ScriptedAiGateway(
            classify: _ => Result<AiClassificationResult>.Failure("classify role unavailable"),
            ocr: _ => throw new InvalidOperationException("native PDF text never reaches OCR")));

        var decision = await harness.Gate.EvaluateAsync(
            Tenant, Actor, "msa.pdf", "application/pdf", BuildPdf(MsaText));

        Assert.Equal(AdmissionOutcome.Failed, decision.Outcome);
        Assert.Equal("classify role unavailable", decision.Error);
        Assert.Empty(harness.Audit.Entries);
    }

    [Fact]
    public void Readable_chars_ignore_whitespace_and_blank_pages()
    {
        var pages = new List<DocumentPageText>
        {
            new(1, "  ab c \n\t d "),
            new(2, ""),
            new(3, "   "),
            new(4, "efg"),
        };

        Assert.Equal(7, DocumentAdmissionGate.CountReadableChars(pages));
        Assert.Equal(0, DocumentAdmissionGate.CountReadableChars([]));
    }

    /// <summary>The same hand-built PDF shape <c>R1ExtractionFixtures.BuildBornDigitalPdfBytes</c> and
    /// <c>NativeDocumentTextExtractorTests</c> use — one page object, one text-bearing content stream.</summary>
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

    /// <summary>A fixture "scanned image": JPEG signature + UTF-8 page text, the format
    /// <see cref="FixtureAiGateway"/>'s OCR decodes (see its <c>DecodePages</c>).</summary>
    private static byte[] FixtureScannedImage(string text) =>
        [0xFF, 0xD8, 0xFF, .. Encoding.UTF8.GetBytes(text)];

    private sealed class Harness
    {
        public required DocumentAdmissionGate Gate { get; init; }
        public required CountingAiGateway Gateway { get; init; }
        public required RecordingAuditWriter Audit { get; init; }

        public static Harness WithFixtureGateway(DocumentAdmissionOptions? options = null) =>
            Build(new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now), new AiGatewayOcrOptions()), options);

        public static Harness WithScriptedClassification(
            AiDocumentType type, double confidence, DocumentAdmissionOptions? options = null) =>
            WithScriptedGateway(
                new ScriptedAiGateway(
                    classify: _ => Result<AiClassificationResult>.Success(new AiClassificationResult(
                        type, confidence, new AiCallMetadata("scripted-classify", "1", "p1", Now, "hash"))),
                    ocr: _ => throw new InvalidOperationException("native PDF text never reaches OCR")),
                options);

        public static Harness WithScriptedGateway(IAiGateway gateway, DocumentAdmissionOptions? options = null) =>
            Build(gateway, options);

        private static Harness Build(IAiGateway inner, DocumentAdmissionOptions? options)
        {
            var gateway = new CountingAiGateway(inner);
            var audit = new RecordingAuditWriter();
            var parsing = new HybridDocumentParsingService(gateway, new NativeDocumentTextExtractor());
            var gate = new DocumentAdmissionGate(
                parsing, gateway, options ?? new DocumentAdmissionOptions(), new TenantContext(), audit, new FixedClock(Now));
            return new Harness { Gate = gate, Gateway = gateway, Audit = audit };
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEntry> Entries { get; } = [];

        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class CountingAiGateway(IAiGateway inner) : IAiGateway
    {
        public int ClassifyCalls { get; private set; }
        public int OcrCalls { get; private set; }

        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default)
        {
            ClassifyCalls++;
            return inner.ClassifyAsync(request, cancellationToken);
        }

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            inner.ExtractAsync(request, cancellationToken);

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            inner.EmbedAsync(request, cancellationToken);

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            inner.AnswerAsync(request, cancellationToken);

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default)
        {
            OcrCalls++;
            return inner.OcrAsync(request, cancellationToken);
        }
    }

    private sealed class ScriptedAiGateway(
        Func<AiClassificationRequest, Result<AiClassificationResult>> classify,
        Func<AiOcrRequest, Result<AiOcrResult>> ocr) : IAiGateway
    {
        public Task<Result<AiClassificationResult>> ClassifyAsync(
            AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(classify(request));

        public Task<Result<AiExtractionResult>> ExtractAsync(
            AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The admission gate never extracts.");

        public Task<Result<AiEmbeddingResult>> EmbedAsync(
            AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The admission gate never embeds.");

        public Task<Result<AiAnswerResult>> AnswerAsync(
            AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The admission gate never answers.");

        public Task<Result<AiOcrResult>> OcrAsync(
            AiOcrRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(ocr(request));
    }
}
