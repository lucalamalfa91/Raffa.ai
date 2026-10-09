using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application.Extraction;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.Shared.Parsing;

/// <summary>
/// Hybrid parse: native text first for PDF/DOCX/XLSX, Azure Document Intelligence
/// (<see cref="IAiGateway.OcrAsync"/>) for scans and images. Wave w19 / ADR-017 amendment
/// 2026-09-09 sent every PDF to OCR; when DI or the fixture scanner returned an empty page
/// map, <c>DocumentAdmissionGate</c> rejected real contracts as
/// <c>no_readable_text</c> and deleted the blob. Native pdfium text is restored, and OCR
/// empty/failure falls back to whatever native pages were recovered.
/// </summary>
public sealed class HybridDocumentParsingService(
    IAiGateway aiGateway, INativeDocumentTextExtractor nativeTextExtractor)
{
    public async Task<Result<IReadOnlyList<DocumentPageText>>> ParseAsync(
        string fileName, string mimeType, ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
    {
        if (content.IsEmpty)
        {
            return Result<IReadOnlyList<DocumentPageText>>.Failure(
                "Hybrid document parsing requires non-empty document content.");
        }

        NativeTextExtractionResult? native = null;
        if (nativeTextExtractor.CanHandle(mimeType))
        {
            native = nativeTextExtractor.Extract(mimeType, content);
            if (native.IsSufficient)
            {
                return Result<IReadOnlyList<DocumentPageText>>.Success(native.Pages);
            }
        }

        // `native.Pages.Count > 0` is pdfium's own page count for a PDF it could actually open —
        // the same count the "sufficient" check above just used, so handing it to the gateway as
        // AiOcrRequest.KnownPageCount is a trustworthy pre-check, not a guess. A zero count means
        // pdfium could not open the file at all (ExtractPdf's catch-and-return-empty path) rather
        // than a real zero-page PDF, so it is not trustworthy and must not be passed as known —
        // the gateway falls back to finding the real count itself.
        var knownPageCount = native is { Pages.Count: > 0 } ? native.Pages.Count : (int?)null;
        var ocrResult = await aiGateway
            .OcrAsync(new AiOcrRequest(fileName, mimeType, content, knownPageCount), cancellationToken)
            .ConfigureAwait(false);

        if (ocrResult.IsSuccess && ocrResult.Value.Pages.Count > 0)
        {
            IReadOnlyList<DocumentPageText> ocrPages = ocrResult.Value.Pages
                .Select(page => new DocumentPageText(page.PageNumber, page.Text))
                .ToList();

            if (native?.Pages is { Count: > 0 } nativePages
                && DocumentPageText.CountReadableChars(nativePages)
                   > DocumentPageText.CountReadableChars(ocrPages))
            {
                // Fixture placeholder / empty DI span map on a text PDF: keep the richer native layer.
                return Result<IReadOnlyList<DocumentPageText>>.Success(nativePages);
            }

            return Result<IReadOnlyList<DocumentPageText>>.Success(ocrPages);
        }

        if (native?.Pages is { Count: > 0 })
        {
            return Result<IReadOnlyList<DocumentPageText>>.Success(native.Pages);
        }

        if (ocrResult.IsFailure)
        {
            return Result<IReadOnlyList<DocumentPageText>>.Failure(ocrResult.Error);
        }

        return Result<IReadOnlyList<DocumentPageText>>.Failure(
            "OCR completed but produced no pages.");
    }
}
