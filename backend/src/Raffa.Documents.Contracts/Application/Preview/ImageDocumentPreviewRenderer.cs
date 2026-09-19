using Raffa.Documents.Contracts.Application.Admission;

namespace Raffa.Documents.Contracts.Application.Preview;

/// <summary>
/// PNG and JPEG uploads are already the page: return the original bytes so the viewer overlay
/// shows the scan, not the FILE placeholder card. JPEG is served as JPEG (the preview endpoint
/// sniffs the signature); PNG stays PNG.
/// </summary>
internal sealed class ImageDocumentPreviewRenderer : IDocumentPreviewRenderer
{
    /// <inheritdoc/>
    public byte[]? Render(string fileName, string mimeType, ReadOnlyMemory<byte> content, int page = 1)
    {
        if (page != 1 || content.IsEmpty || !CanLiveRasterize(mimeType))
        {
            return null;
        }

        return content.ToArray();
    }

    internal static bool CanLiveRasterize(string? mimeType) =>
        string.Equals(mimeType, DocumentFormatSniffer.PngMimeType, StringComparison.OrdinalIgnoreCase)
        || string.Equals(mimeType, DocumentFormatSniffer.JpegMimeType, StringComparison.OrdinalIgnoreCase);
}
