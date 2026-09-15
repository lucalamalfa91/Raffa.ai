using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Real (non-AI) text extraction for born-digital document formats spec §4 accepts:
/// <list type="bullet">
/// <item><b>DOCX / XLSX</b> — <c>DocumentFormat.OpenXml</c> SDK.</item>
/// <item><b>PDF</b> — <c>PdfPig</c> for born-digital PDFs that embed their own text streams.
/// Instant-identity-ingest amendment: PdfPig replaces the OCR path for readable PDFs, so the
/// 7-page OCR budget is reserved for scanned/image PDFs. If PdfPig extracts fewer than
/// <see cref="MinPdfChars"/> non-whitespace chars, <see cref="NativeTextExtractionResult.IsSufficient"/>
/// is <see langword="false"/> and <see cref="HybridDocumentParsingService"/> falls back to OCR.</item>
/// </list>
///
/// A Word/Excel file is never a scanned image, so a successful parse is always
/// <see cref="NativeTextExtractionResult.IsSufficient"/>, regardless of how much text it actually
/// contains — an empty document is an honest fact, not a reason to spend an OCR page on it.
/// </summary>
public sealed class NativeDocumentTextExtractor : INativeDocumentTextExtractor
{
    private const string PdfMimeType = "application/pdf";
    private const string DocxMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string XlsxMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Minimum non-whitespace character count for a PdfPig result to be considered
    /// "sufficient" for skipping OCR. Below this the PDF is likely scanned/image-only.</summary>
    private const int MinPdfChars = 50;

    /// <inheritdoc/>
    public bool CanHandle(string mimeType) => Normalize(mimeType) is PdfMimeType or DocxMimeType or XlsxMimeType;

    /// <inheritdoc/>
    public NativeTextExtractionResult Extract(string mimeType, ReadOnlyMemory<byte> content) =>
        Normalize(mimeType) switch
        {
            PdfMimeType => ExtractPdf(content),
            DocxMimeType => ExtractDocx(content),
            XlsxMimeType => ExtractXlsx(content),
            _ => throw new NotSupportedException(
                $"{nameof(NativeDocumentTextExtractor)} cannot handle mime type '{mimeType}'; " +
                $"call {nameof(CanHandle)} first."),
        };

    private static NativeTextExtractionResult ExtractPdf(ReadOnlyMemory<byte> content)
    {
        try
        {
            using var pdf = PdfDocument.Open(content.ToArray());
            var pages = new List<DocumentPageText>();

            foreach (var page in pdf.GetPages())
            {
                var text = page.Text ?? string.Empty;
                pages.Add(new DocumentPageText(page.Number, text));
            }

            // IsSufficient = true only when there is enough embedded text to skip OCR.
            var totalNonWs = pages.Sum(p => p.Text.Count(c => !char.IsWhiteSpace(c)));
            return new NativeTextExtractionResult(pages, IsSufficient: totalNonWs >= MinPdfChars);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Untrusted bytes: any parse failure degrades to "not sufficient" (HybridDocumentParsingService
            // will fall through to OCR).
            return new NativeTextExtractionResult([], IsSufficient: false);
        }
    }

    /// <summary>Strips a trailing <c>; charset=...</c>-style parameter and normalizes case, so a
    /// caller-declared mime type like <c>"Application/PDF; charset=binary"</c> still matches.</summary>
    private static string Normalize(string mimeType) =>
        mimeType.Split(';')[0].Trim().ToLowerInvariant();

    private static NativeTextExtractionResult ExtractDocx(ReadOnlyMemory<byte> content)
    {
        try
        {
            using var stream = new MemoryStream(content.ToArray());
            using var document = WordprocessingDocument.Open(stream, isEditable: false);

            var text = document.MainDocumentPart?.Document?.Body?.InnerText ?? string.Empty;

            return new NativeTextExtractionResult([new DocumentPageText(1, text)], IsSufficient: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Untrusted, caller-supplied bytes claiming to be a docx: any parse failure (corrupt
            // zip, not actually an OOXML package, ...) degrades to "insufficient" rather than
            // crashing the pipeline on one bad upload.
            return new NativeTextExtractionResult([], IsSufficient: false);
        }
    }

    private static NativeTextExtractionResult ExtractXlsx(ReadOnlyMemory<byte> content)
    {
        try
        {
            using var stream = new MemoryStream(content.ToArray());
            using var document = SpreadsheetDocument.Open(stream, isEditable: false);

            var workbookPart = document.WorkbookPart;
            var sheets = workbookPart?.Workbook?.Sheets?.Elements<Sheet>() ?? [];
            var sharedStrings = workbookPart?.SharedStringTablePart?.SharedStringTable;

            var pages = new List<DocumentPageText>();

            foreach (var sheet in sheets)
            {
                if (workbookPart is null
                    || sheet.Id?.Value is not { } relationshipId
                    || workbookPart.GetPartById(relationshipId) is not WorksheetPart worksheetPart
                    || worksheetPart.Worksheet is not { } worksheet)
                {
                    continue;
                }

                var builder = new StringBuilder();

                foreach (var row in worksheet.Descendants<Row>())
                {
                    foreach (var cell in row.Elements<Cell>())
                    {
                        var cellText = ReadCellText(cell, sharedStrings);
                        if (!string.IsNullOrEmpty(cellText))
                        {
                            builder.Append(cellText).Append(' ');
                        }
                    }
                }

                // One "page" per worksheet — XLSX has no native page concept without a rendering
                // engine (no fixed print layout is guaranteed), but "sheet" is the closest,
                // honestly-meaningful unit to cite as evidence (spec §7.3 source page/section).
                pages.Add(new DocumentPageText(pages.Count + 1, builder.ToString().TrimEnd()));
            }

            return new NativeTextExtractionResult(pages, IsSufficient: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new NativeTextExtractionResult([], IsSufficient: false);
        }
    }

    private static string? ReadCellText(Cell cell, SharedStringTable? sharedStrings)
    {
        // Inline strings (<is><t>...</t></is>) carry their text directly on the cell, not via the
        // <v>/CellValue element the other two branches below read — a writer that has no
        // SharedStringTablePart at all (a real, common case, not just a test convenience) still
        // needs its cell text read correctly.
        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return cell.InlineString?.Text?.Text;
        }

        var rawValue = cell.CellValue?.InnerText;
        if (string.IsNullOrEmpty(rawValue))
        {
            return null;
        }

        if (cell.DataType?.Value == CellValues.SharedString
            && sharedStrings is not null
            && int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex))
        {
            return sharedStrings.Elements<SharedStringItem>().ElementAtOrDefault(sharedIndex)?.InnerText;
        }

        return rawValue;
    }
}
