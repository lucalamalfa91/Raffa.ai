using System.Globalization;
using Raffa.Chat.Application.Pack;

namespace Raffa.Chat.Application.Guards;

/// <summary>
/// The ADR-024 numeric guard (task E13/F06/US01/T01, ask-engine coding objective point 5: "every
/// currency amount, percentage and date in `answerMarkdown` equals a pack value, normalized and
/// currency-aware; dates in the pack's formats"; `inputs/requirements.md` R-ASK-06 point 2;
/// Appendix C rule 6 "prefer deterministic arithmetic to LLM reasoning" applied here as "never let
/// the model restate a number the calculators did not produce"). Pure and synchronous — no I/O, no
/// LLM call.
///
/// <para>
/// Deliberately checks only the three shapes R-ASK-06 names (amount, percentage, date) — never a
/// bare integer: a page number, a day count written as prose ("in 40 days"), or a footnote index
/// would all false-positive against an unrelated pack number if every digit sequence were checked,
/// which would make this guard noisier than useful. <see cref="GroundingGuard"/> already owns
/// citation-marker (<c>[n]</c>) verification separately, so this guard never has to.
/// </para>
///
/// <para>
/// <b>Currency-aware (R-ASK-06)</b>: an amount token is only compared against
/// <see cref="PackValueKind.Amount"/> values whose own <see cref="PackValue.Currency"/> matches the
/// token's currency code — "CHF 140" can never be satisfied by an EUR 140 pack value, even though
/// the bare number is identical.
/// </para>
///
/// <para>
/// <b>Dates, in the pack's own formats</b>: <see cref="PackValue"/> stores every
/// <see cref="PackValueKind.Date"/> value as ISO <c>yyyy-MM-dd</c> (<c>PackItem</c>'s own doc
/// comment). The persona prompt (<c>Answering.AnswerPromptV2</c>) instructs the model to state the
/// same calendar date it was given, in either that ISO form or a human "D Month YYYY" form
/// (English or Italian) — this guard recognizes both shapes and compares the parsed
/// <see cref="DateOnly"/>, not the literal substring, so "2027-01-15" and "15 January 2027" both
/// match a pack value of <c>"2027-01-15"</c>. Exactly like the percentage and currency-amount
/// loops below, a date that matches no structured <see cref="PackValueKind.Date"/> value is still
/// grounded when it appears verbatim in a cited <see cref="PackItem.Snippet"/> (<see cref="AnyPackSnippetContains"/>)
/// — a bare calendar date quoted straight out of clause prose (e.g. an MSA's own opening line,
/// "... effective 2026-01-01, governed by ...") never gets separately structured into a
/// <see cref="PackValue"/>, the same "quoted, not calculated" case the amount/percentage loops
/// already handle.
/// </para>
///
/// <para>
/// <b>Locale-aware (LANG-01, F1-T07)</b>: amounts, percentages and dates are read in the five supported
/// languages by <see cref="NumericTokenExtractor"/> and <see cref="LocaleNumberParser"/> —
/// <c>1.234,56</c> (IT/ES/DE), <c>1,234.56</c> (EN), <c>1 234,56</c> (FR, with U+202F or U+00A0),
/// <c>EUR 667.000,00</c>, <c>667.000 euro</c>, <c>667,000</c>, <c>40k</c>, <c>1,5M</c>, and dates with
/// month names in each language. <c>667.000,00</c> used to be read as 667 and rejected.
/// </para>
///
/// <para>
/// <b>Snippet fallback only over the items the answer cites (F1-T07)</b>: a figure or date that is not
/// a structured <see cref="PackValue"/> is still grounded when it appears in the snippet of an item the
/// answer actually cites (<c>citedKeys</c>) — verbatim or, new, as the same number in another format.
/// It used to be searched in every snippet of the pack, so a figure from an item the answer never
/// cited passed. With no <c>citedKeys</c> (an answer that cites nothing, or a caller that has no
/// citations) the whole pack is searched, as before.
/// </para>
/// </summary>
public static class NumericGuard
{
    /// <summary>
    /// Validates every currency amount, percentage and date found in
    /// <paramref name="answerMarkdown"/> against <paramref name="pack"/>'s own
    /// <see cref="PackValue"/> facts (across every <see cref="PackItem"/>, regardless of corpus —
    /// a number may be grounded by a tenant fact, a market record or a calculator output equally).
    /// </summary>
    /// <param name="answerMarkdown">The text to check.</param>
    /// <param name="pack">The pack the numbers must come from.</param>
    /// <param name="citedKeys">The citation keys the answer returned: the verbatim-snippet fallback
    /// looks only inside these items. <see langword="null"/> or empty searches every item.</param>
    /// <param name="language">The answer's language (<c>it</c>, <c>en</c>, <c>fr</c>, <c>es</c>,
    /// <c>de</c>), when known: it settles the one ambiguous shape (<c>1.250</c>, <c>1,500</c>) and the
    /// day/month order of <c>03/04/2027</c>. <see langword="null"/> reads them the common way.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pack"/> is <see langword="null"/>.</exception>
    public static GuardVerdict Validate(
        string? answerMarkdown,
        IReadOnlyList<PackItem> pack,
        IReadOnlyCollection<string>? citedKeys = null,
        string? language = null)
    {
        ArgumentNullException.ThrowIfNull(pack);

        var markdown = answerMarkdown ?? string.Empty;
        var convention = NumericLocale.ConventionFor(language);
        var values = pack.SelectMany(item => item.Values).ToList();
        var scope = SnippetScope(pack, citedKeys);

        foreach (var percentage in NumericTokenExtractor.Percentages(markdown, convention))
        {
            var grounded = values
                .Where(v => v.Kind == PackValueKind.Percentage)
                .Any(v => PackValueMatches(v.Value, percentage.Value, percentage.Tolerance));

            if (!grounded && !SnippetGroundsPercentage(scope, percentage))
            {
                return GuardVerdict.Fail(
                    $"percentage '{percentage.Raw}' does not equal any pack value (Appendix C rule 10).");
            }
        }

        foreach (var money in NumericTokenExtractor.Money(markdown, convention))
        {
            var grounded = values
                .Where(v => v.Kind == PackValueKind.Amount &&
                    string.Equals(v.Currency, money.Currency, StringComparison.OrdinalIgnoreCase))
                .Any(v => PackValueMatches(v.Value, money.Value, money.Tolerance));

            // Fallback: the amount is also grounded when it appears inside the snippet of an item the
            // answer cites (a clause/market-note excerpt legitimately quoting a figure that was never
            // separately structured into Values — e.g. "the liability cap is CHF 1,000,000" copied
            // straight from a cited clause), verbatim or as the same number in another format
            // ("EUR 667.000,00" for "EUR 667,000.00"). Never fabricated: this only ever matches text
            // the pack itself already carries as citable evidence (Appendix C rule 2); a truly
            // invented figure appears in neither Values nor any cited snippet.
            if (!grounded && !SnippetGroundsMoney(scope, money))
            {
                return GuardVerdict.Fail(
                    $"amount '{money.Raw}' does not equal any {money.Currency} pack value, and " +
                    "does not appear in any cited evidence snippet either (Appendix C " +
                    "rule 10; currency-aware — a different currency's matching number does not count).");
            }
        }

        foreach (var date in NumericTokenExtractor.Dates(markdown, convention))
        {
            var grounded = values
                .Where(v => v.Kind == PackValueKind.Date)
                .Any(v => DateOnly.TryParse(v.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var packDate)
                    && date.Candidates.Contains(packDate));

            // Fallback: the same cited-snippet grounding — a date copied straight out of a cited
            // clause excerpt is grounded even when it was never separately structured into a Values
            // entry (a clause pack item's snippet is raw retrieved text and never carries one).
            if (!grounded && !SnippetGroundsDate(scope, date))
            {
                return GuardVerdict.Fail(
                    $"date '{date.Raw}' does not equal any pack value, and does not appear " +
                    "in any cited evidence snippet either (Appendix C rule 10).");
            }
        }

        return GuardVerdict.Ok;
    }

    /// <summary>The items whose snippets may ground a figure: the cited ones. An answer that cites
    /// nothing (uncited guidance) or cites keys the pack does not hold keeps the whole pack.</summary>
    private static IReadOnlyList<PackItem> SnippetScope(IReadOnlyList<PackItem> pack, IReadOnlyCollection<string>? citedKeys)
    {
        if (citedKeys is null || citedKeys.Count == 0)
        {
            return pack;
        }

        var keys = new HashSet<string>(citedKeys.Where(k => !string.IsNullOrWhiteSpace(k)), StringComparer.Ordinal);
        var cited = pack.Where(item => keys.Contains(item.CitationKey)).ToList();
        return cited.Count == 0 ? pack : cited;
    }

    private static bool PackValueMatches(string packValue, decimal candidate, decimal tolerance) =>
        LocaleNumberParser.TryParsePackValue(packValue, out var parsed) && Math.Abs(candidate - parsed) <= tolerance;

    private static bool SnippetGroundsPercentage(IReadOnlyList<PackItem> scope, PercentToken token) =>
        AnySnippetContains(scope, token.Raw) ||
        scope.Any(item => NumericTokenExtractor.Percentages(item.Snippet)
            .Any(other => Math.Abs(other.Value - token.Value) <= Math.Max(other.Tolerance, token.Tolerance)));

    private static bool SnippetGroundsMoney(IReadOnlyList<PackItem> scope, MoneyToken token) =>
        AnySnippetContains(scope, token.Raw) ||
        scope.Any(item => NumericTokenExtractor.Money(item.Snippet)
            .Any(other => string.Equals(other.Currency, token.Currency, StringComparison.Ordinal) &&
                Math.Abs(other.Value - token.Value) <= Math.Max(other.Tolerance, token.Tolerance)));

    private static bool SnippetGroundsDate(IReadOnlyList<PackItem> scope, DateToken token) =>
        AnySnippetContains(scope, token.Raw) ||
        scope.Any(item => NumericTokenExtractor.Dates(item.Snippet)
            .Any(other => other.Candidates.Intersect(token.Candidates).Any()));

    /// <summary>The verbatim-snippet fallback: whether <paramref name="token"/> (the raw match from the
    /// answer markdown — e.g. <c>"CHF 140"</c>, <c>"7%"</c> or <c>"2026-01-01"</c>) appears as is in
    /// the snippet of one of <paramref name="scope"/>'s items. Ordinal, case-sensitive: the snippet
    /// must carry the same characters the model echoed.</summary>
    private static bool AnySnippetContains(IReadOnlyList<PackItem> scope, string token)
    {
        var trimmed = token.Trim();
        return trimmed.Length > 0 &&
            scope.Any(item => item.Snippet.Contains(trimmed, StringComparison.Ordinal));
    }
}
