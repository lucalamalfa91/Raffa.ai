using System.Globalization;
using Raffa.Chat.Application.Pack;

namespace Raffa.Chat.Application.Guards;

/// <summary>
/// The ADR-024 numeric guard (R-ASK-06 point 2): every currency amount, percentage and calendar date in
/// the answer equals a <see cref="PackValue"/> of the pack, so the model never restates a number the
/// calculators did not produce. Pure and synchronous. It checks only those three shapes, never a bare
/// integer (a page number or "in 40 days" would false-positive against an unrelated pack number);
/// citation markers belong to <see cref="GroundingGuard"/>.
///
/// <para>
/// <b>Currency-aware:</b> an amount is only compared with <see cref="PackValueKind.Amount"/> values of its
/// own currency. <b>Dates:</b> compared as <see cref="DateOnly"/> against the pack's ISO values, so
/// <c>2027-01-15</c> and <c>15 January 2027</c> both match. <b>Locale-aware (LANG-01, F1-T07):</b> the
/// five languages' number, currency and date formats are read by <see cref="NumericTokenExtractor"/>.
/// </para>
///
/// <para>
/// <b>Snippet fallback (F1-T07):</b> a figure that is not a structured <see cref="PackValue"/> is still
/// grounded when it appears in the snippet of an item the answer cites (<c>citedKeys</c>), verbatim or as
/// the same value in another format — a figure quoted from clause prose is never separately structured.
/// With no <c>citedKeys</c> the whole pack is searched.
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
        var snippets = new Lazy<List<FigureEvidence>>(
            () => SnippetScope(pack, citedKeys).Select(item => new FigureEvidence(item.Snippet)).ToList());

        foreach (var percentage in NumericTokenExtractor.Percentages(markdown, convention))
        {
            var grounded = values
                .Where(v => v.Kind == PackValueKind.Percentage)
                .Any(v => PackValueMatches(v.Value, percentage.Value, percentage.Tolerance));

            if (!grounded && !snippets.Value.Any(e => e.Supports(percentage)))
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

            if (!grounded && !snippets.Value.Any(e => e.Supports(money)))
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

            if (!grounded && !snippets.Value.Any(e => e.Supports(date)))
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
}
