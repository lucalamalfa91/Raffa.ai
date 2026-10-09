using Raffa.AiFlows.WebResearch.Guards;

namespace Raffa.AiFlows.Shared.Guards;

/// <summary>
/// A text a figure can be checked against, read once: the figure is supported when the text states the
/// same value (same currency for an amount, one of the candidate days for a date) in any of the five
/// languages' formats, or carries the figure's own characters. Shared by <see cref="NumericGuard"/> (the
/// snippet of a cited pack item) and <see cref="WebFigureGuard"/> (the quote of a cited web source).
/// Pure.
/// </summary>
internal sealed class FigureEvidence
{
    private readonly string _text;
    private readonly bool _web;
    private readonly IReadOnlyList<PercentToken> _percentages;
    private readonly IReadOnlyList<MoneyToken> _money;
    private readonly IReadOnlyList<DateToken> _dates;
    private readonly IReadOnlyList<MonthYearToken> _monthYears;

    /// <param name="text">The passage to read.</param>
    /// <param name="web">Read it the web way: a range written once ("5-10%", "$10-15") states both bounds,
    /// a spelled-out percentage and a month-year count, and the verbatim fallback never matches inside a
    /// longer number ("5%" is not in "15%"). Off, the answer-role reading: the shapes
    /// <see cref="NumericTokenExtractor"/> finds, and a plain substring match.</param>
    public FigureEvidence(string text, bool web = false)
    {
        _text = text;
        _web = web;
        var read = web ? text + "\n" + string.Join("\n", WebFigureTokens.RangeBounds(text).Select(r => r.Synthetic)) : text;
        _percentages = NumericTokenExtractor.Percentages(read)
            .Concat(web ? WebFigureTokens.SpelledPercentages(read) : [])
            .ToList();
        _money = NumericTokenExtractor.Money(read);
        _dates = NumericTokenExtractor.Dates(read);
        _monthYears = web ? WebFigureTokens.MonthYears(read) : [];
    }

    public bool Supports(NumericToken token) => token switch
    {
        PercentToken p => _percentages.Any(o => Close(o.Value, p.Value, o.Tolerance, p.Tolerance)) || ContainsToken(p.Raw),
        MoneyToken m => _money.Any(o => o.Currency == m.Currency && Close(o.Value, m.Value, o.Tolerance, m.Tolerance)) || ContainsToken(m.Raw),
        DateToken d => _dates.Any(o => o.Candidates.Intersect(d.Candidates).Any()) || ContainsToken(d.Raw),
        MonthYearToken my => _monthYears.Any(o => o.Month == my.Month && o.Year == my.Year) ||
            _dates.Any(o => o.Candidates.Any(c => c.Month == my.Month && c.Year == my.Year)),
        _ => false,
    };

    private static bool Close(decimal a, decimal b, decimal toleranceA, decimal toleranceB) =>
        Math.Abs(a - b) <= Math.Max(toleranceA, toleranceB);

    private bool ContainsToken(string raw)
    {
        var token = raw.Trim();
        if (token.Length == 0)
        {
            return false;
        }

        if (!_web)
        {
            return _text.Contains(token, StringComparison.Ordinal);
        }

        for (var index = _text.IndexOf(token, StringComparison.Ordinal); index >= 0; index = _text.IndexOf(token, index + 1, StringComparison.Ordinal))
        {
            var before = index == 0 ? ' ' : _text[index - 1];
            var afterIndex = index + token.Length;
            var after = afterIndex >= _text.Length ? ' ' : _text[afterIndex];
            var leftOk = !char.IsDigit(token[0]) || !(char.IsDigit(before) || before is '.' or ',');
            var rightOk = !char.IsDigit(token[^1]) || !(char.IsDigit(after) ||
                (after is '.' or ',' && afterIndex + 1 < _text.Length && char.IsDigit(_text[afterIndex + 1])));
            if (leftOk && rightOk)
            {
                return true;
            }
        }

        return false;
    }
}
