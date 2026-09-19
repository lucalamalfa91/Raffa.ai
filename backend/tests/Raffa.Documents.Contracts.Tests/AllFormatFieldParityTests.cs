using System.Text;
using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Raffa.AiGateway;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.SharedKernel;
using WP = DocumentFormat.OpenXml.Wordprocessing;

namespace Raffa.Documents.Contracts.Tests;

/// <summary>
/// Every dropzone format (PDF, DOCX, XLSX, PNG, JPG) must hand staged extraction the same
/// labelled facts a reviewer can read. Admission pages are the pages extraction receives —
/// there is no classify-only prefix. Status is date-derived when start/end exist; supplier is
/// never invented.
/// </summary>
public sealed class AllFormatFieldParityTests
{
    private static readonly DateOnly Today = new(2026, 9, 19);

    private const string LabelledContract =
        "MASTER SERVICES AGREEMENT\n" +
        "Supplier: IBM Corporation\n" +
        "Currency: USD\n" +
        "Governing law: New York\n" +
        "Annual spend: USD 188000\n" +
        "Total contract value: USD 391000\n" +
        "Payment terms: Net 60\n" +
        "Effective date: 1 January 2026\n" +
        "Start date: 1 January 2026\n" +
        "End date: 31 December 2028\n" +
        "Cancellation deadline: 2 October 2028\n" +
        "This Agreement renews automatically for successive twelve month terms.";

    [Theory]
    [InlineData("pdf")]
    [InlineData("docx")]
    [InlineData("xlsx")]
    [InlineData("png")]
    [InlineData("jpeg")]
    public async Task Format_populates_supplier_dates_law_spend_and_date_derived_status(string format)
    {
        var (fileName, mimeType, bytes) = Build(format);
        var gateway = new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock());
        var parse = await new HybridDocumentParsingService(gateway, new NativeDocumentTextExtractor())
            .ParseAsync(fileName, mimeType, bytes);

        Assert.True(parse.IsSuccess, parse.IsFailure ? parse.Error : null);
        Assert.NotEmpty(parse.Value);

        var handedToExtraction = string.Join('\n', parse.Value.Select(page => page.Text));
        Assert.Contains("IBM Corporation", handedToExtraction, StringComparison.Ordinal);
        Assert.Contains("1 January 2026", handedToExtraction, StringComparison.Ordinal);
        Assert.DoesNotContain("SupplierIBM", handedToExtraction, StringComparison.Ordinal);

        var marked = PageMarked(parse.Value);
        var metadata = Facts("Metadata", marked);
        var commercial = Facts("CommercialTerms", marked);
        var dates = Facts("DatesAndRenewalTerms", marked);

        Assert.Equal("IBM Corporation", Value(metadata["supplier"]));
        Assert.True(Confidence(metadata["supplier"]) >= ExtractionConfidencePolicy.AutoAcceptThreshold);
        Assert.Equal("USD", Value(metadata["currency"]));
        Assert.Equal("New York", Value(metadata["governingLaw"]));

        Assert.Equal("188000", Value(commercial["annualSpend"]));
        Assert.Equal("391000", Value(commercial["totalContractValue"]));
        Assert.Equal("Net 60", Value(commercial["paymentTerms"]));

        Assert.Equal("2026-01-01", Value(dates["startDate"]));
        Assert.Equal("2026-01-01", Value(dates["effectiveDate"]));
        Assert.Equal("2028-12-31", Value(dates["endDate"]));
        Assert.Equal("2028-10-02", Value(dates["cancellationDeadline"]));
        Assert.Equal("true", Value(dates["autoRenewal"]));
        Assert.True(Confidence(dates["startDate"]) >= ExtractionConfidencePolicy.AutoAcceptThreshold);

        var status = ExtractionConfidencePolicy.DeriveStatus(
            DateOnly.Parse(Value(dates["startDate"])),
            DateOnly.Parse(Value(dates["endDate"])),
            Today);
        Assert.Equal("active", status);
        Assert.NotEqual("processing", status);
    }

    [Fact]
    public async Task A_docx_without_a_supplier_does_not_invent_one()
    {
        var bytes = BuildDocx("This master services agreement names no supplier at all. Start date: 1 January 2026.");
        var gateway = new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock());
        var parse = await new HybridDocumentParsingService(gateway, new NativeDocumentTextExtractor())
            .ParseAsync("blank.docx", DocumentFormatSniffer.DocxMimeType, bytes);

        Assert.True(parse.IsSuccess, parse.IsFailure ? parse.Error : null);
        var facts = Facts("Metadata", PageMarked(parse.Value));
        Assert.False(facts.ContainsKey("supplier"));
    }

    private static string PageMarked(IReadOnlyList<DocumentPageText> pages)
    {
        var builder = new StringBuilder();
        foreach (var page in pages)
        {
            builder.Append("[[PAGE ").Append(page.PageNumber).Append("]]\n");
            builder.Append(page.Text);
            builder.Append("\n\n");
        }

        return builder.ToString();
    }

    private static Dictionary<string, JsonElement> Facts(string stage, string text)
    {
        var payload = FixtureContractFactExtractor.Extract(stage, text);
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty("facts").EnumerateArray()
            .ToDictionary(f => f.GetProperty("field").GetString()!, f => f.Clone(), StringComparer.Ordinal);
    }

    private static string Value(JsonElement fact) => fact.GetProperty("value").GetString()!;
    private static double Confidence(JsonElement fact) => fact.GetProperty("confidence").GetDouble();

    private static (string FileName, string MimeType, byte[] Bytes) Build(string format) => format switch
    {
        "pdf" => ("order.pdf", "application/pdf", BornDigitalPdf.FromPages(LabelledContract)),
        "docx" => ("order.docx", DocumentFormatSniffer.DocxMimeType, BuildDocxOrderForm()),
        "xlsx" => ("order.xlsx", DocumentFormatSniffer.XlsxMimeType, BuildXlsxOrderForm()),
        "png" => ("scan.png", DocumentFormatSniffer.PngMimeType, FixtureScan([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], LabelledContract)),
        "jpeg" => ("scan.jpg", DocumentFormatSniffer.JpegMimeType, FixtureScan([0xFF, 0xD8, 0xFF], LabelledContract)),
        _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported parity format."),
    };

    private static byte[] FixtureScan(byte[] signature, string text) =>
        [.. signature, .. Encoding.UTF8.GetBytes(text)];

    private static byte[] BuildDocx(string body) => BuildDocxOrderForm(body, includeTable: false);

    private static byte[] BuildDocxOrderForm(string? extraParagraph = null, bool includeTable = true)
    {
        using var stream = new MemoryStream();
        using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            var header = main.AddNewPart<HeaderPart>();
            header.Header = new WP.Header(new WP.Paragraph(new WP.Run(new WP.Text("IBM Enterprise Service Order"))));
            header.Header.Save();

            var children = new List<OpenXmlElement>
            {
                new WP.Paragraph(new WP.Run(new WP.Text(extraParagraph ?? LabelledContract))),
            };
            if (includeTable)
            {
                children.Add(new WP.Table(
                    Row("Supplier", "IBM Corporation"),
                    Row("Start date", "1 January 2026"),
                    Row("End date", "31 December 2028"),
                    Row("Governing law", "New York"),
                    Row("Annual spend", "USD 188000"),
                    Row("Total contract value", "USD 391000")));
            }

            main.Document = new WP.Document(new WP.Body(children));
            main.Document.Save();
        }

        return stream.ToArray();
    }

    private static WP.TableRow Row(string label, string value) =>
        new(
            new WP.TableCell(new WP.Paragraph(new WP.Run(new WP.Text(label)))),
            new WP.TableCell(new WP.Paragraph(new WP.Run(new WP.Text(value)))));

    private static byte[] BuildXlsxOrderForm()
    {
        using var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook))
        {
            var workbook = document.AddWorkbookPart();
            workbook.Workbook = new Workbook();

            var cover = workbook.AddNewPart<WorksheetPart>();
            cover.Worksheet = new Worksheet(new SheetData(XlsxRow("A1", "Cover", "B1", LabelledContract)));
            cover.Worksheet.Save();

            var commercials = workbook.AddNewPart<WorksheetPart>();
            commercials.Worksheet = new Worksheet(new SheetData(
                XlsxRow("A1", "Supplier", "B1", "IBM Corporation"),
                XlsxRow("A2", "Start date", "B2", "1 January 2026"),
                XlsxRow("A3", "End date", "B3", "31 December 2028"),
                XlsxRow("A4", "Governing law", "B4", "New York")));
            commercials.Worksheet.Save();

            var sheets = workbook.Workbook.AppendChild(new Sheets());
            sheets.Append(new Sheet { Id = workbook.GetIdOfPart(cover), SheetId = 1, Name = "Cover" });
            sheets.Append(new Sheet { Id = workbook.GetIdOfPart(commercials), SheetId = 2, Name = "Commercials" });
            workbook.Workbook.Save();
        }

        return stream.ToArray();
    }

    private static Row XlsxRow(string aRef, string a, string bRef, string b) =>
        new(
            new Cell { CellReference = aRef, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(a)) },
            new Cell { CellReference = bRef, DataType = CellValues.InlineString, InlineString = new InlineString(new Text(b)) });

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
    }
}
