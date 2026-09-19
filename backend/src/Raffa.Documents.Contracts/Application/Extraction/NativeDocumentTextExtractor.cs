using Docnet.Core;
using Docnet.Core.Models;
using DocumentFormat.OpenXml.Packaging;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Application.Preview;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Native (non-AI) text for born-digital uploads. DOCX/XLSX are read via
/// <c>DocumentFormat.OpenXml</c>; PDF selectable text is read via Docnet.Core/pdfium (the same
/// library that rasterises preview pages). A Word/Excel parse is always
/// <see cref="NativeTextExtractionResult.IsSufficient"/> — those formats are never scans.
/// A PDF is sufficient only when pdfium recovered a real text layer (aligned with
/// <see cref="DocumentAdmissionOptions.MinReadableChars"/>); an image-only scan stays
/// insufficient so <see cref="HybridDocumentParsingService"/> can OCR it.
///
/// Wave w19 / ADR-017 amendment 2026-09-09 (commit <c>380cc2a1</c>) removed PDF from this
/// type. Real contracts then went to Document Intelligence or the fixture scanner, both of
/// which can return a near-empty page map; the admission gate treated that as
/// <c>no_readable_text</c> and deleted the blob. pdfium text restores the pre-w19 path.
/// </summary>
public sealed class NativeDocumentTextExtractor : INativeDocumentTextExtractor
{
    private const string PdfMimeType = "application/pdf";
    private const string DocxMimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    private const string XlsxMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>
    /// Same default as <see cref="DocumentAdmissionOptions.MinReadableChars"/>. A PDF whose
    /// embedded text meets this floor is a born-digital contract, not a scan.
    /// </summary>
    internal const int PdfSufficientCharFloor = 200;

    private static readonly PageDimensions TextDimensions = new(1, 1);

    /// <inheritdoc/>
    public bool CanHandle(string mimeType) => Normalize(mimeType) is PdfMimeType or DocxMimeType or XlsxMimeType;

    /// <inheritdoc/>
    public NativeTextExtractionResult Extract(string mimeType, ReadOnlyMemory<byte> content) =>
        Normalize(mimeType) switch
        {
            PdfMimeType => ExtractPdf(content),
            DocxMimeType => ExtractDocx(content),
            XlsxMimeType => ExtractXlsx(content),
            _ => throw new NotSupportedException(
                $"{nameof(NativeDocumentTextExtractor)} cannot handle mime type '{mimeType}'; " +
                $"call {nameof(CanHandle)} first."),
        };

    /// <summary>Strips a trailing <c>; charset=...</c>-style parameter and normalizes case, so a
    /// caller-declared mime type like <c>"Application/PDF; charset=binary"</c> still matches.</summary>
    private static string Normalize(string mimeType) =>
        mimeType.Split(';')[0].Trim().ToLowerInvariant();

    private static NativeTextExtractionResult ExtractPdf(ReadOnlyMemory<byte> content)
    {
        if (content.IsEmpty)
        {
            return new NativeTextExtractionResult([], IsSufficient: false);
        }

        try
        {
            List<DocumentPageText> pages;
            lock (PdfiumGate.Sync)
            {
                var library = DocLib.Instance;
                using var reader = library.GetDocReader(content.ToArray(), TextDimensions);
                var pageCount = reader.GetPageCount();
                if (pageCount < 1)
                {
                    return new NativeTextExtractionResult([], IsSufficient: false);
                }

                pages = new List<DocumentPageText>(pageCount);
                for (var index = 0; index < pageCount; index++)
                {
                    using var page = reader.GetPageReader(index);
                    pages.Add(new DocumentPageText(index + 1, page.GetText() ?? string.Empty));
                }
            }

            var sufficient = DocumentAdmissionGate.CountReadableChars(pages) >= PdfSufficientCharFloor;
            return new NativeTextExtractionResult(pages, sufficient);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new NativeTextExtractionResult([], IsSufficient: false);
        }
    }

    private static NativeTextExtractionResult ExtractDocx(ReadOnlyMemory<byte> content)
    {
        try
        {
            using var stream = new MemoryStream(content.ToArray());
            using var document = WordprocessingDocument.Open(stream, isEditable: false);

            // Structured walk — never Body.InnerText. Adjacent paragraphs/cells would otherwise
            // concatenate ("SupplierIBM Corporation") and staged extraction would miss fields.
            var pages = DocxPageTextReader.ReadPages(document)
                .Select((text, index) => new DocumentPageText(index + 1, text))
                .ToList();

            return new NativeTextExtractionResult(pages, IsSufficient: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Untrusted, caller-supplied bytes claiming to be a docx: any parse failure (corrupt
            // zip, not actually an OOXML package, ...) degrades to "insufficient" rather than
            // crashing the pipeline on one bad upload.
            return new NativeTextExtractionResult([], IsSufficient: false);
        }
    }

    private static NativeTextExtractionResult ExtractXlsx(ReadOnlyMemory<byte> content)
    {
        try
        {
            using var stream = new MemoryStream(content.ToArray());
            using var document = SpreadsheetDocument.Open(stream, isEditable: false);

            var pages = XlsxPageTextReader.ReadPages(document)
                .Select((text, index) => new DocumentPageText(index + 1, text))
                .ToList();

            return new NativeTextExtractionResult(pages, IsSufficient: true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new NativeTextExtractionResult([], IsSufficient: false);
        }
    }
}
