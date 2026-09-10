namespace Raffa.Quotes.Application.Assessment;

/// <summary>
/// Recommended target range + potential saving for one already-benchmarked
/// <see cref="Raffa.Quotes.Domain.QuoteLine"/> (task E05/F02/US01/T02, target-saving; parent story
/// us-01-market-assessment AC-2's own "recommended target range + potential saving" half — the
/// above/in-line/below flag is task-01's own, separate scope; see
/// <see cref="Raffa.Quotes.Domain.MarketPosition"/>'s own doc comment). Product spec §11.2's
/// "Assessment output" table names both rows verbatim: "Recommended target" (e.g. "CHF 410-440k")
/// and "Potential saving" (e.g. "CHF 80-110k").
///
/// Computed by <see cref="TargetSavingCalculator.Compute"/> — see that type's own doc comment for the
/// exact formula. Mirrors <c>Raffa.Savings.Application.PriceComparisonResult</c>'s own
/// <c>RecommendedTargetLow/High</c> + <c>SavingsRangeLow/High</c> + <c>TotalSavingsRangeLow/High</c>
/// field names and shape exactly (duplicated, not referenced: ADR-002 forbids <c>Raffa.Quotes</c>
/// from referencing <c>Raffa.Savings</c> — its own allowed Raffa references are exactly
/// <c>[SharedKernel, Benchmark]</c> — see <c>Raffa.Quotes.Domain.MarketConfidenceLevel</c>'s own doc
/// comment for why).
/// </summary>
/// <param name="RecommendedTargetLow">The more aggressive end of the recommended target range —
/// <c>min(P25, unitPrice)</c> — never higher than the current price (this calculator never
/// recommends paying <em>more</em> than the current price). <see langword="null"/> exactly when no
/// well-ordered benchmark distribution was available to compute one — see
/// <see cref="Explanation"/> for why.</param>
/// <param name="RecommendedTargetHigh">The more conservative end of the recommended target range —
/// <c>min(P50, unitPrice)</c>. Always <c>&gt;= RecommendedTargetLow</c> when populated. Null under the
/// same condition as <see cref="RecommendedTargetLow"/>.</param>
/// <param name="SavingsRangeLow">Per-unit saving from reaching only
/// <see cref="RecommendedTargetHigh"/> — <c>unitPrice - RecommendedTargetHigh</c>, always
/// <c>&gt;= 0</c> when populated. Null under the same condition as
/// <see cref="RecommendedTargetLow"/>.</param>
/// <param name="SavingsRangeHigh">Per-unit saving from reaching the more aggressive
/// <see cref="RecommendedTargetLow"/> — <c>unitPrice - RecommendedTargetLow</c>, always
/// <c>&gt;= SavingsRangeLow</c> when populated. Null under the same condition as
/// <see cref="RecommendedTargetLow"/>.</param>
/// <param name="TotalSavingsRangeLow"><see cref="SavingsRangeLow"/> times the line's own
/// <c>QuoteLine.Quantity</c> — the total (not per-unit) saving across every purchased unit; spec
/// §11.2's own "Potential saving" example (<c>CHF 80-110k</c>) is this total, not a per-unit rate.
/// Null under the same condition as <see cref="RecommendedTargetLow"/>.</param>
/// <param name="TotalSavingsRangeHigh"><see cref="SavingsRangeHigh"/> times the line's own
/// <c>QuoteLine.Quantity</c>. Null under the same condition as <see cref="RecommendedTargetLow"/>.</param>
/// <param name="Explanation">Human-readable, deterministic trace of what this calculator computed and
/// why — including an honest, named reason when every numeric field above is <see langword="null"/>
/// (never a silent, unexplained abstain — Appendix C rule 10).</param>
public sealed record LineTargetSaving(
    decimal? RecommendedTargetLow,
    decimal? RecommendedTargetHigh,
    decimal? SavingsRangeLow,
    decimal? SavingsRangeHigh,
    decimal? TotalSavingsRangeLow,
    decimal? TotalSavingsRangeHigh,
    string Explanation);
