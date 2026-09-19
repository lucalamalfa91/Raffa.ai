using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.Documents.Contracts.Application.Preview;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using WP = DocumentFormat.OpenXml.Wordprocessing;

namespace Raffa.Documents.Contracts.Tests.Admission;

/// <summary>
/// One admission + extract + preview proof per dropzone format (PDF, DOCX, XLSX, PNG, JPG).
/// A fixture with real contract text must be Admitted, must expose extractable field text,
/// and must produce a real preview page — never <c>no_readable_text</c> / FILE placeholder.
/// </summary>
public sealed class SupportedFormatAdmissionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 19, 10, 0, 0, TimeSpan.Zero);
    private static readonly TenantId Tenant = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private const string Actor = "procurement@acme.example";

    [Fact]
    public async Task Pdf_with_selectable_text_is_admitted_without_ocr_and_exposes_contract_fields()
    {
        var harness = Harness.Create();
        var bytes = BornDigitalPdf.FromPages(BornDigitalPdf.MsaPageOne, BornDigitalPdf.MsaPageTwo);

        var decision = await harness.Gate.EvaluateAsync(Tenant, Actor, "msa.pdf", "application/pdf", bytes);

        Assert.Equal(AdmissionOutcome.Admitted, decision.Outcome);
        Assert.Equal(ContractDocumentType.Msa, decision.DetectedType);
        Assert.True(decision.ReadableChars >= 200);
        Assert.Equal(0, harness.Gateway.OcrCalls);
        Assert.Equal(1, harness.Gateway.ClassifyCalls);
        Assert.Contains("IBM Corporation", string.Join('\n', decision.Pages.Select(p => p.Text)), StringComparison.Ordinal);
        Assert.Contains("Governing law", string.Join('\n', decision.Pages.Select(p => p.Text)), StringComparison.OrdinalIgnoreCase);

        var preview = new PdfPageDocumentPreviewRenderer().Render("msa.pdf", "application/pdf", bytes, page: 1);
        Assert.NotNull(preview);
        Assert.NotEqual(PlaceholderDocumentPreviewRenderer.RenderPlaceholder("FILE"), preview);
        Assert.Equal(0x89, preview![0]);
    }

    [Fact]
    public async Task Docx_order_form_is_admitted_with_table_fields_and_a_real_preview()
    {
        var harness = Harness.Create();
        var bytes = BuildDocx();

        var decision = await harness.Gate.EvaluateAsync(
            Tenant, Actor, "order.docx", DocumentFormatSniffer.DocxMimeType, bytes);

        Assert.Equal(AdmissionOutcome.Admitted, decision.Outcome);
        Assert.True(decision.ReadableChars >= 200);
        Assert.Equal(0, harness.Gateway.OcrCalls);
        var text = string.Join('\n', decision.Pages.Select(p => p.Text));
        Assert.Contains("Supplier | IBM Corporation", text, StringComparison.Ordinal);
        Assert.DoesNotContain("SupplierIBM", text, StringComparison.Ordinal);

        var preview = new OfficePageDocumentPreviewRenderer()
            .Render("order.docx", DocumentFormatSniffer.DocxMimeType, bytes);
        Assert.NotNull(preview);
        Assert.NotEqual(PlaceholderDocumentPreviewRenderer.RenderPlaceholder("FILE"), preview);
    }

    [Fact]
    public async Task Xlsx_workbook_is_admitted_with_sheet_cells_and_a_real_preview()
    {
        var harness = Harness.Create();
        var bytes = BuildXlsx();

        var decision = await harness.Gate.EvaluateAsync(
            Tenant, Actor, "prices.xlsx", DocumentFormatSniffer.XlsxMimeType, bytes);

        Assert.Equal(AdmissionOutcome.Admitted, decision.Outcome);
        Assert.True(decision.ReadableChars >= 200);
        Assert.Equal(0, harness.Gateway.OcrCalls);
        var text = Assert.Single(decision.Pages).Text;
        Assert.Contains("Supplier | IBM Corporation", text, StringComparison.Ordinal);
        Assert.Contains("Annual spend | 188000", text, StringComparison.Ordinal);

        var preview = new OfficePageDocumentPreviewRenderer()
            .Render("prices.xlsx", DocumentFormatSniffer.XlsxMimeType, bytes);
        Assert.NotNull(preview);
        Assert.NotEqual(PlaceholderDocumentPreviewRenderer.RenderPlaceholder("FILE"), preview);
    }

    [Fact]
    public async Task Png_scan_is_admitted_through_ocr_and_uses_the_image_as_preview()
    {
        var harness = Harness.Create();
        var bytes = FixtureScannedPng(BornDigitalPdf.MsaPageOne + " " + BornDigitalPdf.MsaPageTwo);

        var decision = await harness.Gate.EvaluateAsync(
            Tenant, Actor, "scan.png", DocumentFormatSniffer.PngMimeType, bytes);

        Assert.Equal(AdmissionOutcome.Admitted, decision.Outcome);
        Assert.True(decision.ReadableChars >= 200);
        Assert.Equal(1, harness.Gateway.OcrCalls);
        Assert.Contains("MASTER SERVICES AGREEMENT", decision.Pages[0].Text, StringComparison.Ordinal);

        var preview = new ImageDocumentPreviewRenderer()
            .Render("scan.png", DocumentFormatSniffer.PngMimeType, bytes);
        Assert.Equal(bytes, preview);
    }

    [Fact]
    public async Task Jpeg_scan_is_admitted_through_ocr_and_uses_the_image_as_preview()
    {
        var harness = Harness.Create();
        var bytes = FixtureScannedJpeg(BornDigitalPdf.MsaPageOne + " " + BornDigitalPdf.MsaPageTwo);

        var decision = await harness.Gate.EvaluateAsync(
            Tenant, Actor, "scan.jpg", DocumentFormatSniffer.JpegMimeType, bytes);

        Assert.Equal(AdmissionOutcome.Admitted, decision.Outcome);
        Assert.True(decision.ReadableChars >= 200);
        Assert.Equal(1, harness.Gateway.OcrCalls);
        Assert.Contains("IBM Corporation", decision.Pages[0].Text, StringComparison.Ordinal);

        var preview = new ImageDocumentPreviewRenderer()
            .Render("scan.jpg", DocumentFormatSniffer.JpegMimeType, bytes);
        Assert.Equal(bytes, preview);
        Assert.NotEqual(PlaceholderDocumentPreviewRenderer.RenderPlaceholder("FILE"), preview);
    }

    private static byte[] FixtureScannedPng(string text) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Encoding.UTF8.GetBytes(text)];

    private static byte[] FixtureScannedJpeg(string text) =>
        [0xFF, 0xD8, 0xFF, .. Encoding.UTF8.GetBytes(text)];

    private static byte[] BuildDocx()
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            main.Document = new WP.Document(new WP.Body(
                new WP.Paragraph(new WP.Run(new WP.Text("MASTER SERVICES AGREEMENT"))),
                new WP.Paragraph(new WP.Run(new WP.Text(BornDigitalPdf.MsaPageOne))),
                new WP.Table(
                    new WP.TableRow(
                        new WP.TableCell(new WP.Paragraph(new WP.Run(new WP.Text("Supplier")))),
                        new WP.TableCell(new WP.Paragraph(new WP.Run(new WP.Text("IBM Corporation"))))),
                    new WP.TableRow(
                        new WP.TableCell(new WP.Paragraph(new WP.Run(new WP.Text("Governing law")))),
                        new WP.TableCell(new WP.Paragraph(new WP.Run(new WP.Text("New York"))))))));
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static byte[] BuildXlsx()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();
            var sheetPart = workbook.AddNewPart<WorksheetPart>();
            sheetPart.Worksheet = new Worksheet(new SheetData(
                Row("A1", "MASTER SERVICES AGREEMENT", "B1", BornDigitalPdf.MsaPageOne),
                Row("A2", "Supplier", "B2", "IBM Corporation"),
                Row("A3", "Annual spend", "B3", "188000"),
                Row("A4", "Governing law", "B4", "New York")));
            sheetPart.Worksheet.Save();
            var sheets = workbook.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = workbook.GetIdOfPart(sheetPart), SheetId = 1, Name = "Commercials" });
            workbook.Workbook.Save();
        }

        return stream.ToArray();
    }

    private static Row Row(string aRef, string a, string bRef, string b) =>
        new(
            new Cell { CellReference = aRef, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(a)) },
            new Cell { CellReference = bRef, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(b)) });

    private sealed class Harness
    {
        public required DocumentAdmissionGate Gate { get; init; }
        public required CountingGateway Gateway { get; init; }

        public static Harness Create()
        {
            var inner = new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock());
            var gateway = new CountingGateway(inner);
            var parsing = new HybridDocumentParsingService(gateway, new NativeDocumentTextExtractor());
            var gate = new DocumentAdmissionGate(
                parsing, gateway, new DocumentAdmissionOptions(), new TenantContext(), new NullAudit(), new FixedClock());
            return new Harness { Gate = gate, Gateway = gateway };
        }
    }

    private sealed class CountingGateway(IAiGateway inner) : IAiGateway
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

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private sealed class NullAudit : IAuditWriter
    {
        public Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
