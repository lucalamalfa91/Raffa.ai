using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Walks a WordprocessingML package into page-mapped text the extract and preview pipelines
/// share. <see cref="Body.InnerText"/> concatenates every run without paragraph, cell or page
/// separators, which is why a readable order form arrived at staged extraction as one mashed
/// string (supplier/dates unrecoverable; contract status left at the bootstrap
/// <c>processing</c> placeholder). This reader:
/// <list type="bullet">
/// <item>keeps paragraph breaks,</item>
/// <item>emits table rows as <c>cell | cell</c> lines,</item>
/// <item>splits pages on Word's own <see cref="LastRenderedPageBreak"/> and explicit page
/// breaks,</item>
/// <item>includes headers/footers (party names often live there),</item>
/// <item>skips deleted revisions (<c>w:delText</c> is never a <see cref="Text"/>).</item>
/// </list>
/// A successful open is always sufficient — a born-digital DOCX is never an OCR-scan.
/// </summary>
internal sealed class DocxPageTextReader
{
    private readonly StringBuilder _current = new();
    private readonly List<string> _pages = [];

    public static IReadOnlyList<string> ReadPages(WordprocessingDocument document)
    {
        var main = document.MainDocumentPart;
        if (main?.Document?.Body is null)
        {
            return [string.Empty];
        }

        var header = ReadAuxiliaryParts(main.HeaderParts.Select(part => part.Header).OfType<OpenXmlElement>());
        var footer = ReadAuxiliaryParts(main.FooterParts.Select(part => part.Footer).OfType<OpenXmlElement>());

        var reader = new DocxPageTextReader();
        reader.Walk(main.Document.Body);
        var pages = reader.Finish();

        if (header.Length == 0 && footer.Length == 0)
        {
            return pages;
        }

        return pages
            .Select(page => JoinNonEmpty(header, page, footer))
            .ToList();
    }

    private static string ReadAuxiliaryParts(IEnumerable<OpenXmlElement> parts)
    {
        var reader = new DocxPageTextReader();
        foreach (var part in parts)
        {
            reader.Walk(part);
        }

        return string.Join("\n", reader.Finish().Where(static text => text.Length > 0));
    }

    private static string JoinNonEmpty(params string[] parts) =>
        string.Join("\n", parts.Where(static part => part.Length > 0));

    private void Walk(OpenXmlElement element)
    {
        switch (element)
        {
            case Paragraph paragraph:
                WriteParagraph(paragraph);
                return;
            case Table table:
                WriteTable(table);
                return;
            case TextBoxContent box:
                foreach (var child in box.Elements())
                {
                    Walk(child);
                }

                return;
            case AlternateContent alternate:
                // Word letterheads often live in the mc:Choice drawing, not the fallback.
                var choice = alternate.GetFirstChild<AlternateContentChoice>();
                if (choice is not null)
                {
                    Walk(choice);
                    return;
                }

                foreach (var child in alternate.Elements())
                {
                    Walk(child);
                }

                return;
            default:
                foreach (var child in element.Elements())
                {
                    Walk(child);
                }

                return;
        }
    }

    private void WriteParagraph(Paragraph paragraph)
    {
        foreach (var child in paragraph.ChildElements)
        {
            WriteInline(child);
        }

        foreach (var box in paragraph.Descendants<TextBoxContent>())
        {
            Walk(box);
        }

        _current.Append('\n');
    }

    private void WriteInline(OpenXmlElement element)
    {
        switch (element)
        {
            case Deleted or DeletedRun:
                return;
            case Run run:
                WriteRun(run);
                return;
            default:
                foreach (var child in element.ChildElements)
                {
                    WriteInline(child);
                }

                return;
        }
    }

    private void WriteRun(Run run)
    {
        foreach (var child in run.ChildElements)
        {
            switch (child)
            {
                case LastRenderedPageBreak:
                    BreakPage();
                    break;
                case Break br when br.Type?.Value == BreakValues.Page:
                    BreakPage();
                    break;
                case Break or CarriageReturn:
                    _current.Append('\n');
                    break;
                case Text text:
                    _current.Append(text.Text);
                    break;
                case TabChar:
                    _current.Append('\t');
                    break;
            }
        }
    }

    private void WriteTable(Table table)
    {
        List<string>? headers = null;
        foreach (var row in table.Elements<TableRow>())
        {
            var cells = new List<string>();
            foreach (var cell in row.Elements<TableCell>())
            {
                var nested = new DocxPageTextReader();
                foreach (var child in cell.Elements())
                {
                    if (child is TableCellProperties)
                    {
                        continue;
                    }

                    nested.Walk(child);
                }

                cells.Add(string.Join(' ', nested.Finish()).Replace('\n', ' ').Trim());
            }

            if (OfficeTableText.AppendRow(_current, cells, headers))
            {
                headers = cells;
            }
        }

        _current.Append('\n');
    }

    private void BreakPage()
    {
        _pages.Add(_current.ToString().Trim());
        _current.Clear();
    }

    private IReadOnlyList<string> Finish()
    {
        var tail = _current.ToString().Trim();
        if (tail.Length > 0 || _pages.Count == 0)
        {
            _pages.Add(tail);
        }

        return _pages;
    }
}
