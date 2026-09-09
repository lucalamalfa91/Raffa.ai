using System.IO.Compression;
using System.Text;
using Contigo.Documents.Contracts.Application.Admission;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace Contigo.Documents.Contracts.Tests.Admission;

/// <summary>
/// Task E13/F04/US01/T01 (documents-admission): <see cref="DocumentFormatSniffer"/> — extension
/// and magic bytes must agree (<c>inputs/requirements.md</c> R-DOC-02, AC-1 "a .zip renamed .pdf
/// is refused").
/// </summary>
public sealed class DocumentFormatSnifferTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

    [Fact]
    public void Pdf_named_pdf_is_detected_with_the_canonical_mime_type()
    {
        var bytes = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Page >> endobj\n");

        Assert.True(DocumentFormatSniffer.TryDetect("Contract.PDF", bytes, out var detection));
        Assert.Equal(UploadedDocumentFormat.Pdf, detection.Format);
        Assert.Equal("application/pdf", detection.MimeType);
        Assert.Equal(".pdf", detection.Extension);
    }

    [Fact]
    public void Pdf_header_may_sit_behind_up_to_1_KiB_of_leading_bytes()
    {
        var bytes = Encoding.ASCII.GetBytes(new string(' ', 900) + "%PDF-1.7\n");

        Assert.True(DocumentFormatSniffer.TryDetect("late-header.pdf", bytes, out var detection));
        Assert.Equal(UploadedDocumentFormat.Pdf, detection.Format);
    }

    [Fact]
    public void Pdf_header_beyond_1_KiB_is_not_a_pdf()
    {
        var bytes = Encoding.ASCII.GetBytes(new string(' ', 2000) + "%PDF-1.7\n");

        Assert.False(DocumentFormatSniffer.TryDetect("buried.pdf", bytes, out _));
    }

    [Fact]
    public void Zip_renamed_pdf_is_refused()
    {
        var bytes = BuildZip(("readme.txt", "not a contract"));

        Assert.False(DocumentFormatSniffer.TryDetect("archive.pdf", bytes, out _));
    }

    [Fact]
    public void Docx_is_detected_by_its_main_part_not_just_the_zip_signature()
    {
        var docx = BuildMinimalDocx("Master Services Agreement");
        Assert.True(DocumentFormatSniffer.TryDetect("msa.docx", docx, out var detection));
        Assert.Equal(UploadedDocumentFormat.Docx, detection.Format);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document", detection.MimeType);

        var plainZip = BuildZip(("notes/readme.txt", "just a zip"));
        Assert.False(DocumentFormatSniffer.TryDetect("renamed.docx", plainZip, out _));
    }

    [Fact]
    public void Xlsx_is_detected_and_a_docx_renamed_xlsx_is_refused()
    {
        var xlsx = BuildMinimalXlsx();
        Assert.True(DocumentFormatSniffer.TryDetect("price-list.xlsx", xlsx, out var detection));
        Assert.Equal(UploadedDocumentFormat.Xlsx, detection.Format);
        Assert.Equal(
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", detection.MimeType);

        var docx = BuildMinimalDocx("Master Services Agreement");
        Assert.False(DocumentFormatSniffer.TryDetect("mislabelled.xlsx", docx, out _));
    }

    [Fact]
    public void Png_and_jpeg_are_detected_by_signature()
    {
        Assert.True(DocumentFormatSniffer.TryDetect("scan.png", PngSignature, out var png));
        Assert.Equal(UploadedDocumentFormat.Png, png.Format);
        Assert.Equal("image/png", png.MimeType);

        Assert.True(DocumentFormatSniffer.TryDetect("scan.jpg", JpegSignature, out var jpg));
        Assert.Equal(UploadedDocumentFormat.Jpeg, jpg.Format);
        Assert.Equal("image/jpeg", jpg.MimeType);

        Assert.True(DocumentFormatSniffer.TryDetect("scan.JPEG", JpegSignature, out var jpeg));
        Assert.Equal(UploadedDocumentFormat.Jpeg, jpeg.Format);
        Assert.Equal(".jpeg", jpeg.Extension);
    }

    [Fact]
    public void Extension_and_bytes_must_agree()
    {
        // A PNG renamed .pdf is refused rather than silently re-labelled.
        Assert.False(DocumentFormatSniffer.TryDetect("photo.pdf", PngSignature, out _));
        // A PDF renamed .png likewise.
        Assert.False(DocumentFormatSniffer.TryDetect("contract.png", Encoding.ASCII.GetBytes("%PDF-1.4"), out _));
    }

    [Theory]
    [InlineData("contract.tiff")]
    [InlineData("contract.gif")]
    [InlineData("contract.doc")]
    [InlineData("contract.txt")]
    [InlineData("contract")]
    [InlineData("")]
    [InlineData(null)]
    public void Unsupported_or_missing_extension_is_refused(string? fileName)
    {
        Assert.False(DocumentFormatSniffer.TryDetect(fileName, Encoding.ASCII.GetBytes("%PDF-1.4 some bytes"), out _));
    }

    [Fact]
    public void Empty_content_is_refused()
    {
        Assert.False(DocumentFormatSniffer.TryDetect("contract.pdf", [], out _));
    }

    private static byte[] BuildZip(params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return stream.ToArray();
    }

    private static byte[] BuildMinimalDocx(string text)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new W.Document(new W.Body(new W.Paragraph(new W.Run(new W.Text(text)))));
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static byte[] BuildMinimalXlsx()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new S.Workbook();
            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = new S.Worksheet(new S.SheetData());
            var sheets = workbookPart.Workbook.AppendChild(new S.Sheets());
            sheets.Append(new S.Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "Prices",
            });
            workbookPart.Workbook.Save();
        }

        return stream.ToArray();
    }
}
