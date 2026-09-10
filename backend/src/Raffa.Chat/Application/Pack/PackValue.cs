namespace Raffa.Chat.Application.Pack;

/// <summary>
/// The kind of normalized fact a <see cref="PackValue"/> carries — how
/// <c>Guards.NumericGuard</c> must parse/compare a matching token found in an answer's markdown
/// (task E13/F06/US01/T01, ask-engine coding objective point 3: "values[] numeric facts";
/// point 5: "every monetary amount, percentage and date... normalized and currency-aware; dates
/// in the pack's formats").
/// </summary>
public enum PackValueKind
{
    /// <summary>A currency amount, e.g. <c>640000</c> (CHF) — compared currency-aware
    /// (<see cref="PackValue.Currency"/>), ignoring thousands separators/decimal style.</summary>
    Amount,

    /// <summary>A percentage, e.g. <c>7</c> for "7%" — compared ignoring a trailing '%' and
    /// decimal-style differences.</summary>
    Percentage,

    /// <summary>A calendar date, formatted exactly as the pack itself renders it
    /// (<see cref="PackValue.Value"/> is the canonical <c>yyyy-MM-dd</c> the guard parses both
    /// sides through) — "dates in the pack's formats" (task text).</summary>
    Date,

    /// <summary>Any other bare number (a day count, a sample size, a score) — compared as a plain
    /// invariant-culture number.</summary>
    Number,
}

/// <summary>
/// One normalized, guardable fact carried by a <see cref="PackItem"/> (task E13/F06/US01/T01,
/// ask-engine coding objective point 3: "values[] numeric facts"). <c>Guards.NumericGuard</c> is
/// the only consumer that inspects <see cref="Value"/>'s content — everything else in the pipeline
/// treats a pack item as an opaque citation card.
/// </summary>
/// <param name="Key">A short, human-readable label for this fact, e.g. <c>"unitPriceP50"</c>,
/// <c>"endDate"</c>, <c>"upliftCapPercent"</c> — shown nowhere on its own; it exists so an
/// explanation string (guard intervention detail, test assertion) can name which fact of several
/// on the same item mismatched.</param>
/// <param name="Value">The normalized value, invariant-culture formatted: a bare decimal for
/// <see cref="PackValueKind.Amount"/>/<see cref="PackValueKind.Percentage"/>/
/// <see cref="PackValueKind.Number"/> (no thousands separator, no currency symbol, no '%'), or
/// <c>yyyy-MM-dd</c> for <see cref="PackValueKind.Date"/>.</param>
/// <param name="Kind">How <c>Guards.NumericGuard</c> must parse a candidate token before comparing
/// it to this value.</param>
/// <param name="Currency">ISO 4217 currency code, required and meaningful only for
/// <see cref="PackValueKind.Amount"/> (a bare number is currency-blind — R-ASK-06's "currency
/// -aware" comparison needs to know which currency a matched amount is even expressed in);
/// <see langword="null"/> for every other <see cref="Kind"/>.</param>
public sealed record PackValue(string Key, string Value, PackValueKind Kind, string? Currency = null);
