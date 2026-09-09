using System.Text;

namespace Contigo.IntegrationTests;

/// <summary>
/// Shared fixture data for task E02/F06/US01/T01 (r1-integration): a hand-built, minimal-but-real
/// born-digital PDF (proves the native text extraction path — <c>NativeDocumentTextExtractor</c>
/// never routes it through the `ocr` gateway role), a scanned/image-style document (proves AC-4's
/// "at least one scanned or image-based contract extracts via Document Intelligence" — an
/// <c>image/png</c> mime type <c>NativeDocumentTextExtractor.CanHandle</c> always returns
/// <see langword="false"/> for, so <c>HybridDocumentParsingService</c> structurally cannot take the
/// native path), and the scripted `extract` payloads <see cref="ScriptedR1AiGateway"/> returns for
/// both. Centralized here so <see cref="R1IntegrationFixture"/>, <c>R1EndToEndTests</c> and
/// <c>R1CrossTenantIsolationTests</c> assert against the same named values instead of duplicating
/// magic strings.
/// </summary>
internal static class R1ExtractionFixtures
{
    public const string BornDigitalFileName = "msa-acme-contoso.pdf";
    public const string BornDigitalMimeType = "application/pdf";

    // Task E13/F04/US01/T01 (documents-admission): PNG rather than the original TIFF — R-DOC-02
    // accepts exactly PDF/DOCX/XLSX/PNG/JPEG, and POST /api/documents now sniffs extension *and*
    // magic bytes, so a .tiff upload is refused (415) before it can reach the pipeline this
    // fixture exercises. PNG keeps the property that made TIFF the right choice here.
    public const string ScannedFileName = "scanned-msa-northwind.png";
    // Not one of NativeDocumentTextExtractor's three recognized mime types (PDF/DOCX/XLSX) — see
    // that type's own CanHandle — so HybridDocumentParsingService.ParseAsync always falls back to
    // the `ocr` gateway role for this fixture, by construction, not by chance (AC-4).
    public const string ScannedMimeType = "image/png";

    /// <summary>The commercial-terms stage's own low-confidence fact (below
    /// <c>DocumentProcessingPipeline</c>/<c>StagedExtractionService</c>'s shared 0.6 threshold) —
    /// AC-2's "low-confidence field correction" needs one real field to correct. Both fixture
    /// documents extract to this same value; each is staged into its own, separate
    /// <c>Contract</c> row (one per uploaded <c>Document</c>).</summary>
    public const string OriginalAnnualSpend = "48000";

    /// <summary>What AC-2's correction test PATCHes <see cref="OriginalAnnualSpend"/> to.</summary>
    public const string CorrectedAnnualSpend = "60000";

    /// <summary>
    /// How <see cref="OriginalAnnualSpend"/> reads back as a string once round-tripped through
    /// <c>Contract.AnnualSpend</c>'s <c>numeric(18,2)</c> column
    /// (<c>ContractConfiguration.HasPrecision(18, 2)</c>): <c>ContractCorrectionService</c>'s own
    /// `previousValue` for a correction comes from a fresh <c>Contract</c> row read (<c>Read: c =&gt;
    /// read(c)?.ToString(CultureInfo.InvariantCulture)</c>), not from this fixture's original JSON
    /// string, so it always carries the column's fixed two-decimal scale — unlike
    /// <c>ExtractionEvidence.Value</c> (a plain string column that stores the extraction payload's
    /// literal value verbatim, never coerced through a `numeric` column) or a correction's own
    /// `newValue` (computed by parsing+formatting the caller's raw PATCH input directly, before any
    /// database round trip) — see <c>ContractCorrectionService.CorrectAsync</c>'s own remarks.
    /// </summary>
    public const string OriginalAnnualSpendAsStoredInDb = "48000.00";

    public const string ExpectedLineItemSku = "SKU-CLOUD-100";
    public const string ExpectedClauseType = "termination";
    public const string ExpectedObligationParty = "Customer";
    public const string ExpectedRiskType = "liability";

    /// <summary>
    /// Scripted `extract` payloads, keyed by <c>AiExtractionRequest.StageName</c> (i.e.
    /// <c>ExtractionStage.ToString()</c>) — same shape as
    /// <c>Contigo.Documents.Contracts.Tests.StagedExtractionServiceTests.HighConfidencePayloads</c>,
    /// with one deliberately low-confidence fact (<see cref="OriginalAnnualSpend"/>, 0.35) so the
    /// resulting <c>Contract</c> has a real field AC-2's correction test can target.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PayloadsByStage { get; } = new Dictionary<string, string>
    {
        ["Metadata"] = """
            {"facts":[
                {"field":"currency","value":"USD","sourcePage":1,"sourceSpan":"Currency: USD","confidence":0.95},
                {"field":"governingLaw","value":"State of Delaware","sourcePage":1,"sourceSpan":"Governing law: Delaware","confidence":0.9},
                {"field":"status","value":"Active","sourcePage":1,"sourceSpan":"Status: Active","confidence":0.9}
            ]}
            """,
        ["CommercialTerms"] = $$"""
            {"facts":[
                {"field":"annualSpend","value":"{{OriginalAnnualSpend}}","sourcePage":2,"sourceSpan":"Annual spend (approx): $48,000","confidence":0.35},
                {"field":"totalContractValue","value":"144000","sourcePage":2,"sourceSpan":"TCV: $144,000","confidence":0.9},
                {"field":"paymentTerms","value":"Net 30","sourcePage":2,"sourceSpan":"Payment terms: Net 30","confidence":0.9}
            ]}
            """,
        ["DatesAndRenewalTerms"] = """
            {"facts":[
                {"field":"startDate","value":"2026-01-01","sourcePage":1,"confidence":0.9},
                {"field":"endDate","value":"2027-01-01","sourcePage":1,"confidence":0.9},
                {"field":"autoRenewal","value":"true","sourcePage":1,"confidence":0.9},
                {"field":"renewalTermMonths","value":"12","sourcePage":1,"confidence":0.9}
            ]}
            """,
        ["LineItems"] = $$"""
            {"items":[
                {"sku":"{{ExpectedLineItemSku}}","description":"Cloud Hosting Services","quantity":12,"unit":"month","unitPrice":4000,"sourcePage":3,"sourceSpan":"12 months @ $4,000","confidence":0.9}
            ]}
            """,
        ["LegalClauses"] = $$"""
            {"items":[
                {"clauseType":"{{ExpectedClauseType}}","rawText":"Either party may terminate for convenience with 90 days notice.","riskLevel":"Medium","sourcePage":4,"sourceSpan":"Termination clause","confidence":0.85}
            ]}
            """,
        ["Obligations"] = $$"""
            {"items":[
                {"party":"{{ExpectedObligationParty}}","obligationType":"payment","description":"Pay invoice within 30 days of receipt","dueDate":"2026-02-01","sourcePage":2,"sourceSpan":"Payment obligation","confidence":0.8}
            ]}
            """,
        ["Risk"] = $$"""
            {"items":[
                {"riskType":"{{ExpectedRiskType}}","severity":"High","description":"Uncapped liability clause","sourcePage":4,"sourceSpan":"Liability clause","confidence":0.75}
            ]}
            """,
    };

    /// <summary>
    /// A minimal, hand-built, syntactically real single-page PDF: one <c>/Type /Page</c> object
    /// and one uncompressed content stream with a <c>BT ... Tj ... ET</c> text object — enough for
    /// <c>NativeDocumentTextExtractor.ExtractPdfNatively</c>'s own lightweight scan (it counts
    /// <c>/Type /Page</c> occurrences and pairs them 1:1 with content streams; it does not parse
    /// the cross-reference table, so no <c>xref</c>/<c>trailer</c> section is needed for this scan
    /// to succeed — see that method's own doc comment) to report
    /// <c>IsSufficient: true</c>, so <c>HybridDocumentParsingService</c> never calls the `ocr` role
    /// for this fixture. The embedded text contains "MASTER SERVICES AGREEMENT" so
    /// <c>FixtureAiGateway.ClassifyAsync</c>'s keyword match resolves it to
    /// <c>AiDocumentType.Msa</c>, and is well over the extractor's 40-non-whitespace-char-per-page
    /// sufficiency floor.
    /// </summary>
    /// <summary>The 8-byte PNG signature <see cref="BuildScannedImageOcrBytes"/> prefixes its page
    /// text with — what <c>DocumentFormatSniffer</c> checks and <c>FixtureAiGateway</c> strips.</summary>
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static byte[] BuildBornDigitalPdfBytes()
    {
        // Task E13/F04/US01/T01: comfortably over Documents:MinReadableChars (200 non-whitespace
        // characters) as well, so the admission gate admits it on the same defaults production
        // runs with — the text grew, nothing else about this fixture changed.
        const string text =
            "MASTER SERVICES AGREEMENT between Acme Corp and Contoso Ltd, effective 2026-01-01, " +
            "governed by the laws of the State of Delaware. This Agreement sets out the terms under " +
            "which the Supplier provides subscription services to the Customer, and applies to every " +
            "Order Form the parties execute under it. Fees are invoiced annually in advance and are " +
            "payable within thirty days. The initial term is thirty-six months and renews " +
            "automatically for successive twelve-month terms unless either party gives ninety days " +
            "written notice before the end of the then-current term.";

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

        // Latin1: a lossless byte<->char round trip for the ASCII-only text above, matching
        // NativeDocumentTextExtractor.ExtractPdfNatively's own Encoding.Latin1.GetString decode.
        return Encoding.Latin1.GetBytes(pdf);
    }

    /// <summary>
    /// Plain UTF-8 text standing in for a scanned/image document's Document-Intelligence output —
    /// <c>FixtureAiGateway.OcrAsync</c> decodes whatever bytes it receives as UTF-8 and splits on
    /// the form-feed character (<c>\f</c>) into pages (see that method's own doc comment); this is
    /// the fixture's own honest stand-in for "no live Document Intelligence endpoint configured"
    /// scanned-page text, not real scanned bytes. Two pages (ADR-017 "full document, no 2-page
    /// cap" — proving more than a single page survives the OCR path) and, like the born-digital
    /// fixture, contains "MASTER SERVICES AGREEMENT" so classification resolves the same way.
    ///
    /// <para>
    /// Task E13/F04/US01/T01 (documents-admission): the bytes now start with the real 8-byte PNG
    /// signature, because <c>POST /api/documents</c> sniffs magic bytes before anything else and
    /// would otherwise refuse this fixture with a 415. <c>FixtureAiGateway.OcrAsync</c> strips that
    /// signature before decoding, so the page text is unchanged; the OCR'd text is also over the
    /// gate's 200-non-whitespace-character floor.
    /// </para>
    /// </summary>
    public static byte[] BuildScannedImageOcrBytes()
    {
        const string page1 =
            "MASTER SERVICES AGREEMENT (scanned copy) between Northwind Traders and Fabrikam Inc. " +
            "This copy was captured by a scanner; no digital text layer is present in the original file.";
        const string page2 =
            "Continued: governing law is the State of Delaware. Signatures appear on the final page " +
            "of the scanned image.";

        return [.. PngSignature, .. Encoding.UTF8.GetBytes(page1 + "\f" + page2)];
    }
}
