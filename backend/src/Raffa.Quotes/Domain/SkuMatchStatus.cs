namespace Raffa.Quotes.Domain;

/// <summary>
/// Outcome of matching one <see cref="QuoteLine"/>'s normalized SKU against this tenant's own
/// <see cref="SkuProductMapping"/> rows (task E05/F01/US02/T01, sku-normalization; parent story
/// us-02-sku-normalization AC-1 "Normalize SKU/edition to the canonical product mapping", AC-2
/// "Show unmatched SKUs..."). Computed by
/// <c>Raffa.Quotes.Application.Normalization.SkuNormalizationService</c> — never hand-set by a
/// caller, so "matched" always means "actually resolved against a stored mapping", never a
/// fabricated default (Appendix C rule 10; spec §11.3's guardrail: "Do not generate a savings
/// target if line-item normalization is unresolved").
/// </summary>
public enum SkuMatchStatus
{
    /// <summary>The line carries no SKU at all (<see cref="QuoteLine.Sku"/> null/blank) — for
    /// example a services or usage-based line (mirrors
    /// <c>Raffa.Benchmark.Contracts.BenchmarkQuery.Sku</c>'s own "not every purchase is
    /// SKU-level" doc comment). Not a mapping gap to surface for correction, unlike
    /// <see cref="Unmatched"/>.</summary>
    NotApplicable,

    /// <summary>A SKU is present (<see cref="QuoteLine.NormalizedSku"/> is non-null) but does not
    /// resolve to any <see cref="SkuProductMapping"/> row for this tenant — spec §11.3's "Show
    /// unmatched SKUs and allow manual product mapping" (task E05/F01/US02/T02's own scope for the
    /// "allow manual mapping" half). The safe default: a line stays here until a mapping actually
    /// resolves it, never silently promoted to <see cref="Matched"/>.</summary>
    Unmatched,

    /// <summary>The normalized SKU resolved to an existing <see cref="SkuProductMapping"/> row for
    /// this tenant.</summary>
    Matched,
}
