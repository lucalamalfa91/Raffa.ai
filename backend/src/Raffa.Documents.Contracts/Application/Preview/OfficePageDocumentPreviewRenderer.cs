using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.Documents.Contracts.Application.Preview;

/// <summary>
/// Rasterises one page of a born-digital Office file (DOCX/XLSX) to PNG by painting the same
/// native page text <see cref="NativeDocumentTextExtractor"/> feeds staged extraction. Word has
/// no pdfium path — <see cref="PdfPageDocumentPreviewRenderer"/> returns <see langword="null"/>
/// and the service used to store the honest "FILE PREVIEW NOT RENDERED" card. The viewer overlay
/// still loads <c>image/png</c> from the existing preview store; this type just gives it a
/// readable page instead of the placeholder.
///
/// <para>
/// Never throws. A parse failure returns <see langword="null"/> so the service's placeholder
/// fallback still applies. No word boxes — "citation could not be restored" stays honest when
/// there is no geometry; the page itself is still readable.
/// </para>
/// </summary>
internal sealed class OfficePageDocumentPreviewRenderer : IDocumentPreviewRenderer
{
    internal const int PageWidth = 1240;
    internal const int PageHeight = 1754;

    private static readonly NativeDocumentTextExtractor Extractor = new();

    /// <inheritdoc/>
    public byte[]? Render(string fileName, string mimeType, ReadOnlyMemory<byte> content, int page = 1)
    {
        if (!Extractor.CanHandle(mimeType) || content.IsEmpty || page < 1)
        {
            return null;
        }

        try
        {
            var extracted = Extractor.Extract(mimeType, content);
            if (!extracted.IsSufficient)
            {
                return null;
            }

            var pageText = extracted.Pages.FirstOrDefault(candidate => candidate.PageNumber == page);
            if (pageText is null)
            {
                return null;
            }

            return PageTextRaster.Render(pageText.Text);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether <see cref="DocumentPreviewService.LoadAsync"/> should re-rasterise from the
    /// original bytes (same live-render posture PDFs already take) so a DOCX stored under the
    /// old placeholder card becomes readable without a reprocess.
    /// </summary>
    internal static bool CanLiveRasterize(string? mimeType) =>
        string.Equals(mimeType, DocumentFormatSniffer.DocxMimeType, StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimeType, DocumentFormatSniffer.XlsxMimeType, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Paints wrapped page text onto an A4-ish white PNG with the module's managed bitmap font.
/// No imaging SDK — same encoder <see cref="PdfPageDocumentPreviewRenderer"/> already uses.
/// </summary>
internal static class PageTextRaster
{
    private const int Margin = 56;
    private const int Scale = 2;
    private const int LinePadding = 4;
    private const int MaxHeightPages = 3;

    private static readonly Rgb Paper = new(0xFF, 0xFF, 0xFF);
    private static readonly Rgb Ink = new(0x2B, 0x2A, 0x28);

    public static byte[] Render(string text)
    {
        var charWidth = (BitmapFont5x7.GlyphWidth + 1) * Scale;
        var maxChars = Math.Max(1, (OfficePageDocumentPreviewRenderer.PageWidth - (2 * Margin)) / charWidth);
        var lines = Wrap(text ?? string.Empty, maxChars);
        var lineHeight = (BitmapFont5x7.GlyphHeight * Scale) + LinePadding;
        var needed = (2 * Margin) + (Math.Max(lines.Count, 1) * lineHeight);
        var height = Math.Clamp(needed, OfficePageDocumentPreviewRenderer.PageHeight, OfficePageDocumentPreviewRenderer.PageHeight * MaxHeightPages);

        var image = new PngImage(OfficePageDocumentPreviewRenderer.PageWidth, height, Paper);
        var y = Margin;
        foreach (var line in lines)
        {
            if (y + lineHeight > height)
            {
                break;
            }

            if (line.Length > 0)
            {
                image.DrawText(line, Margin, y, Scale, Ink);
            }

            y += lineHeight;
        }

        return image.ToPng();
    }

    private static List<string> Wrap(string text, int maxChars)
    {
        var lines = new List<string>();
        foreach (var raw in text.Replace('\r', '\n').Replace('\t', ' ').Split('\n'))
        {
            if (raw.Length == 0)
            {
                lines.Add(string.Empty);
                continue;
            }

            var remaining = raw;
            while (remaining.Length > maxChars)
            {
                var split = remaining.LastIndexOf(' ', maxChars);
                if (split < maxChars / 2)
                {
                    split = maxChars;
                }

                lines.Add(remaining[..split].TrimEnd());
                remaining = remaining[split..].TrimStart();
            }

            lines.Add(remaining);
        }

        return lines;
    }
}
