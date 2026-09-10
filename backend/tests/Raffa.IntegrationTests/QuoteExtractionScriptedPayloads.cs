using System.Text;

namespace Raffa.IntegrationTests;

/// <summary>
/// Shared fixture data for task E05/F01/US01/T01 (quote-extraction): a hand-built, minimal-but-real
/// born-digital PDF (read by the `ocr` gateway role like every PDF since the ADR-017 amendment of
/// 2026-09-09) and a scanned/image-style quote (proves AC-4's "scanned/image quote PDFs reuse the
/// epic-02 hybrid OCR path... no 2-page cap" — an <c>image/tiff</c> mime type
/// <c>NativeDocumentTextExtractor.CanHandle</c> always returns <see langword="false"/> for, so
/// <c>HybridDocumentParsingService</c> structurally cannot take the native path), plus the scripted
/// `QuoteLineItems`-stage payload <see cref="ScriptedR1AiGateway"/> returns for both. Same
/// hand-built-PDF technique as <see cref="R1ExtractionFixtures.BuildBornDigitalPdfBytes"/> (see that
/// method's own doc comment for exactly what the lightweight native scanner does and does not
/// handle) — duplicated, not shared, only because the embedded text needs to read as a quote, not
/// an MSA.
/// </summary>
internal static class QuoteExtractionScriptedPayloads
{
    public const string BornDigitalFileName = "quote-acme-enterprise.pdf";
    public const string BornDigitalMimeType = "application/pdf";

    public const string ScannedFileName = "scanned-quote-northwind.tiff";
    public const string ScannedMimeType = "image/tiff";

    public const string ExpectedSku = "SKU-ENT-100";
    public const string ExpectedEdition = "Enterprise";

    /// <summary>
    /// Scripted `extract` payload for the `QuoteLineItems` stage (<c>AiExtractionRequest.StageName</c>
    /// — see <c>Raffa.Api.QuoteExtractionPipeline</c>'s own constant) — same shape
    /// <see cref="R1ExtractionFixtures.PayloadsByStage"/> uses for the sibling contract pipeline.
    /// Never states a total/extended price anywhere (AC-3): <c>QuoteLineExtractionService</c> must
    /// derive <c>ExtendedPrice</c> = 1000 (10 × 100) itself for this end-to-end proof to pass.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PayloadsByStage { get; } = new Dictionary<string, string>
    {
        ["QuoteLineItems"] = $$"""
            {"items":[
                {"sku":"{{ExpectedSku}}","edition":"{{ExpectedEdition}}","description":"Enterprise Suite Seats",
                 "quantity":10,"unit":"seat","unitPrice":100,"term":"Annual",
                 "sourcePage":1,"sourceSpan":"10 seats @ $100/seat, annual","confidence":0.93}
            ]}
            """,
    };

    /// <summary>Minimal, hand-built, syntactically real single-page PDF — see
    /// <see cref="R1ExtractionFixtures.BuildBornDigitalPdfBytes"/>'s own doc comment for the
    /// technique; the fixture `ocr` role reads it through <c>FixturePdfTextScanner</c>.</summary>
    public static byte[] BuildBornDigitalQuoteBytes()
    {
        const string text =
            "Supplier Quote Q-1001 for Acme Corp: Enterprise Suite, 10 seats, $100/seat, annual term.";

        var pdf = $"""
            %PDF-1.4
            1 0 obj
            << /Type /Catalog /Pages 2 0 R >>
            endobj
            2 0 obj
            << /Type /Pages /Kids [3 0 R] /Count 1 >>
            endobj
            3 0 obj
            << /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> >> /MediaBox [0 0 612 792] /Contents 5 0 R >>
            endobj
            4 0 obj
            << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>
            endobj
            5 0 obj
            << /Length 200 >>
            stream
            BT /F1 12 Tf 72 712 Td ({text}) Tj ET
            endstream
            endobj
            %%EOF
            """;

        return Encoding.Latin1.GetBytes(pdf);
    }

    /// <summary>
    /// Plain UTF-8 text standing in for a scanned/image quote's Document-Intelligence output — see
    /// <see cref="R1ExtractionFixtures.BuildScannedImageOcrBytes"/>'s own doc comment
    /// (<c>FixtureAiGateway.OcrAsync</c> decodes bytes as UTF-8, splitting on the form-feed
    /// character into pages). Two pages (ADR-017 "full document, no 2-page cap").
    /// </summary>
    public static byte[] BuildScannedQuoteBytes()
    {
        const string page1 = "Supplier Quote Q-2002 (scanned copy) for Northwind Traders.";
        const string page2 = "Enterprise Suite, 10 seats, $100/seat, annual term.";

        return Encoding.UTF8.GetBytes(page1 + "\f" + page2);
    }
}
