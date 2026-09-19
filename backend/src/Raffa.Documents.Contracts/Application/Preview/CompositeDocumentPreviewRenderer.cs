namespace Raffa.Documents.Contracts.Application.Preview;

/// <summary>
/// Tries pdfium (PDF), then the uploaded image (PNG/JPEG), then the Office text-page painter.
/// The first non-null page wins; a complete miss still returns <see langword="null"/> so
/// <see cref="DocumentPreviewService"/> can store the honest placeholder rather than failing
/// the document.
/// </summary>
internal sealed class CompositeDocumentPreviewRenderer(
    PdfPageDocumentPreviewRenderer pdfRenderer,
    ImageDocumentPreviewRenderer imageRenderer,
    OfficePageDocumentPreviewRenderer officeRenderer) : IDocumentPreviewRenderer
{
    public byte[]? Render(string fileName, string mimeType, ReadOnlyMemory<byte> content, int page = 1) =>
        pdfRenderer.Render(fileName, mimeType, content, page)
        ?? imageRenderer.Render(fileName, mimeType, content, page)
        ?? officeRenderer.Render(fileName, mimeType, content, page);
}
