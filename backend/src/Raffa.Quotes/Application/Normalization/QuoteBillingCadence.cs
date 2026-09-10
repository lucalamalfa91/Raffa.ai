namespace Raffa.Quotes.Application.Normalization;

/// <summary>
/// Recognizes a small, fixed, deliberately unambiguous vocabulary of billing-cadence phrases for
/// task E05/F01/US01/T02 (quote-normalization) — see
/// <see cref="QuoteLineNormalizationService.NormalizeUnitEconomics"/>'s own doc comment for how the
/// result is used.
///
/// <b>Deliberately narrow, not a general date/duration parser</b>: <c>Raffa.Quotes.Domain
/// .QuoteLine.Term</c> is free text with no ADR/spec-fixed vocabulary (see that property's own doc
/// comment), so a numeric commitment length ("36 months", "3 years") is genuinely ambiguous between
/// "this recurs every N months" and "this is a one-time N-month commitment billed some other,
/// unstated way" — this codebase has no way to actually know which, and guessing would be exactly
/// the fabricated conversion Appendix C rule 10 forbids (the same restraint
/// <c>Raffa.Savings.Application.PriceComparisonRequest</c>'s own doc comment documents for
/// cross-module term alignment: "no additional term-arithmetic... when the true billing-period
/// relationship is not actually known"). Only phrases with one, singular, commonly-understood
/// meaning are recognized — for example "biannual"/"biennial" are deliberately <b>not</b> in this
/// vocabulary, because that word alone is genuinely ambiguous in English between "twice a year" and
/// "every two years". Anything not listed here — including every numeric term, "one-time",
/// "perpetual", a blank/whitespace-only term, or free text this method does not recognize — resolves
/// to <see langword="null"/>, the honest "cannot determine" this codebase always prefers over a
/// guess (Appendix C rule 10; spec §11.3 "line-item normalization is unresolved").
/// </summary>
internal static class QuoteBillingCadence
{
    /// <summary>Case-insensitive, whitespace-trimmed exact-phrase lookup — not a regex, not a
    /// substring match, so "quarterly bonus" or "annual review" (real words that merely contain a
    /// recognized phrase) never accidentally resolve to a cadence they do not actually name.</summary>
    private static readonly IReadOnlyDictionary<string, int> MonthsByRecognizedPhrase =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["monthly"] = 1,
            ["month"] = 1,
            ["per month"] = 1,
            ["/month"] = 1,

            ["quarterly"] = 3,
            ["quarter"] = 3,
            ["per quarter"] = 3,

            ["semi-annual"] = 6,
            ["semiannual"] = 6,
            ["semi annual"] = 6,

            ["annual"] = 12,
            ["annually"] = 12,
            ["yearly"] = 12,
            ["year"] = 12,
            ["per year"] = 12,
            ["/year"] = 12,
        };

    /// <summary>
    /// Returns the recognized cadence's length in months (1/3/6/12), or <see langword="null"/> when
    /// <paramref name="term"/> is blank or not an exact match (after trimming) for one of this
    /// method's own fixed, documented phrases.
    /// </summary>
    public static int? RecognizeMonths(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return null;
        }

        return MonthsByRecognizedPhrase.TryGetValue(term.Trim(), out var months) ? months : null;
    }
}
