using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Task E02/F01/US02/T02's own coding objective: "Add hybrid OCR pre-pass behind gateway (full
/// doc, no 2-page cap)". Produces the page-mapped <see cref="DocumentPageText"/> list
/// <see cref="StagedExtractionService"/> already depends on as its own input seam (see that
/// service's own AC-3 remarks: "this service does not read document bytes at all... that parsing
/// step is task T02's own coding objective") and ADR-017's hybrid parse as amended on 2026-09-09:
/// native text for the Office formats (<see cref="NativeDocumentTextExtractor"/>: DOCX/XLSX), Azure
/// AI Document Intelligence (behind <see cref="Raffa.AiGateway.IAiGateway.OcrAsync"/>) for every
/// PDF and image — full document, always, no 2-page cap.
///
/// Deliberately takes bytes/mime-type directly rather than a <see cref="Domain.Document"/> id +
/// <see cref="Raffa.SharedKernel.Storage.IDocumentStorage"/> lookup, mirroring
/// <see cref="StagedExtractionService"/>'s own choice to stay storage/DB-free where it can: this
/// keeps the hybrid routing/budget logic a small, pure, easily-unit-tested unit. Wiring "load the
/// Document row, read its bytes from storage, call this, then call StagedExtractionService" into
/// an HTTP endpoint or the Worker's queue dispatch is later-task scope — nothing in this wave
/// dispatches the `Classification` job's queue message to a domain handler yet (see
/// <c>Raffa.Worker.Queue.QueueConsumerHostedService</c>'s own doc comment), so there is no
/// caller to wire this into today.
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

        if (nativeTextExtractor.CanHandle(mimeType))
        {
            var native = nativeTextExtractor.Extract(mimeType, content);

            if (native.IsSufficient)
            {
                // ADR-017: "native text extraction... keeps born-digital cost low" — the `ocr`
                // gateway role is never called for a document the native extractor already
                // trusts, so a born-digital contract never carries a Document Intelligence
                // per-page charge.
                return Result<IReadOnlyList<DocumentPageText>>.Success(native.Pages);
            }
        }

        // Scanned/image/low-text/unrecognized format: full document through the `ocr` gateway
        // role (ADR-017). Always the complete, unmodified content — no 2-page cap, and no
        // pre-slicing here that would silently reintroduce one. The gateway itself enforces the
        // configured page-budget (AiGatewayOcrOptions) and fails visibly rather than truncating.
        var ocrResult = await aiGateway
            .OcrAsync(new AiOcrRequest(fileName, mimeType, content), cancellationToken)
            .ConfigureAwait(false);

        if (ocrResult.IsFailure)
        {
            return Result<IReadOnlyList<DocumentPageText>>.Failure(ocrResult.Error);
        }

        if (ocrResult.Value.Pages.Count == 0)
        {
            // Honest failure, not a silently empty success: StagedExtractionService's own
            // "Staged extraction requires at least one page" only fires if it is ever called —
            // failing here, one layer earlier, gives a clearer error for this exact cause.
            return Result<IReadOnlyList<DocumentPageText>>.Failure(
                "OCR completed but produced no pages.");
        }

        IReadOnlyList<DocumentPageText> pages = ocrResult.Value.Pages
            .Select(page => new DocumentPageText(page.PageNumber, page.Text))
            .ToList();

        return Result<IReadOnlyList<DocumentPageText>>.Success(pages);
    }
}
