namespace Raffa.Documents.Contracts.Application.Extraction;

/// <summary>
/// One page's worth of already-parsed document text, keyed by 1-based page number. This is the
/// input seam <see cref="StagedExtractionService"/> stages against — how the bytes behind a
/// <see cref="Domain.Document"/> become page-mapped text is deliberately out of this task's
/// scope (task E02/F01/US02/T01's own coding objective is the staged extraction pipeline, not
/// text acquisition). ADR-017 assigns that to the *hybrid pre-pass* — task E02/F01/US02/T02
/// ("hybrid-ocr"): native text for born-digital pages, Azure AI Document Intelligence for
/// scanned/image pages, both behind <c>IAiGateway</c> — which produces this same shape so the
/// pipeline below does not need to know or care which path produced a given page's text.
/// ADR-017 "Implications for the decomposition": "Every OCR call MUST ... persist a page map so
/// evidence source.page / section still resolve" — this record IS that page map, one entry per
/// page, in document order.
/// </summary>
/// <param name="PageNumber">1-based page number, matching how a human would cite "page N".</param>
/// <param name="Text">The page's text content (native-extracted or OCR'd).</param>
public sealed record DocumentPageText(int PageNumber, string Text);
