namespace Raffa.Benchmark.Contracts;

/// <summary>
/// A generic priced line — quantity, unit price, term, currency, optional SKU, plus the matched
/// <see cref="BenchmarkDistribution"/> when one exists (task E13/F07/US01/T01, insights-calculators;
/// parent story us-01-insights AC-3; product spec §12.1). Generalizes the shape
/// <c>Raffa.Quotes.Domain.QuoteLine</c> + <c>Raffa.Quotes.Application.Assessment.LineTargetSaving</c>
/// together already carry for one quote line, so a <c>Raffa.Documents.Contracts.Domain.ContractLineItem</c>
/// can be assessed the same deterministic way without either module depending on the other's entity.
///
/// <para>
/// <b>Why this DTO lives in <c>Raffa.Benchmark</c>, not <c>Raffa.Insights</c></b>: ADR-024's
/// module map puts <c>Raffa.Insights</c> at <c>[SharedKernel, Benchmark]</c> — the same allow-list
/// <c>Raffa.Quotes</c> already has (<c>Raffa.ArchitectureTests.DependencyDirectionTests
/// .AllowedReferences</c>). Neither module may reference the other (Insights is not in Quotes' own
/// allow-list, and adding it would touch <c>DependencyDirectionTests.cs</c>, out of this task's file
/// scope — see the parent task's own "Do not touch" list). <c>Raffa.Benchmark</c> is the one
/// project both already see, so this shared input — and the tiny pure step below both calculators
/// share — lives here, next to <see cref="BenchmarkDistribution"/>, instead of being declared once
/// and referenced from only one side.
/// </para>
/// </summary>
/// <param name="Sku">SKU/edition identifier, when the line is SKU-level; <see langword="null"/> for a
/// services/usage-based line (mirrors <see cref="BenchmarkQuery.Sku"/>'s own optionality).</param>
/// <param name="Description">Human-readable line description (product/service name) — always
/// present, the one field this record does not make optional (mirrors
/// <c>Raffa.Quotes.Domain.QuoteLine.Description</c> / <c>Raffa.Documents.Contracts.Domain
/// .ContractLineItem.Description</c>, both <see langword="required"/> on their own entities).</param>
/// <param name="Quantity">Purchased quantity, when recorded.</param>
/// <param name="UnitPrice">The line's current per-unit price — the figure a negotiation target/
/// walk-away is computed against.</param>
/// <param name="Currency">ISO 4217 currency code <see cref="UnitPrice"/> is expressed in.</param>
/// <param name="TermMonths">Commitment length in months, when known as a normalized integer (mirrors
/// <c>QuoteLine.NormalizedTermMonths</c>) — <see langword="null"/> when no normalized-months field
/// exists for this line's source entity yet (for example <c>ContractLineItem.BillingPeriod</c> is
/// still free text with no normalized-months column — a follow-up gap, not this task's to close).</param>
/// <param name="Benchmark">The matched P25/P50/P75 distribution, when the Benchmark Service found
/// enough comparables to publish one; <see langword="null"/> for an explicit "insufficient market
/// data" outcome (ADR-001) — never a bare precise-looking number without one.</param>
/// <param name="SampleSize">Number of comparables behind <see cref="Benchmark"/>, when the adapter
/// can report one (mirrors <see cref="BenchmarkResult.SampleSize"/>) — provenance only, not read by
/// the pure stepping math below.</param>
public sealed record PricedLine(
    string? Sku,
    string Description,
    decimal? Quantity,
    decimal? UnitPrice,
    string? Currency,
    int? TermMonths,
    BenchmarkDistribution? Benchmark,
    int? SampleSize);

/// <summary>
/// The one piece of negotiation-target arithmetic <c>Raffa.Quotes.Application.Strategy
/// .NegotiationStrategyCalculator</c> (quote lines) and <c>Raffa.Insights.Negotiation
/// .PricedLineNegotiationCalculator</c> (contract priced lines) must produce identically for
/// identical inputs (parent story us-01-insights AC-3: "yields opening target, acceptable range,
/// walk-away and levers exactly as a quote line does"). Deliberately the smallest possible shared
/// surface: stepping an <em>already-known</em> acceptable range into an opening target and a
/// walk-away threshold, not the earlier "raw distribution -&gt; recommended range" step (that stays
/// each module's own, independently-implemented arithmetic — <c>Raffa.Quotes.Application.Assessment
/// .TargetSavingCalculator</c> on one side, <c>PricedLineNegotiationCalculator</c> on the other —
/// because <c>NegotiationStrategyCalculator.Compute</c>'s public signature already takes a
/// pre-computed <c>LineTargetSaving</c>, not a raw <see cref="BenchmarkDistribution"/>, and changing
/// that signature would break every existing call site/test — out of this task's "existing quote
/// strategy tests must pass unchanged" bound). Pure and synchronous (Appendix C rule 6): the same
/// three decimals always produce the same pair, no database/HTTP/LLM call.
/// </summary>
public static class PricedLineNegotiationMath
{
    /// <summary>
    /// Steps <paramref name="rangeLow"/>/<paramref name="rangeHigh"/> (an already-known acceptable
    /// target range) into an opening target one range-width below the low end (floored at zero — a
    /// target is never negative) and a walk-away threshold one range-width above the high end,
    /// clamped to <paramref name="unitPrice"/> (never recommend escalating past what is already
    /// being paid) — spec §12.1's own worked example: range [410k, 440k] against a 520k current
    /// price steps to opening 400k... walk-away 470k (440k + (440k-410k) = 470k, unclamped since
    /// 470k &lt;= 520k).
    /// </summary>
    public static (decimal OpeningTarget, decimal WalkAwayThreshold) StepRange(
        decimal rangeLow, decimal rangeHigh, decimal unitPrice)
    {
        var rangeWidth = rangeHigh - rangeLow;
        var openingTarget = Math.Max(0m, rangeLow - rangeWidth);
        var walkAwayThreshold = Math.Min(unitPrice, rangeHigh + rangeWidth);
        return (openingTarget, walkAwayThreshold);
    }
}
