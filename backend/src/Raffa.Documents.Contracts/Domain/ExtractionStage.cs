namespace Raffa.Documents.Contracts.Domain;

/// <summary>
/// A bounded, schema-constrained extraction task (product spec §7.2: "Avoid one giant prompt.
/// Split extraction into bounded, schema-constrained tasks"). Each stage is its own
/// <see cref="ExtractionJob"/>. Task E02/F01/US02/T01 (us-02-staged-extraction, AC-1) fixes the
/// order this enum is declared in as the pipeline order:
/// <c>Classification -&gt; Metadata -&gt; CommercialTerms -&gt; DatesAndRenewalTerms -&gt; LineItems
/// -&gt; LegalClauses -&gt; Obligations -&gt; Risk</c> — "metadata -&gt; commercial terms -&gt; dates -&gt;
/// price/SKU -&gt; clauses -&gt; obligations -&gt; risk", with <see cref="Classification"/> as the
/// zeroth stage queued by upload (task E01/F06/US01/T01) before any of these run.
/// <see cref="Raffa.Documents.Contracts.Application.Extraction.StagedExtractionService"/> is
/// the only caller that runs <see cref="Obligations"/>/<see cref="Risk"/> today.
/// </summary>
public enum ExtractionStage
{
    Classification,
    Metadata,
    CommercialTerms,
    DatesAndRenewalTerms,
    LineItems,
    LegalClauses,

    /// <summary>Added by task E02/F01/US02/T01 to close AC-1's 7-stage list — the original
    /// contract-schema task (E02/F02/US01/T01) that first declared this enum did not yet need a
    /// caller that ran every AC-1 stage. Stored via `HasConversion&lt;string&gt;()`
    /// (<see cref="Raffa.Documents.Contracts.Infrastructure.Configurations.ExtractionJobConfiguration"/>),
    /// so adding a value is a code-only change, not a migration.</summary>
    Obligations,

    /// <summary>See <see cref="Obligations"/> — same addition, same reason.</summary>
    Risk,
}
