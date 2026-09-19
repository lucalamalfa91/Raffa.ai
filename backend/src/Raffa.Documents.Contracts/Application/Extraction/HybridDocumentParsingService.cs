using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Hybrid parse: native text first for PDF/DOCX/XLSX, Azure Document Intelligence
/// (<see cref="IAiGateway.OcrAsync"/>) for scans and images. Wave w19 / ADR-017 amendment
/// 2026-09-09 sent every PDF to OCR; when DI or the fixture scanner returned an empty page
/// map, <see cref="DocumentAdmissionGate"/> rejected real contracts as
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

        var ocrResult = await aiGateway
            .OcrAsync(new AiOcrRequest(fileName, mimeType, content), cancellationToken)
            .ConfigureAwait(false);

        if (ocrResult.IsSuccess && ocrResult.Value.Pages.Count > 0)
        {
            IReadOnlyList<DocumentPageText> ocrPages = ocrResult.Value.Pages
                .Select(page => new DocumentPageText(page.PageNumber, page.Text))
                .ToList();

            if (native?.Pages is { Count: > 0 } nativePages
                && DocumentAdmissionGate.CountReadableChars(nativePages)
                   > DocumentAdmissionGate.CountReadableChars(ocrPages))
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
