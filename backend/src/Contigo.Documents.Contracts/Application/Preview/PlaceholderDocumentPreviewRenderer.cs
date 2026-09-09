using Contigo.Documents.Contracts.Application.Admission;

namespace Contigo.Documents.Contracts.Application.Preview;

/// <summary>
/// The always-available preview renderer (task E13/F04/US01/T02, R-DOC-08):
///
/// <list type="bullet">
/// <item><b>PNG upload</b> — the uploaded image itself is the page-1 preview; nothing is
/// re-encoded, so what the user sees is exactly what they sent.</item>
/// <item><b>Everything else</b> (PDF, JPEG, DOCX, XLSX) — a generated placeholder PNG that names
/// the format and says plainly that it is a placeholder. The requirement asks for "an honest
/// placeholder"; a grey rectangle that pretends to be a page would not be one.</item>
/// </list>
///
/// <para>
/// <b>Known gap, stated rather than hidden:</b> a true first-page raster of a PDF (and a
/// re-encode of a JPEG into the PNG the endpoint's contract promises) needs a rasteriser —
/// pdfium or Skia — which is a native provider dependency and therefore belongs in
/// <c>Contigo.Api</c>'s infrastructure behind <see cref="IDocumentPreviewRenderer"/>, not in this
/// domain module (ADR-002). Until such a renderer is registered, a PDF's preview is this
/// placeholder. Nothing else in the pipeline changes when one is: the port, the storage path, the
/// endpoint and the stored <c>preview_path</c> all stay as they are.
/// </para>
/// </summary>
public sealed class PlaceholderDocumentPreviewRenderer : IDocumentPreviewRenderer
{
    // A4-ish portrait at a thumbnail-friendly size: big enough to read the label in the citation
    // card, small enough that the blob is a few kilobytes.
    private const int Width = 480;
    private const int Height = 640;

    private static readonly Rgb Page = new(0xFF, 0xFF, 0xFF);
    private static readonly Rgb Backdrop = new(0xF2, 0xF1, 0xEE);
    private static readonly Rgb Border = new(0xD8, 0xD5, 0xCE);
    private static readonly Rgb Ink = new(0x2B, 0x2A, 0x28);
    private static readonly Rgb Muted = new(0x8A, 0x86, 0x7E);

    public byte[]? Render(string fileName, string mimeType, ReadOnlyMemory<byte> content)
    {
        if (string.Equals(mimeType, DocumentFormatSniffer.PngMimeType, StringComparison.OrdinalIgnoreCase)
            && !content.IsEmpty)
        {
            return content.ToArray();
        }

        return RenderPlaceholder(Label(mimeType, fileName));
    }

    /// <summary>The honest placeholder, exposed so a host-side renderer can fall back to it.</summary>
    public static byte[] RenderPlaceholder(string label)
    {
        var image = new PngImage(Width, Height, Backdrop);

        const int margin = 24;
        image.Fill(margin, margin, Width - (2 * margin), Height - (2 * margin), Page);
        image.Outline(margin, margin, Width - (2 * margin), Height - (2 * margin), 2, Border);

        // Accent band, then the format label centred under it, then the "no rendered page" line.
        image.Fill(margin + 2, margin + 2, Width - (2 * margin) - 4, 10, AccentFor(label));

        const int labelScale = 8;
        var labelWidth = PngImage.MeasureText(label, labelScale);
        image.DrawText(label, (Width - labelWidth) / 2, 250, labelScale, Ink);

        const string caption = "PREVIEW NOT RENDERED";
        const int captionScale = 3;
        image.DrawText(caption, (Width - PngImage.MeasureText(caption, captionScale)) / 2, 340, captionScale, Muted);

        return image.ToPng();
    }

    private static string Label(string mimeType, string fileName) => mimeType switch
    {
        DocumentFormatSniffer.PdfMimeType => "PDF",
        DocumentFormatSniffer.DocxMimeType => "DOCX",
        DocumentFormatSniffer.XlsxMimeType => "XLSX",
        DocumentFormatSniffer.JpegMimeType => "JPG",
        DocumentFormatSniffer.PngMimeType => "PNG",
        _ => ExtensionLabel(fileName),
    };

    private static string ExtensionLabel(string fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).TrimStart('.').ToUpperInvariant();
        return extension.Length is > 0 and <= 5 ? extension : "FILE";
    }

    private static Rgb AccentFor(string label) => label switch
    {
        "PDF" => new Rgb(0xC0, 0x39, 0x2B),
        "DOCX" => new Rgb(0x2B, 0x57, 0x9A),
        "XLSX" => new Rgb(0x1E, 0x7A, 0x46),
        "JPG" or "PNG" => new Rgb(0x7A, 0x5A, 0x1E),
        _ => Muted,
    };
}
