using System.Globalization;
using System.Text.RegularExpressions;
using Contigo.Chat.Application.Pack;

namespace Contigo.Chat.Application.Guards;

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
/// </summary>
public static class NumericGuard
{
    private static readonly string[] SupportedCurrencyCodes = ["CHF", "EUR", "USD", "GBP"];

    private static readonly Regex PercentagePattern = new(
        @"(?<amount>\d+(?:[.,]\d+)?)\s?%",
        RegexOptions.Compiled);

    // Either "CHF 140" / "CHF140" or "140 CHF" / "140CHF" — currency code adjacent to a number,
    // in either order. Group names differ per alternative so the caller can tell which side the
    // currency was on without re-matching.
    private static readonly Regex CurrencyAmountPattern = new(
        @"\b(?<cur1>CHF|EUR|USD|GBP)\s?(?<amt1>\d[\d,.'’]*)\b|" +
        @"\b(?<amt2>\d[\d,.'’]*)\s?(?<cur2>CHF|EUR|USD|GBP)\b",
        RegexOptions.Compiled);

    private static readonly Regex IsoDatePattern = new(@"\b(\d{4}-\d{2}-\d{2})\b", RegexOptions.Compiled);

    // "15 January 2027" / "15 gennaio 2027" — a day number, a run of letters (any script, so an
    // Italian month name matches too), a four-digit year.
    private static readonly Regex LongDatePattern = new(
        @"\b(\d{1,2}\s+\p{L}+\s+\d{4})\b", RegexOptions.Compiled);

    private static readonly string[] LongDateFormats = ["d MMMM yyyy", "dd MMMM yyyy"];

    private static readonly CultureInfo[] DateCultures =
    [
        CultureInfo.InvariantCulture,
        CultureInfo.GetCultureInfo("it-IT"),
    ];

    /// <summary>
    /// Validates every currency amount, percentage and date found in
    /// <paramref name="answerMarkdown"/> against <paramref name="pack"/>'s own
    /// <see cref="PackValue"/> facts (across every <see cref="PackItem"/>, regardless of corpus —
    /// a number may be grounded by a tenant fact, a market record or a calculator output equally).
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="pack"/> is <see langword="null"/>.</exception>
    public static GuardVerdict Validate(string? answerMarkdown, IReadOnlyList<PackItem> pack)
    {
        ArgumentNullException.ThrowIfNull(pack);

        var markdown = answerMarkdown ?? string.Empty;
        var values = pack.SelectMany(item => item.Values).ToList();

        foreach (Match match in PercentagePattern.Matches(markdown))
        {
            var candidate = ParseInvariantDecimal(match.Groups["amount"].Value);
            var grounded = values
                .Where(v => v.Kind == PackValueKind.Percentage)
                .Any(v => NumbersMatch(candidate, ParseInvariantDecimal(v.Value)));

            if (!grounded && !AnyPackSnippetContains(pack, match.Value))
            {
                return GuardVerdict.Fail(
                    $"percentage '{match.Value.Trim()}' does not equal any pack value (Appendix C rule 10).");
            }
        }

        foreach (Match match in CurrencyAmountPattern.Matches(markdown))
        {
            var currency = match.Groups["cur1"].Success ? match.Groups["cur1"].Value : match.Groups["cur2"].Value;
            var amountText = match.Groups["amt1"].Success ? match.Groups["amt1"].Value : match.Groups["amt2"].Value;
            var candidate = ParseInvariantDecimal(amountText);

            var grounded = values
                .Where(v => v.Kind == PackValueKind.Amount &&
                    string.Equals(v.Currency, currency, StringComparison.OrdinalIgnoreCase))
                .Any(v => NumbersMatch(candidate, ParseInvariantDecimal(v.Value)));

            // Fallback: the amount is also grounded when it appears verbatim inside some pack
            // item's own Snippet (a clause/market-note excerpt legitimately quoting a figure that
            // was never separately structured into Values — e.g. "the liability cap is CHF
            // 1,000,000" copied straight from a cited clause). Never fabricated: this only ever
            // matches text the pack itself already carries as citable evidence (Appendix C rule
            // 2), the same source a Values-backed match would point to; it does not weaken the
            // AC-7 fabrication case (a truly invented figure appears in neither Values nor any
            // snippet).
            if (!grounded && !AnyPackSnippetContains(pack, match.Value))
            {
                return GuardVerdict.Fail(
                    $"amount '{match.Value.Trim()}' does not equal any {currency} pack value, and " +
                    "does not appear verbatim in any cited evidence snippet either (Appendix C " +
                    "rule 10; currency-aware — a different currency's matching number does not count).");
            }
        }

        foreach (var dateText in FindDateTokens(markdown))
        {
            if (!TryParseDate(dateText, out var candidate))
            {
                // Not a recognized calendar-date shape (an unusual month abbreviation, etc.) —
                // skip rather than risk a false positive on prose this guard cannot parse.
                continue;
            }

            var grounded = values
                .Where(v => v.Kind == PackValueKind.Date)
                .Any(v => DateOnly.TryParse(v.Value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var packDate)
                    && packDate == candidate);

            // Fallback: the same verbatim-snippet grounding the percentage and currency-amount
            // loops above already fall back to (AnyPackSnippetContains's own doc comment) — a date
            // copied straight out of a cited clause excerpt is grounded even when it was never
            // separately structured into a Values entry (e.g. a contract fact item built from
            // portfolio data always carries an "endDate" PackValue, but a *clause* pack item's
            // snippet — raw retrieved text — never does; without this fallback, any clause answer
            // that echoes a bare calendar date it legitimately quoted would guard-fail every time).
            if (!grounded && !AnyPackSnippetContains(pack, dateText))
            {
                return GuardVerdict.Fail(
                    $"date '{dateText}' does not equal any pack value, and does not appear " +
                    "verbatim in any cited evidence snippet either (Appendix C rule 10).");
            }
        }

        return GuardVerdict.Ok;
    }

    private static IEnumerable<string> FindDateTokens(string markdown)
    {
        foreach (Match match in IsoDatePattern.Matches(markdown))
        {
            yield return match.Groups[1].Value;
        }

        foreach (Match match in LongDatePattern.Matches(markdown))
        {
            yield return match.Groups[1].Value;
        }
    }

    private static bool TryParseDate(string text, out DateOnly date)
    {
        if (DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return true;
        }

        foreach (var culture in DateCultures)
        {
            foreach (var format in LongDateFormats)
            {
                if (DateOnly.TryParseExact(text, format, culture, DateTimeStyles.None, out date))
                {
                    return true;
                }
            }
        }

        date = default;
        return false;
    }

    /// <summary>Strips thousands separators (<c>,</c>/<c>'</c>/<c>’</c>) and parses the remaining
    /// invariant-culture decimal (accepting either <c>.</c> or <c>,</c> as the decimal point, since
    /// both appear in the wild depending on locale) — never throws on a malformed token; an
    /// unparseable amount fails the match rather than the guard call itself.</summary>
    private static decimal ParseInvariantDecimal(string text)
    {
        var cleaned = text.Replace("'", "", StringComparison.Ordinal).Replace("’", "", StringComparison.Ordinal);

        // A single comma with exactly two trailing digits reads as a decimal separator (European
        // style, e.g. "132,50"); anything else (thousands grouping, e.g. "1,000") is dropped.
        if (Regex.IsMatch(cleaned, @"^\d+,\d{2}$", RegexOptions.None))
        {
            cleaned = cleaned.Replace(',', '.');
        }
        else
        {
            cleaned = cleaned.Replace(",", "", StringComparison.Ordinal);
        }

        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : decimal.MinValue;
    }

    /// <summary>Equal within a cent-level tolerance — guards against harmless floating rounding
    /// noise between how the calculators formatted a value and how the model echoed it, without
    /// opening the door to a materially different number (Appendix C rule 10: still uncertainty
    /// over fabricated precision, not a loose match).</summary>
    private static bool NumbersMatch(decimal candidate, decimal packValue) =>
        Math.Abs(candidate - packValue) <= 0.01m;

    /// <summary>The verbatim-snippet fallback the percentage, currency-amount and date loops all
    /// fall back to when <paramref name="token"/> (the raw regex match from the answer markdown —
    /// e.g. <c>"CHF 140"</c>, <c>"7%"</c> or <c>"2026-01-01"</c>) does not equal a structured
    /// <see cref="PackItem.Values"/> fact: whether it nonetheless appears verbatim in some
    /// <see cref="PackItem.Snippet"/> in
    /// <paramref name="pack"/>, i.e. the model quoted a figure straight out of a cited clause or
    /// market note excerpt rather than restating a calculator output (see the currency-amount
    /// loop's own remarks). Ordinal, case-sensitive: the snippet must carry the same digits the
    /// model echoed, not merely a same-shaped one — never a source of false grounding, only ever
    /// matches text the pack itself already carries as citable evidence (Appendix C rule 2).</summary>
    private static bool AnyPackSnippetContains(IReadOnlyList<PackItem> pack, string token)
    {
        var trimmed = token.Trim();
        return trimmed.Length > 0 &&
            pack.Any(item => item.Snippet.Contains(trimmed, StringComparison.Ordinal));
    }
}
