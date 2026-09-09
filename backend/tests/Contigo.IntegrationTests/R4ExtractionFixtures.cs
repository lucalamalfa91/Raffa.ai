using System.Text;

namespace Contigo.IntegrationTests;

/// <summary>
/// Shared fixture data for task E05/F04/US01/T01 (r4-integration): a hand-built, minimal-but-real
/// born-digital quote PDF whose one line item is deliberately built to match one of
/// <c>Contigo.Benchmark.Fixtures.FixtureBenchmarkAdapter</c>'s own catalog rows — Salesforce Sales
/// Cloud Enterprise, 100 seats, 12-month US/USD (P25/P50/P75 = 1500/1800/2100 per seat/year, sample
/// size 512) — the same "matched quote" stand-in <c>Contigo.Quotes.Tests.MarketAssessmentServiceTests</c>/
/// <c>NegotiationStrategyServiceTests</c> and <c>Contigo.IntegrationTests.R3EndToEndTests</c> already
/// establish for this exact catalog row, driven here through the real upload/extraction HTTP path
/// instead of a hand-seeded row. Same hand-built-PDF technique as
/// <see cref="QuoteExtractionScriptedPayloads.BuildBornDigitalQuoteBytes"/>/
/// <see cref="R1ExtractionFixtures.BuildBornDigitalPdfBytes"/> — duplicated, not shared, only because
/// the embedded text and scripted payload need their own R4-specific numbers (a 2,200/seat quoted
/// price, deliberately above the catalog's own P75 of 2,100, so the R4 Day-1 path has a real,
/// non-trivial saving to negotiate — spec §20's "where we can save money").
///
/// <para>
/// The scripted line deliberately carries a raw <c>sku</c> ("SKU-SFDC-ENT") with **no** matching
/// <c>Contigo.Quotes.Domain.SkuProductMapping</c> seeded anywhere — every tenant starts with zero
/// mappings (see that type's own doc comment), so this line is <c>SkuMatchStatus.Unmatched</c>
/// immediately after upload. <see cref="R4EndToEndTests"/> corrects that mapping mid-flow (parent
/// story us-01-final-integration AC-2, "User can correct SKU matching before accepting assessment")
/// before reading the assessment — see that class's own doc comment for why a direct
/// <c>SkuProductMapping</c> insert, not a dedicated HTTP endpoint, is how this task proves that
/// correction (task E05/F01/US02/T02, "Manual product mapping + recalculate trigger", never landed
/// any code — an honest, still-open wave-spec gap, not something this task invents a feature to
/// close).
/// </para>
/// </summary>
internal static class R4ExtractionFixtures
{
    public const string BornDigitalFileName = "quote-salesforce-sales-cloud.pdf";
    public const string BornDigitalMimeType = "application/pdf";

    public const string RawSku = "SKU-SFDC-ENT";
    public const string ExpectedEdition = "Enterprise";
    public const string ProductDescription = "Sales Cloud Enterprise";

    /// <summary>Quoted per-seat price — deliberately above the matched fixture catalog row's own P75
    /// (2,100), so <c>Contigo.Quotes.Application.Assessment.MarketAssessmentCalculator</c> classifies
    /// this line <c>MarketPosition.AboveMarket</c> and there is a real, non-zero recommended
    /// target/saving for <see cref="R4EndToEndTests"/> to carry through negotiation strategy and
    /// outcome capture.</summary>
    public const decimal QuotedUnitPrice = 2200m;

    public const decimal Quantity = 100m;

    /// <summary>Exact literal <c>Contigo.Benchmark.Contracts.BenchmarkQuery.Term</c> match for the
    /// 12-month Salesforce/Sales Cloud Enterprise catalog row — see
    /// <c>Contigo.Quotes.Application.Assessment.MarketAssessmentQueryBuilder</c>'s own doc comment for
    /// why this is passed through verbatim, never normalized against
    /// <c>Contigo.Quotes.Application.Normalization.QuoteBillingCadence</c>'s own, different,
    /// annualization vocabulary (which does not recognize "12 months" either — an honest, expected
    /// "quote-normalization unresolved" outcome for this line that does not block assessment).</summary>
    public const string Term = "12 months";

    /// <summary>
    /// Scripted `extract` payload for the `QuoteLineItems` stage
    /// (<c>Contigo.Api.QuoteExtractionPipeline.StageName</c>) — same shape
    /// <see cref="QuoteExtractionScriptedPayloads.PayloadsByStage"/> already establishes for the
    /// sibling quote-extraction fixture. Never states a total/extended price anywhere (AC-3):
    /// <c>QuoteLineExtractionService</c> must derive <c>ExtendedPrice</c> = 220,000 (100 × 2,200)
    /// itself.
    /// </summary>
    public static IReadOnlyDictionary<string, string> PayloadsByStage { get; } = new Dictionary<string, string>
    {
        ["QuoteLineItems"] = $$"""
            {"items":[
                {"sku":"{{RawSku}}","edition":"{{ExpectedEdition}}","description":"{{ProductDescription}}",
                 "quantity":{{Quantity}},"unit":"seat","unitPrice":{{QuotedUnitPrice}},"term":"{{Term}}",
                 "sourcePage":1,"sourceSpan":"100 seats @ $2,200/seat, 12-month term","confidence":0.95}
            ]}
            """,
    };

    /// <summary>Minimal, hand-built, syntactically real single-page PDF — see
    /// <see cref="R1ExtractionFixtures.BuildBornDigitalPdfBytes"/>'s own doc comment for the
    /// technique; the fixture `ocr` role reads it through <c>FixturePdfTextScanner</c> — this
    /// task's own Day-1 proof does not need to re-prove OCR routing (already proved by
    /// <c>Contigo.IntegrationTests.QuoteEndToEndTests</c>/<c>R1EndToEndTests</c>).</summary>
    public static byte[] BuildBornDigitalQuoteBytes()
    {
        const string text =
            "Supplier Quote Q-4001 for Acme Corp: Sales Cloud Enterprise, 100 seats, " +
            "$2,200/seat, 12-month term.";

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
}
