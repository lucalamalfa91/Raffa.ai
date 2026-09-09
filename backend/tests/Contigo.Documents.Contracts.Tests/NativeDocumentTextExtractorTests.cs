using Contigo.Documents.Contracts.Application.Extraction;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using WP = DocumentFormat.OpenXml.Wordprocessing;

namespace Contigo.Documents.Contracts.Tests;

/// <summary>
/// Proves <see cref="NativeDocumentTextExtractor"/> — the concrete, real native-text half of task
/// E02/F01/US02/T02's hybrid pre-pass. DOCX/XLSX round-trip through the real
/// <c>DocumentFormat.OpenXml</c> SDK (no hand-rolled binary — the SDK's own writer builds the test
/// fixtures). PDF is deliberately <em>not</em> handled here since the ADR-017 amendment of
/// 2026-09-09: every PDF goes to the `ocr` role (Document Intelligence Read on a live deployment,
/// <c>FixturePdfTextScanner</c> under the fixture gateway — see <c>FixturePdfTextScannerTests</c>).
/// </summary>
public sealed class NativeDocumentTextExtractorTests
{
    private const string PdfMimeType = "application/pdf";
    private const string DocxMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string XlsxMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [Fact]
    public void CanHandle_recognizes_docx_and_xlsx_but_neither_pdf_nor_an_image_mime_type()
    {
        var extractor = new NativeDocumentTextExtractor();

        Assert.True(extractor.CanHandle(DocxMimeType));
        Assert.True(extractor.CanHandle(XlsxMimeType));
        Assert.False(extractor.CanHandle(PdfMimeType));
        Assert.False(extractor.CanHandle("image/png"));
    }

    [Fact]
    public void CanHandle_tolerates_a_charset_suffix_and_case_differences()
    {
        var extractor = new NativeDocumentTextExtractor();

        Assert.True(extractor.CanHandle("Application/VND.openxmlformats-officedocument.wordprocessingml.document; charset=binary"));
    }

    [Fact]
    public void A_pdf_handed_to_extract_anyway_is_refused_rather_than_scanned()
    {
        // CanHandle is the contract; a caller that skips it gets a loud NotSupportedException, never
        // a silent "insufficient" that would quietly re-route a PDF through a native path again.
        var extractor = new NativeDocumentTextExtractor();

        Assert.Throws<NotSupportedException>(
            () => extractor.Extract(PdfMimeType, "%PDF-1.4\n1 0 obj << /Type /Page >> endobj\n"u8.ToArray()));
    }

    // ---- DOCX ------------------------------------------------------------------------------

    [Fact]
    public void Docx_text_is_extracted_as_a_single_page_and_always_sufficient()
    {
        var bytes = BuildMinimalDocx("This is a real Word document with actual contract text.");
        var extractor = new NativeDocumentTextExtractor();

        var result = extractor.Extract(DocxMimeType, bytes);

        Assert.True(result.IsSufficient);
        var page = Assert.Single(result.Pages);
        Assert.Equal(1, page.PageNumber);
        Assert.Contains("real Word document", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Corrupted_docx_bytes_are_insufficient_not_a_crash()
    {
        var extractor = new NativeDocumentTextExtractor();

        var result = extractor.Extract(DocxMimeType, "not actually a zip archive"u8.ToArray());

        Assert.False(result.IsSufficient);
        Assert.Empty(result.Pages);
    }

    private static byte[] BuildMinimalDocx(string text)
    {
        using var stream = new MemoryStream();

        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new WP.Document(new WP.Body(new WP.Paragraph(new WP.Run(new WP.Text(text)))));
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    // ---- XLSX ------------------------------------------------------------------------------

    [Fact]
    public void Xlsx_reads_shared_string_and_inline_string_cells_from_every_worksheet()
    {
        var bytes = BuildMinimalXlsx();
        var extractor = new NativeDocumentTextExtractor();

        var result = extractor.Extract(XlsxMimeType, bytes);

        Assert.True(result.IsSufficient);
        var page = Assert.Single(result.Pages);
        Assert.Equal(1, page.PageNumber);
        Assert.Contains("Alpha", page.Text, StringComparison.Ordinal);
        Assert.Contains("Beta", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Corrupted_xlsx_bytes_are_insufficient_not_a_crash()
    {
        var extractor = new NativeDocumentTextExtractor();

        var result = extractor.Extract(XlsxMimeType, "not actually a zip archive either"u8.ToArray());

        Assert.False(result.IsSufficient);
        Assert.Empty(result.Pages);
    }

    private static byte[] BuildMinimalXlsx()
    {
        using var stream = new MemoryStream();

        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();

            var sharedStringPart = workbookPart.AddNewPart<SharedStringTablePart>();
            sharedStringPart.SharedStringTable = new SharedStringTable(new SharedStringItem(new Text("Alpha")));
            sharedStringPart.SharedStringTable.Save();

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new Worksheet(
                new SheetData(
                    new Row(
                        new Cell { CellReference = "A1", DataType = CellValues.SharedString, CellValue = new CellValue("0") },
                        new Cell { CellReference = "B1", DataType = CellValues.InlineString, InlineString = new InlineString(new Text("Beta")) })));
            worksheetPart.Worksheet.Save();

            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = workbookPart.GetIdOfPart(worksheetPart), SheetId = 1, Name = "Sheet1" });
            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }
}
