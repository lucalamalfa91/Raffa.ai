using Contigo.Documents.Contracts.Application.Admission;

namespace Contigo.Documents.Contracts.Application.Preview;

/// <summary>
/// Renders the first page of an uploaded document to a PNG (task E13/F04/US01/T02,
/// <c>inputs/requirements.md</c> R-DOC-08). A port, not an implementation detail: the default
/// <see cref="PlaceholderDocumentPreviewRenderer"/> is pure managed code and always available, and
/// a host may register a rasteriser-backed renderer (pdfium/Skia — a provider SDK, so it belongs
/// in <c>Contigo.Api</c>'s infrastructure, never in this module: ADR-002) that produces a true
/// page-1 raster for PDFs and falls back to the placeholder for everything it cannot draw.
///
/// <para>
/// A renderer must never throw and never block an upload: the preview is a convenience, and
/// <see cref="DocumentPreviewService"/> treats a failure as "no preview" (the row simply has no
/// <c>PreviewPath</c>), never as an upload failure.
/// </para>
/// </summary>
public interface IDocumentPreviewRenderer
{
    /// <summary>
    /// Returns the PNG bytes to store as this document's preview, or <see langword="null"/> when
    /// this renderer has nothing to offer for <paramref name="mimeType"/> (the caller then falls
    /// back to the placeholder renderer).
    /// </summary>
    /// <param name="mimeType">The sniffed canonical MIME type (see <see cref="DocumentFormatSniffer"/>).</param>
    byte[]? Render(string fileName, string mimeType, ReadOnlyMemory<byte> content);
}
