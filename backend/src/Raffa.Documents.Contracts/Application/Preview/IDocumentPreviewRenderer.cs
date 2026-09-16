using Raffa.Documents.Contracts.Application.Admission;

namespace Raffa.Documents.Contracts.Application.Preview;

/// <summary>
/// Renders one page of an uploaded document to a PNG (task E13/F04/US01/T02, R-DOC-08; widened
/// by task E22/F02/US01/T01 to carry the <c>page</c> dimension). A port, not an implementation
/// detail: the default <see cref="PlaceholderDocumentPreviewRenderer"/> is pure managed code and
/// always available, and a host may register a rasteriser-backed renderer (pdfium/Skia) that
/// produces a true page raster for PDFs and falls back to the placeholder for everything it
/// cannot draw (ADR-002: keep native SDKs out of the domain module).
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
    /// Returns the PNG bytes for <paramref name="page"/> of this document, or
    /// <see langword="null"/> when this renderer has nothing to offer (the caller then falls back
    /// to the placeholder renderer).
    /// </summary>
    /// <param name="mimeType">The sniffed canonical MIME type (see <see cref="DocumentFormatSniffer"/>).</param>
    /// <param name="page">1-based page number. Callers pass 1 or higher; passing 0 or a negative
    /// value is a caller bug but renderers must not throw — returning <see langword="null"/> is
    /// the correct response (task E22/F02/US01/T01, ADR-029 clause 3).</param>
    byte[]? Render(string fileName, string mimeType, ReadOnlyMemory<byte> content, int page = 1);
}
