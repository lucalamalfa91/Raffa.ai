namespace Raffa.Quotes.Application.Normalization;

/// <summary>
/// AC-1's "normalize SKU/edition" (task E05/F01/US02/T01, sku-normalization) made concrete as an
/// isolated, dependency-free pure function — the same "directly unit-testable, no JSON parsing, no
/// database" shape as
/// <c>Raffa.Quotes.Application.Extraction.QuoteLineExtractionService.ComputePricing</c> (that
/// type's own doc comment).
///
/// Deliberately conservative: only whitespace/case noise is collapsed — <see cref="Normalize"/>
/// trims, collapses internal whitespace runs to a single space, then uppercases (invariant
/// culture, since a SKU is a code, not localized text). Punctuation (dashes, slashes, dots) is left
/// exactly as extracted: two SKUs that differ only by an internal dash (<c>"SKU-ENT-500"</c> vs
/// <c>"SKUENT500"</c>) are a real product-catalog question a person must confirm via manual mapping
/// (task E05/F01/US02/T02), not a formatting accident this normalizer should silently paper over —
/// a false-positive match would be exactly the "fabricated precision" Appendix C rule 10 warns
/// against.
/// </summary>
internal static class SkuNormalizer
{
    /// <summary>
    /// <see langword="null"/> when <paramref name="raw"/> is null/blank — a line with no SKU/edition
    /// text at all is not a formatting problem to fix (see
    /// <see cref="Raffa.Quotes.Domain.SkuMatchStatus.NotApplicable"/>), so there is nothing
    /// honest to normalize it to.
    /// </summary>
    internal static string? Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        // Split on any whitespace run (null separator array = "whitespace", per
        // string.Split(char[]?, StringSplitOptions)) and rejoin with a single space — collapses
        // "SKU-ENT   500" / tab- or newline-mangled extraction noise without a regex dependency.
        var words = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words).ToUpperInvariant();
    }
}
