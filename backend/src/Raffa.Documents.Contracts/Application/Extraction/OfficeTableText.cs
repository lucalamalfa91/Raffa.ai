using System.Text;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Shared DOCX/XLSX table serialization so staged extraction sees the same labelled facts a
/// reviewer reads. Two-column rows become <c>Label: Value</c> (the shape
/// <c>FixtureContractFactExtractor</c> and the extract prompt already understand). Multi-column
/// rows keep a <c>cell | cell</c> overview and, once a header row is known, also emit
/// <c>Header: Value</c> pairs so a spreadsheet like <c>Supplier | Start date | End date</c>
/// still yields recoverable fields.
/// </summary>
internal static class OfficeTableText
{
    /// <summary>
    /// Appends one row. Returns <see langword="true"/> when <paramref name="cells"/> should be
    /// remembered as the header row for later pairing (first wide row only).
    /// </summary>
    public static bool AppendRow(StringBuilder builder, IReadOnlyList<string> cells, IReadOnlyList<string>? headers)
    {
        var filled = cells.Where(static cell => cell.Length > 0).ToList();
        if (filled.Count == 0)
        {
            return false;
        }

        if (filled.Count == 2 && (headers is null || headers.Count < 3))
        {
            builder.Append(filled[0]).Append(": ").Append(filled[1]).Append('\n');
            return false;
        }

        builder.Append(string.Join(" | ", filled)).Append('\n');

        if (headers is { Count: >= 3 } && headers.Count == cells.Count)
        {
            for (var index = 0; index < cells.Count; index++)
            {
                if (headers[index].Length == 0
                    || cells[index].Length == 0
                    || string.Equals(headers[index], cells[index], StringComparison.Ordinal))
                {
                    continue;
                }

                builder.Append(headers[index]).Append(": ").Append(cells[index]).Append('\n');
            }
        }

        return headers is null && cells.Count >= 3;
    }
}
