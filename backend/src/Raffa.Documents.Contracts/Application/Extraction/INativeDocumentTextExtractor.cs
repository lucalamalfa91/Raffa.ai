namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// Attempts native (non-AI) text extraction for a born-digital file. Task E02/F01/US02/T02
/// (hybrid-ocr): ADR-017 "native text extraction... keeps born-digital cost low" — this is not AI
/// I/O (no Foundry/Document Intelligence call, no billing, no no-training/audit concerns), so it
/// must never be routed through <see cref="Raffa.AiGateway.IAiGateway"/>, and it is why this
/// interface lives in this module rather than the gateway.
///
/// An abstraction — rather than <see cref="HybridDocumentParsingService"/> calling
/// <see cref="NativeDocumentTextExtractor"/> directly — for the same reason
/// <see cref="Raffa.AiGateway.IAiGateway"/> is an interface: it lets the hybrid routing/
/// page-budget logic be unit-tested against a scripted fake instead of real PDF/DOCX/XLSX bytes,
/// mirroring how <see cref="StagedExtractionService"/>'s own tests script <c>IAiGateway</c> rather
/// than depending on a live model.
/// </summary>
public interface INativeDocumentTextExtractor
{
    /// <summary>
    /// Whether this extractor recognizes <paramref name="mimeType"/> at all. An unrecognized mime
    /// type (for example a raw image format, or anything outside spec §4's PDF/DOCX/XLSX upload
    /// set) is a different situation from "recognized but insufficient" — it always routes
    /// straight to OCR, never attempts a native parse that was never going to apply.
    /// </summary>
    bool CanHandle(string mimeType);

    /// <summary>
    /// Extracts whatever native text <paramref name="content"/> contains, page by page, treating
    /// it as <paramref name="mimeType"/> (the caller has always already confirmed
    /// <see cref="CanHandle"/> for the same value — passed again here, rather than re-detected,
    /// so this method never has to guess a format <see cref="CanHandle"/> already settled).
    /// Implementations may throw for a <paramref name="mimeType"/> that was never a
    /// <see cref="CanHandle"/> match.
    /// </summary>
    NativeTextExtractionResult Extract(string mimeType, ReadOnlyMemory<byte> content);
}

/// <summary>
/// Result of one <see cref="INativeDocumentTextExtractor.Extract"/> call.
/// <see cref="IsSufficient"/> is this extractor's own honest signal that the result is trustworthy
/// enough to skip OCR (ADR-017: "native text... with sufficient extractable text") —
/// <see langword="false"/> covers both "this file could not be parsed at all" (corrupt/malformed
/// content despite a recognized mime type) and "parsed, but too little text to be a real
/// born-digital file" (for example a scanned PDF with an empty text layer). Either way, the caller
/// (<see cref="HybridDocumentParsingService"/>) falls back to the `ocr` gateway role for the full
/// document rather than trusting a near-empty native result — ADR-017's own worry ("OCR is the
/// backstop, not a replacement") cuts both ways: never skip OCR when native parsing quietly failed.
/// </summary>
/// <param name="Pages">Native pages extracted, in document order. May be empty when <see cref="IsSufficient"/> is <see langword="false"/>.</param>
/// <param name="IsSufficient">Whether <see cref="Pages"/> is trustworthy enough to use as-is, skipping OCR.</param>
public sealed record NativeTextExtractionResult(
    IReadOnlyList<DocumentPageText> Pages,
    bool IsSufficient);
