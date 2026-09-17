using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Domain;

/// <summary>
/// Source span + confidence for one extracted <see cref="Contract"/> scalar field (product spec
/// §7.3: "every extracted fact carries source span + confidence"; Appendix C rule 2). Task
/// E02/F01/US02/T01 (us-02-staged-extraction, AC-2) adds this entity because <see cref="Contract"/>
/// itself is one row aggregating many independently-extracted facts (currency, dates, spend,
/// payment terms, ...) — unlike <see cref="Clause"/>/<see cref="Obligation"/>/<see cref="Risk"/>/
/// <see cref="ContractLineItem"/>, which are already "one row = one fact" and carry their own
/// <c>SourceSpan</c>/<c>SourcePage</c>/<c>Confidence</c> columns directly, a single evidence
/// column set on <see cref="Contract"/> could not say *which* field it was evidence for. This is
/// the extraction-time sibling of <see cref="CorrectionHistory"/>, which already had to solve the
/// identical "one row, many independently-correctable fields" problem for human corrections —
/// same <see cref="FieldName"/>-per-row addressing scheme, kept as its own type (not a shared
/// base) because extraction rows are proposals with confidence and corrections are overrides
/// with an actor, and conflating the two would blur Appendix C rule 5's "preserve original AI
/// extraction and correction history" distinction.
/// </summary>
public sealed class ExtractionEvidence : TenantScopedEntity
{
    public required EntityId ContractId { get; set; }

    /// <summary>The document text this fact was extracted from, so evidence remains traceable
    /// even if the contract later aggregates facts from more than one source document (e.g. an
    /// amendment).</summary>
    public EntityId? SourceDocumentId { get; set; }

    /// <summary>The <see cref="ExtractionJob"/> whose stage run produced this row, for
    /// traceability back to the model/version that proposed it (brief §8).</summary>
    public EntityId? ExtractionJobId { get; set; }

    /// <summary>Which <see cref="Contract"/> property this row is evidence for, e.g.
    /// <c>"currency"</c>, <c>"annualSpend"</c>, <c>"startDate"</c> — mirrors
    /// <see cref="CorrectionHistory.FieldName"/>'s addressing scheme. Not modelled as an enum:
    /// new extractable fields must not require a migration to this type.</summary>
    public required string FieldName { get; set; }

    /// <summary>The extracted value as written onto <see cref="Contract"/>, kept alongside the
    /// evidence as plain text (dates/decimals included) so a reviewer can see what the model
    /// proposed without re-reading the <see cref="Contract"/> row's current (possibly since
    /// human-corrected) value.</summary>
    public string? Value { get; set; }

    /// <summary>
    /// The reviewer's corrected OCR phrase, written <b>beside</b> <see cref="Value"/> by the
    /// phrase-edit write path (epic-23 feature-03) — never in place of it (ADR-029 w18 footer
    /// clause 1: "an override, never an in-place rewrite"). <see cref="Value"/> stays exactly what
    /// the model proposed for every row; this is null until a human edits this phrase, and a
    /// reprocess that re-derives <see cref="Value"/> must leave this column untouched (ADR-027).
    /// The read model reads this the same way — and from the same row — as the proposal, so the
    /// two are always distinguishable rather than one silently replacing the other.
    /// </summary>
    public string? OverrideValue { get; set; }

    public string? SourceSpan { get; set; }
    public int? SourcePage { get; set; }
    public double? Confidence { get; set; }

    /// <summary>
    /// Pixel-space bounding box of this phrase on the rendered page image (ADR-003 w18 footer
    /// clauses 1-2; ADR-017 w18's <c>prebuilt-layout</c> word geometry, unioned over the phrase's
    /// run of words — <see cref="Raffa.AiGateway.Contracts.AiOcrWord"/>). All four are set
    /// together or not at all: every row written before this wave, and any page the OCR call
    /// returned no layout geometry for, leaves them null — the viewer then falls back to the
    /// existing <see cref="SourceSpan"/> text-level highlight, never an error (epic-23 AC-4).
    /// Read from this same row regardless of whether <see cref="OverrideValue"/> is set: editing
    /// the phrase's text does not move the box the model originally found it in.
    /// </summary>
    public double? BoxX { get; set; }
    public double? BoxY { get; set; }
    public double? BoxWidth { get; set; }
    public double? BoxHeight { get; set; }

    /// <summary>
    /// Server-computed acceptance of this proposal: <c>auto_accepted</c>,
    /// <c>human_accepted</c>, or <c>review_required</c>. Not an enum column — new extractable
    /// fields must not require a migration here, and neither must a new decision literal
    /// (same reason <see cref="FieldName"/> is a string). Null only on rows written before
    /// this column existed; a reprocess re-derives it.
    /// </summary>
    public string? Decision { get; set; }

    /// <summary>When <see cref="Decision"/> was written for this row.</summary>
    public DateTimeOffset? DecidedAt { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
