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

    public string? SourceSpan { get; set; }
    public int? SourcePage { get; set; }
    public double? Confidence { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
