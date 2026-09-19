using Docnet.Core;
using Docnet.Core.Converters;
using Docnet.Core.Models;
using Raffa.Documents.Contracts.Application.Admission;

namespace Raffa.Documents.Contracts.Application.Preview;

/// <summary>
/// Real PDF page rasteriser using Docnet.Core (task E22/F02/US01/T01, ADR-029 clause 3).
///
/// <para>
/// <b>Package (OQ-w17-sa-04):</b> Docnet.Core (MIT), wrapping pdfium (BSD-3-Clause). The bundled
/// native binary is statically linked — <b>Linux native-dependency list: empty</b> — so no
/// Dockerfile layer is owed in <c>Raffa.Worker/Dockerfile</c>
/// (see <c>Raffa.Documents.Contracts.csproj</c> for the full OQ binding record).
/// </para>
///
/// <para>
/// <b>One page at a time.</b> Each <see cref="Render"/> call renders exactly one page and returns
/// its PNG bytes. <see cref="DocumentPreviewService"/> calls this once per page, stores the result,
/// and disposes of it before the next call — keeping the in-memory bitmap to at most one A4 frame
/// at 150 DPI (≈ 8.4 MB BGRA) regardless of document length (ADR-005 w17 §19/§23, ADR-029
/// clause 4). At 0.25 vCPU / 0.5 GiB shared between API and Worker and
/// <c>MaxConcurrentCalls = 4</c> per replica, a set-at-once render would OOM the replica and burn
/// deliveries against <c>max_delivery_count = 8</c> — one-at-a-time is not an optimisation, it is
/// a correctness requirement.
/// </para>
///
/// <para>
/// <b>Never throws.</b> A malformed page or a missing font is caught and expressed as a
/// <see langword="null"/> return. The caller's placeholder fallback in
/// <see cref="DocumentPreviewService"/> absorbs it, so the document is never left without a
/// preview (ADR-029 clause 3). pdfium access violations are prevented, not caught: this type
/// never disposes <see cref="DocLib.Instance"/> and serializes native calls.
/// </para>
///
/// <para>
/// <b>Non-PDF pass-through.</b> <see langword="null"/> is returned immediately for non-PDF content
/// so <see cref="CompositeDocumentPreviewRenderer"/> can try the Office painter, and the service's
/// <see cref="PlaceholderDocumentPreviewRenderer"/> fallback still covers JPEG/unknown.
/// </para>
/// </summary>
internal sealed class PdfPageDocumentPreviewRenderer : IDocumentPreviewRenderer
{
    /// <summary>
    /// Target rendering dimensions at approximately 150 DPI for A4 (ADR-005 w17 §19).
    /// A4 at 150 DPI ≈ 1240 × 1754 px; PageDimensions preserves aspect ratio, so letter/legal
    /// sizes are also handled correctly within the same memory budget.
    /// </summary>
    private static readonly PageDimensions RenderDimensions = new(1240, 1754);

    /// <summary>
    /// pdfium is process-global and not safe for concurrent <c>GetDocReader</c>. The Worker
    /// runs <c>MaxConcurrentCalls = 4</c> on this singleton, and a <c>using</c> on
    /// <see cref="DocLib.Instance"/> would call <c>FPDF_DestroyLibrary</c> after every page —
    /// the next load then access-violates (0xC0000005), which a managed catch cannot absorb.
    /// </summary>
    private static readonly object PdfiumLock = new();

    /// <inheritdoc/>
    public byte[]? Render(string fileName, string mimeType, ReadOnlyMemory<byte> content, int page = 1)
    {
        if (!string.Equals(mimeType, DocumentFormatSniffer.PdfMimeType, StringComparison.OrdinalIgnoreCase))
        {
            // Non-PDF: delegate to PlaceholderDocumentPreviewRenderer in the chain.
            return null;
        }

        if (content.IsEmpty || page < 1)
        {
            return null;
        }

        lock (PdfiumLock)
        {
            try
            {
                var pdfBytes = content.ToArray();
                // DocLib.Instance is a process-wide singleton. Do not dispose it.
                var library = DocLib.Instance;
                using var docReader = library.GetDocReader(pdfBytes, RenderDimensions);

                var totalPages = docReader.GetPageCount();
                if (page > totalPages)
                {
                    return null;
                }

                // Docnet.Core uses 0-based page indices.
                using var pageReader = docReader.GetPageReader(page - 1);

                // NaiveTransparencyRemover composites pdfium's transparent background onto white
                // before we encode PNG. BgraToPng does the same as a second line of defence.
                var bgra   = pageReader.GetImage(new NaiveTransparencyRemover());
                var width  = pageReader.GetPageWidth();
                var height = pageReader.GetPageHeight();

                if (bgra is null || width <= 0 || height <= 0)
                {
                    return null;
                }

                // BGRA → PNG via the module's own pure-managed encoder.
                // The bgra array is GC'd after this call; no bitmap is held across pages.
                return PngImage.BgraToPng(width, height, bgra);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A malformed PDF, a missing native asset, or any other renderer fault must not
                // fail the document — the service's placeholder fallback absorbs it (ADR-029 clause 3).
                return null;
            }
        }
    }
}
