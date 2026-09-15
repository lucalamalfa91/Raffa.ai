using Raffa.Documents.Contracts.Application.Extraction;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using WP = DocumentFormat.OpenXml.Wordprocessing;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Proves <see cref="NativeDocumentTextExtractor"/> — the concrete, real native-text half of task
/// E02/F01/US02/T02's hybrid pre-pass. DOCX/XLSX round-trip through the real
/// <c>DocumentFormat.OpenXml</c> SDK (no hand-rolled binary — the SDK's own writer builds the test
/// fixtures). PDF is now handled via <c>PdfPig</c> (instant-identity-ingest amendment):
/// born-digital PDFs that have enough embedded text skip OCR entirely; scanned/image PDFs
/// degrade to <see cref="NativeTextExtractionResult.IsSufficient"/> = false and fall through
/// to OCR in <see cref="HybridDocumentParsingService"/>.
/// </summary>
public sealed class NativeDocumentTextExtractorTests
{
    private const string PdfMimeType = "application/pdf";
    private const string DocxMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string XlsxMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [Fact]
    public void CanHandle_recognizes_pdf_docx_and_xlsx_but_not_images()
    {
        var extractor = new NativeDocumentTextExtractor();

        // instant-identity-ingest: PDF is now handled via PdfPig.
        Assert.True(extractor.CanHandle(PdfMimeType));
        Assert.True(extractor.CanHandle(DocxMimeType));
        Assert.True(extractor.CanHandle(XlsxMimeType));
        Assert.False(extractor.CanHandle("image/png"));
        Assert.False(extractor.CanHandle("image/tiff"));
    }

    [Fact]
    public void CanHandle_tolerates_a_charset_suffix_and_case_differences()
    {
        var extractor = new NativeDocumentTextExtractor();

        Assert.True(extractor.CanHandle("Application/VND.openxmlformats-officedocument.wordprocessingml.document; charset=binary"));
    }

    [Fact]
    public void A_corrupt_pdf_is_insufficient_not_a_crash()
    {
        // Untrusted bytes that are not a valid PDF: PdfPig catches the parse error internally
        // and the extractor degrades to IsSufficient = false, letting HybridDocumentParsingService
        // fall back to OCR — same graceful-degrade posture as corrupt DOCX/XLSX.
        var extractor = new NativeDocumentTextExtractor();

        var result = extractor.Extract(PdfMimeType, "not a real pdf"u8.ToArray());

        Assert.False(result.IsSufficient);
        Assert.Empty(result.Pages);
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
