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
/// fixtures). PDF selectable text is read via pdfium; image-only scans stay insufficient so
/// the hybrid parser can OCR them.
/// </summary>
public sealed class NativeDocumentTextExtractorTests
{
    private const string PdfMimeType = "application/pdf";
    private const string DocxMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string XlsxMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [Fact]
    public void CanHandle_recognizes_pdf_docx_and_xlsx_but_not_an_image_mime_type()
    {
        var extractor = new NativeDocumentTextExtractor();

        Assert.True(extractor.CanHandle(PdfMimeType));
        Assert.True(extractor.CanHandle(DocxMimeType));
        Assert.True(extractor.CanHandle(XlsxMimeType));
        Assert.False(extractor.CanHandle("image/png"));
        Assert.False(extractor.CanHandle("image/jpeg"));
    }

    [Fact]
    public void CanHandle_tolerates_a_charset_suffix_and_case_differences()
    {
        var extractor = new NativeDocumentTextExtractor();

        Assert.True(extractor.CanHandle("Application/VND.openxmlformats-officedocument.wordprocessingml.document; charset=binary"));
    }

    [Fact]
    public void An_image_handed_to_extract_is_refused_rather_than_scanned()
    {
        var extractor = new NativeDocumentTextExtractor();

        Assert.Throws<NotSupportedException>(
            () => extractor.Extract("image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47 }));
    }

    [Fact]
    public void A_born_digital_pdf_is_extracted_by_pdfium_and_is_sufficient()
    {
        var bytes = BornDigitalPdf.FromPages(BornDigitalPdf.MsaPageOne, BornDigitalPdf.MsaPageTwo);
        var extractor = new NativeDocumentTextExtractor();

        var result = extractor.Extract(PdfMimeType, bytes);

        Assert.True(result.IsSufficient);
        Assert.Equal(2, result.Pages.Count);
        Assert.Contains("MASTER SERVICES AGREEMENT", result.Pages[0].Text, StringComparison.Ordinal);
        Assert.Contains("IBM Corporation", result.Pages[0].Text, StringComparison.Ordinal);
        Assert.Contains("1 January 2026", result.Pages[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_hand_rolled_incomplete_pdf_is_insufficient_so_ocr_can_still_run()
    {
        var extractor = new NativeDocumentTextExtractor();

        var result = extractor.Extract(PdfMimeType, "%PDF-1.4\n1 0 obj << /Type /Page >> endobj\n"u8.ToArray());

        Assert.False(result.IsSufficient);
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

    [Fact]
    public void Docx_table_cells_and_paragraphs_stay_separated_so_fields_are_recoverable()
    {
        // Body.InnerText concatenated this to "SupplierIBM CorporationStart date1 January 2026"
        // and staged extraction missed supplier/dates, leaving status at the bootstrap
        // "processing" placeholder.
        var bytes = BuildOrderFormDocx();
        var extractor = new NativeDocumentTextExtractor();

        var result = extractor.Extract(DocxMimeType, bytes);

        Assert.True(result.IsSufficient);
        var page = Assert.Single(result.Pages);
        Assert.DoesNotContain("SupplierIBM", page.Text, StringComparison.Ordinal);
        Assert.Contains("IBM Enterprise Service Order", page.Text, StringComparison.Ordinal);
        Assert.Contains("Supplier | IBM Corporation", page.Text, StringComparison.Ordinal);
        Assert.Contains("Start date | 1 January 2026", page.Text, StringComparison.Ordinal);
        Assert.Contains("Governing law", page.Text, StringComparison.Ordinal);
        Assert.Contains("New York", page.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Docx_page_breaks_become_separate_pages_and_stay_sufficient()
    {
        var bytes = BuildDocxWithPageBreak();
        var extractor = new NativeDocumentTextExtractor();

        var result = extractor.Extract(DocxMimeType, bytes);

        Assert.True(result.IsSufficient);
        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(1, result.Pages[0].PageNumber);
        Assert.Equal(2, result.Pages[1].PageNumber);
        Assert.Contains("IBM Corporation", result.Pages[0].Text, StringComparison.Ordinal);
        Assert.Contains("1 January 2026", result.Pages[1].Text, StringComparison.Ordinal);
        Assert.DoesNotContain("1 January 2026", result.Pages[0].Text, StringComparison.Ordinal);
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

    private static byte[] BuildOrderFormDocx()
    {
        using var stream = new MemoryStream();

        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            var headerPart = mainPart.AddNewPart<HeaderPart>();
            headerPart.Header = new WP.Header(new WP.Paragraph(new WP.Run(new WP.Text("IBM Enterprise Service Order"))));
            headerPart.Header.Save();

            mainPart.Document = new WP.Document(new WP.Body(
                new WP.Paragraph(new WP.Run(new WP.Text("Order Form"))),
                new WP.Table(
                    new WP.TableRow(
                        new WP.TableCell(new WP.Paragraph(new WP.Run(new WP.Text("Supplier")))),
                        new WP.TableCell(new WP.Paragraph(new WP.Run(new WP.Text("IBM Corporation"))))),
                    new WP.TableRow(
                        new WP.TableCell(new WP.Paragraph(new WP.Run(new WP.Text("Start date")))),
                        new WP.TableCell(new WP.Paragraph(new WP.Run(new WP.Text("1 January 2026")))))),
                new WP.Paragraph(new WP.Run(new WP.Text("Governing law"))),
                new WP.Paragraph(new WP.Run(new WP.Text("New York")))));
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static byte[] BuildDocxWithPageBreak()
    {
        using var stream = new MemoryStream();

        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new WP.Document(new WP.Body(
                new WP.Paragraph(new WP.Run(new WP.Text("IBM Corporation"))),
                new WP.Paragraph(new WP.Run(
                    new WP.LastRenderedPageBreak(),
                    new WP.Text("Start date 1 January 2026")))));
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
