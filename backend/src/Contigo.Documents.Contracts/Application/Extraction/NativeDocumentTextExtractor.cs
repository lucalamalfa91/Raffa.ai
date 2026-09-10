using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Contigo.Documents.Contracts.Application.Extraction;

/// <summary>
/// Real (non-AI) text extraction for the two born-digital Office formats spec §4 accepts — DOCX
/// and XLSX — read via the real <c>DocumentFormat.OpenXml</c> SDK (OOXML is just XML in a zip
/// archive — not a provider SDK, so this stays clear of ADR-002's "no provider SDK in domain
/// code" rule the same way <c>EFCore.NamingConventions</c>/<c>Npgsql</c> already do for this
/// project). A Word/Excel file is never a scanned image, so a successful parse is always
/// <see cref="NativeTextExtractionResult.IsSufficient"/>, regardless of how much text it actually
/// contains — an empty document is an honest fact, not a reason to spend an OCR page on it.
///
/// <b>PDF is no longer handled here</b> (ADR-017 amendment 2026-09-09). The previous hand-written
/// content-stream scanner could not read real supplier PDFs (CID fonts, hex strings, object
/// streams) and silently routed them to a fixture that produced no text; every PDF and image now
/// goes through the `ocr` gateway role (Azure AI Document Intelligence Read), which returns a
/// born-digital PDF's embedded text with a uniform page map at the same per-page price. The
/// scanner survives only as <c>Contigo.AiGateway.Fixtures.FixturePdfTextScanner</c>, the fixture
/// gateway's provider-free reader for the repo's hand-built test PDFs.
/// </summary>
public sealed class NativeDocumentTextExtractor : INativeDocumentTextExtractor
{
    private const string DocxMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string XlsxMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <inheritdoc/>
    public bool CanHandle(string mimeType) => Normalize(mimeType) is DocxMimeType or XlsxMimeType;

    /// <inheritdoc/>
    public NativeTextExtractionResult Extract(string mimeType, ReadOnlyMemory<byte> content) =>
        Normalize(mimeType) switch
        {
            DocxMimeType => ExtractDocx(content),
            XlsxMimeType => ExtractXlsx(content),
            _ => throw new NotSupportedException(
                $"{nameof(NativeDocumentTextExtractor)} cannot handle mime type '{mimeType}'; " +
                $"call {nameof(CanHandle)} first."),
        };

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
