using System.Globalization;
using Raffa.Benchmark.Contracts;
using Raffa.Quotes.Domain;

namespace Raffa.Quotes.Application.Assessment;

/// <summary>
/// The deterministic above/in-line/below classifier (task E05/F02/US01/T01, market-assessment;
/// parent story us-01-market-assessment AC-2's own "flag" half — target range/potential saving are
/// task-02's own, separate scope). Pure and synchronous: no database call, no HTTP call, no LLM
/// call anywhere in <see cref="Classify"/> — the same <paramref name="normalizedUnitPrice"/>/
/// <paramref name="benchmark"/> pair always produces the same
/// <see cref="MarketPriceClassification"/> (Appendix C rule 6), the same determinism convention
/// <c>Raffa.Savings.Application.PriceNormalizationCalculator</c> already established for the
/// analogous Savings comparison.
///
/// <b>The market band is [P25, P75]</b>: spec §11.2's own "Assessment output" example table lists
/// "Expected market range" and "Comparable median" as separate, adjacent rows — this classifier
/// reads that as literally the P25-P75 band already published on every
/// <see cref="BenchmarkDistribution"/>, with P50 as the median inside it. A price at or below P25
/// is <see cref="MarketPosition.BelowMarket"/>; at or above P75 is
/// <see cref="MarketPosition.AboveMarket"/>; anything else (including sitting exactly on P50) is
/// <see cref="MarketPosition.InLine"/>. No interpolation is needed for a three-way flag (unlike
/// <c>PriceNormalizationCalculator.ComputePercentileRank</c>'s own continuous 0-100 scale, which
/// this classifier deliberately does not reproduce — task-01's own coding objective is the flag,
/// not a percentile rank).
/// </summary>
public static class MarketAssessmentCalculator
{
    /// <summary>
    /// Classifies <paramref name="normalizedUnitPrice"/> (a <see cref="QuoteLine.UnitPrice"/> — see
    /// <see cref="MarketAssessmentQueryBuilder"/>'s own doc comment for why this is the raw, not the
    /// annualized, price) against <paramref name="benchmark"/>. Never fabricates: a benchmark with
    /// no published distribution, or one whose markers are not well-ordered
    /// (<c>P25 &lt;= P50 &lt;= P75</c>), returns <see cref="MarketAssessmentStatus.InsufficientBenchmarkData"/>
    /// rather than a market position computed from data that cannot support it (Appendix C rule 10;
    /// the same defensive check
    /// <c>Raffa.Savings.Application.PriceNormalizationCalculator.Compare</c> already applies to
    /// this exact same distribution shape).
    /// </summary>
    public static MarketPriceClassification Classify(decimal normalizedUnitPrice, BenchmarkResult benchmark)
    {
        ArgumentNullException.ThrowIfNull(benchmark);

        if (benchmark.Distribution is not { } distribution)
        {
            return new MarketPriceClassification(
                MarketAssessmentStatus.InsufficientBenchmarkData,
                null,
                "Benchmark.Distribution is null (Benchmark.HasSufficientData is false): the " +
                "adapter had too few comparables to publish P25/P50/P75, so a market position " +
                "cannot be determined without fabricating a distribution (Appendix C rule 10; " +
                "ADR-001).");
        }

        if (!(distribution.P25 <= distribution.P50 && distribution.P50 <= distribution.P75))
        {
            return new MarketPriceClassification(
                MarketAssessmentStatus.InsufficientBenchmarkData,
                null,
                $"Benchmark.Distribution ({Fmt(distribution.P25)}/{Fmt(distribution.P50)}/" +
                $"{Fmt(distribution.P75)}) is not well-ordered (P25 <= P50 <= P75 does not hold): " +
                "a market-position comparison against it would not be meaningful (Appendix C rule " +
                "10).");
        }

        var position = normalizedUnitPrice <= distribution.P25 ? MarketPosition.BelowMarket
            : normalizedUnitPrice >= distribution.P75 ? MarketPosition.AboveMarket
            : MarketPosition.InLine;

        return new MarketPriceClassification(
            MarketAssessmentStatus.Assessed,
            position,
            $"Unit price {Fmt(normalizedUnitPrice)} {benchmark.Currency} is {DescribePosition(position)} " +
            $"the expected market range [{Fmt(distribution.P25)}, {Fmt(distribution.P75)}] " +
            $"{benchmark.Currency} (comparable median {Fmt(distribution.P50)} {benchmark.Currency}), " +
            "deterministic arithmetic (Appendix C rule 6).");
    }

    private static string DescribePosition(MarketPosition position) => position switch
    {
        MarketPosition.BelowMarket => "below",
        MarketPosition.AboveMarket => "above",
        _ => "in line with",
    };

    /// <summary>Culture-invariant, unpadded decimal formatting for explanation strings — same
    /// convention <c>Raffa.Savings.Application.PriceNormalizationCalculator.Fmt</c> already
    /// established, kept as its own copy here (each calculator owns its own formatting helper, the
    /// same pattern <c>Raffa.Savings.Application.SavingsProvenanceClassifier</c>'s own <c>Fmt</c>
    /// already follows).</summary>
    private static string Fmt(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}

/// <summary>Result of <see cref="MarketAssessmentCalculator.Classify"/> — the caller
/// (<see cref="MarketAssessmentService"/>) folds this into a full <see cref="LineMarketAssessment"/>
/// alongside the <see cref="QuoteLine"/> id and the <see cref="BenchmarkResult"/> it was computed
/// from.</summary>
/// <param name="Status">Which outcome this is — see <see cref="MarketAssessmentStatus"/>'s own doc
/// comments.</param>
/// <param name="Position">The market-position flag — populated only when <paramref name="Status"/>
/// is <see cref="MarketAssessmentStatus.Assessed"/>.</param>
/// <param name="Explanation">Human-readable trace of what this classifier computed and why — mirrors
/// <c>Raffa.Savings.Application.PriceComparisonResult.Explanation</c>'s own role.</param>
public sealed record MarketPriceClassification(
    MarketAssessmentStatus Status,
    MarketPosition? Position,
    string Explanation);
