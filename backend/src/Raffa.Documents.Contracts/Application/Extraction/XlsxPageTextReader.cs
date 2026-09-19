using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Walks a SpreadsheetML workbook into page-mapped text. One page per worksheet that has cells,
/// prefixed with the sheet name. Dates stored as Excel serials are written as calendar dates;
/// two-column and header-paired rows use <see cref="OfficeTableText"/> so supplier/dates are
/// recoverable the same way as a DOCX table.
/// </summary>
internal static class XlsxPageTextReader
{
    public static IReadOnlyList<string> ReadPages(SpreadsheetDocument document)
    {
        var workbookPart = document.WorkbookPart;
        var sheets = workbookPart?.Workbook?.Sheets?.Elements<Sheet>() ?? [];
        var sharedStrings = workbookPart?.SharedStringTablePart?.SharedStringTable;
        var formats = XlsxNumberFormats.Load(workbookPart);

        var pages = new List<string>();
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
            var sheetName = sheet.Name?.Value;
            if (!string.IsNullOrWhiteSpace(sheetName))
            {
                builder.Append("Sheet ").Append(sheetName).Append('\n');
            }

            List<string>? headers = null;
            foreach (var row in worksheet.Descendants<Row>())
            {
                var cells = ReadRow(row, sharedStrings, formats);
                if (cells.Count == 0)
                {
                    continue;
                }

                if (OfficeTableText.AppendRow(builder, cells, headers))
                {
                    headers = cells;
                }
            }

            var text = builder.ToString().TrimEnd();
            if (text.Length > 0)
            {
                pages.Add(text);
            }
        }

        return pages.Count > 0 ? pages : [string.Empty];
    }

    private static List<string> ReadRow(Row row, SharedStringTable? sharedStrings, XlsxNumberFormats formats)
    {
        var byIndex = new SortedDictionary<int, string>();
        foreach (var cell in row.Elements<Cell>())
        {
            var cellText = ReadCellText(cell, sharedStrings, formats);
            if (string.IsNullOrEmpty(cellText))
            {
                continue;
            }

            byIndex[ColumnIndex(cell.CellReference?.Value)] = cellText;
        }

        if (byIndex.Count == 0)
        {
            return [];
        }

        var max = byIndex.Keys.Max();
        var cells = new List<string>(max + 1);
        for (var index = 0; index <= max; index++)
        {
            cells.Add(byIndex.TryGetValue(index, out var value) ? value : string.Empty);
        }

        return cells;
    }

    private static string? ReadCellText(Cell cell, SharedStringTable? sharedStrings, XlsxNumberFormats formats)
    {
        if (cell.DataType?.Value == CellValues.InlineString)
        {
            return ReadRichText(cell.InlineString);
        }

        if (cell.DataType?.Value == CellValues.SharedString
            && sharedStrings is not null
            && int.TryParse(cell.CellValue?.InnerText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sharedIndex))
        {
            return ReadRichText(sharedStrings.Elements<SharedStringItem>().ElementAtOrDefault(sharedIndex));
        }

        var rawValue = cell.CellValue?.InnerText;
        if (string.IsNullOrEmpty(rawValue))
        {
            return null;
        }

        if (formats.IsDate(cell)
            && double.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
            && serial >= 0)
        {
            try
            {
                return DateTime.FromOADate(serial).ToString("d MMMM yyyy", CultureInfo.InvariantCulture);
            }
            catch (ArgumentException)
            {
                return rawValue;
            }
        }

        return rawValue;
    }

    /// <summary>Joins every <see cref="Text"/> run so a shared string split across runs stays
    /// spaced the way the author wrote it, instead of <c>InnerText</c> concatenating them.</summary>
    private static string? ReadRichText(OpenXmlElement? item)
    {
        if (item is null)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var text in item.Descendants<Text>())
        {
            builder.Append(text.Text);
        }

        return builder.Length > 0 ? builder.ToString() : item.InnerText;
    }

    private static int ColumnIndex(string? cellReference)
    {
        if (string.IsNullOrEmpty(cellReference))
        {
            return 0;
        }

        var index = 0;
        foreach (var character in cellReference)
        {
            if (character is < 'A' or > 'Z' and (< 'a' or > 'z'))
            {
                break;
            }

            index = (index * 26) + (char.ToUpperInvariant(character) - 'A' + 1);
        }

        return Math.Max(0, index - 1);
    }

    private sealed class XlsxNumberFormats
    {
        private readonly Dictionary<int, (uint Id, string? Code)> _byStyle = [];

        public static XlsxNumberFormats Load(WorkbookPart? workbookPart)
        {
            var loaded = new XlsxNumberFormats();
            var styles = workbookPart?.WorkbookStylesPart?.Stylesheet;
            if (styles is null)
            {
                return loaded;
            }

            var custom = new Dictionary<uint, string>();
            if (styles.NumberingFormats is not null)
            {
                foreach (var format in styles.NumberingFormats.Elements<NumberingFormat>())
                {
                    if (format.NumberFormatId?.Value is { } id && format.FormatCode?.Value is { } code)
                    {
                        custom[id] = code;
                    }
                }
            }

            var cellFormats = styles.CellFormats?.Elements<CellFormat>().ToList() ?? [];
            for (var index = 0; index < cellFormats.Count; index++)
            {
                var id = cellFormats[index].NumberFormatId?.Value ?? 0;
                custom.TryGetValue(id, out var code);
                loaded._byStyle[index] = (id, code);
            }

            return loaded;
        }

        public bool IsDate(Cell cell)
        {
            if (cell.StyleIndex?.Value is not uint style)
            {
                return false;
            }

            if (!_byStyle.TryGetValue((int)style, out var format))
            {
                return false;
            }

            return LooksLikeDateFormat(format.Id, format.Code);
        }

        private static bool LooksLikeDateFormat(uint id, string? code)
        {
            if (id is (>= 14 and <= 17) or 22 or (>= 27 and <= 36) or (>= 50 and <= 58))
            {
                return true;
            }

            if (string.IsNullOrEmpty(code))
            {
                return false;
            }

            var stripped = Regex.Replace(code, @"\[[^\]]+\]|""[^""]*""", string.Empty);
            return stripped.Contains('y', StringComparison.OrdinalIgnoreCase)
                || (stripped.Contains('d', StringComparison.OrdinalIgnoreCase)
                    && stripped.Contains('m', StringComparison.OrdinalIgnoreCase));
        }
    }
}
