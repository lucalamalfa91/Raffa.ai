namespace Raffa.Documents.Contracts.Application.Preview;

/// <summary>
/// pdfium (<see cref="Docnet.Core.DocLib"/>) is process-global and not safe for concurrent
/// <c>GetDocReader</c>. Preview raster and native PDF text extract share this lock so a Worker
/// with <c>MaxConcurrentCalls = 4</c> cannot overlap native calls (the same 0xC0000005 risk
/// <see cref="PdfPageDocumentPreviewRenderer"/> already documents).
/// </summary>
internal static class PdfiumGate
{
    internal static readonly object Sync = new();
}
