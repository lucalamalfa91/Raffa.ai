namespace Raffa.Quotes.Application.Strategy;

/// <summary>
/// One structured, queryable citation backing a <see cref="NegotiationLever.Rationale"/> (task
/// E05/F03/US01/T02, strategy-evidence; parent story us-01-negotiation-strategy AC-2 "Rationale
/// cites explicit evidence per lever"; Appendix C rule 2 "never show a consequential... fact
/// without source evidence and confidence metadata"). <see cref="NegotiationLever"/>'s own doc
/// comment already named the gap this closes: <see cref="NegotiationLever.Rationale"/> names a
/// concrete quote/line fact inline (e.g. "This line orders 250 licenses"), but until this task that
/// fact was prose only — not a field a caller could query without re-parsing the sentence.
///
/// <para>
/// Mirrors <c>Raffa.Documents.Contracts.Domain.ExtractionEvidence</c>'s own
/// <c>FieldName</c>/<c>Value</c>/<c>SourceSpan</c>/<c>SourcePage</c>/<c>Confidence</c> addressing
/// scheme (same "which field, what value, from where, how confident" shape), kept as its own
/// Quotes-local record rather than a shared type — <c>Raffa.ArchitectureTests
/// .DependencyDirectionTests</c>' allowed-reference set for <c>Raffa.Quotes</c> is exactly
/// <c>[SharedKernel, Benchmark]</c> (see <see cref="NegotiationStrategyCalculator"/>'s own doc
/// comment), so this module cannot reference <c>Raffa.Documents.Contracts</c>'s type, and
/// <c>Raffa.AiGateway.Contracts.AiCitation</c>/<c>AiEvidenceSnippet</c> are document-citation
/// shaped (<c>DocumentId</c>/<c>Page</c>/<c>Section</c>) for RAG answers grounded in unstructured
/// document text — the wrong shape for a pointer into this module's own already-structured
/// <see cref="Raffa.Quotes.Domain.QuoteLine"/> row, and a reference this module cannot take
/// anyway (AI Gateway is out of this module's allowed set).
/// </para>
///
/// <para>
/// Computed fresh alongside its owning <see cref="NegotiationLever"/> on every call, never
/// persisted — the same posture every other type in this application layer already takes (no
/// ADR/spec names a stored "negotiation evidence" table).
/// </para>
/// </summary>
/// <param name="FieldName">Dotted pointer to the exact field this citation is evidence for, e.g.
/// <c>"QuoteLine.Quantity"</c> — never a lever-level generality, always specific enough to
/// re-query the source row. For a fact this module derives or computes itself rather than reading
/// directly off a persisted entity (e.g. the sibling-line count, or the negotiation-timing
/// reference date), names the computation instead, honestly (e.g. <c>"Quote.LineCount"</c>).</param>
/// <param name="Value">The cited value, formatted the same way <see cref="NegotiationLever.Rationale"/>
/// itself renders it (culture-invariant), so the citation and the prose can never silently
/// disagree.</param>
/// <param name="SourceSpan">This line's own extraction source span
/// (<see cref="Raffa.Quotes.Domain.QuoteLine.SourceSpan"/>), when <see cref="FieldName"/> points
/// at a field the AI Gateway `extract` role originally proposed for this line.
/// <see langword="null"/> for a fact this module derives or computes itself (a normalized/derived
/// field, a cross-line count, or today's date) — none of those has a document span to cite.</param>
/// <param name="SourcePage">This line's own extraction source page
/// (<see cref="Raffa.Quotes.Domain.QuoteLine.SourcePage"/>) — <see langword="null"/> under the
/// same condition as <see cref="SourceSpan"/>.</param>
/// <param name="Confidence">This line's own extraction confidence
/// (<see cref="Raffa.Quotes.Domain.QuoteLine.Confidence"/>, Appendix C rule 2's "confidence
/// metadata") — <see langword="null"/> under the same condition as <see cref="SourceSpan"/>.</param>
public sealed record NegotiationLeverEvidence(
    string FieldName,
    string Value,
    string? SourceSpan,
    int? SourcePage,
    double? Confidence);
