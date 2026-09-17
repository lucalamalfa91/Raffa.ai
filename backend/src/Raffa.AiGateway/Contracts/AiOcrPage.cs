namespace Raffa.AiGateway.Contracts;

/// <summary>
/// One recognized page of `ocr`-role output (ADR-017 "Implications for the decomposition": "must
/// persist a page map so evidence source.page / section still resolve"). Deliberately the same
/// shape as
/// <see cref="Raffa.Documents.Contracts.Application.Extraction.DocumentPageText"/> — the gateway
/// cannot reference that domain type directly (ADR-002: this project must stay domain-agnostic),
/// so this is the gateway-side twin the caller maps 1:1 once the OCR call returns.
/// </summary>
/// <param name="PageNumber">1-based page number, matching how a human would cite "page N".</param>
/// <param name="Text">The page's recognized text.</param>
/// <param name="Words">
/// Per-word geometry (<c>prebuilt-layout</c>, ADR-017 w18) so a later consumer can build a
/// pixel-exact box for an arbitrary cited phrase on this page — each word's own recognized text
/// and pixel-space bounding polygon, in document reading order. <see langword="null"/> (never an
/// empty list — a caller only has to check one thing) when no layout geometry is available for
/// this page: the <c>prebuilt-layout</c> call failed or timed out, the page predates this wave, or
/// the model itself reported no words for it. A caller must treat a null value as "fall back to a
/// text-level highlight", never as an error (ADR-017 w18 / epic-23: "a page with no geometry ...
/// never an error").
/// </param>
public sealed record AiOcrPage(int PageNumber, string Text, IReadOnlyList<AiOcrWord>? Words = null);

/// <summary>
/// One recognized word's text and pixel-space bounding polygon (<c>prebuilt-layout</c>, ADR-017
/// w18). Not itself "the box" for a cited phrase — a phrase usually spans several words — but the
/// raw material a later consumer (the evidence-geometry task, epic-23 feature-02) unions into one:
/// find the run of words on the page whose concatenated <see cref="Text"/> matches the phrase,
/// then take the bounding rectangle of their <see cref="Polygon"/>s. Matching by word text rather
/// than by character offset on purpose: <c>prebuilt-read</c> (which builds <see cref="AiOcrPage.Text"/>)
/// and <c>prebuilt-layout</c> (which builds this list) are two independent analyze calls, each
/// over its own reconstructed <c>content</c> string — their character offsets are not guaranteed
/// to line up, so this gateway does not expose one.
/// </summary>
/// <param name="Text">The word's own recognized text (`prebuilt-layout`'s <c>content</c> field).</param>
/// <param name="Polygon">
/// The word's bounding polygon in the page's pixel coordinate space, exactly as Azure reports it —
/// see <see cref="Raffa.AiGateway.Foundry.Wire.DocumentIntelligenceWord.Polygon"/> for the wire
/// shape this is copied from.
/// </param>
public sealed record AiOcrWord(string Text, IReadOnlyList<double> Polygon);
