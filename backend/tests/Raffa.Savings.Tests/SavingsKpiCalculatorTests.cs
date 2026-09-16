using Raffa.Savings.Application;
using Raffa.Savings.Domain;

namespace Raffa.Savings.Tests;

/// <summary>
/// Proves task E04/F03/US01/T01's (savings-kpis) execution step: <see cref="SavingsKpiCalculator"/>
/// buckets tenant-scoped savings opportunities into spec §10.1's "Savings Identified"/"Savings In
/// Progress"/"Savings Realized" KPIs, grouped by currency, with no database, no HTTP call and no LLM
/// call anywhere in the path — parent story us-01-savings-kpis AC-1. Mirrors
/// <c>Raffa.Renewals.Tests.PriorityScoreCalculatorTests</c>'s own plain-`new()`, no-DI-container
/// style.
/// </summary>
public sealed class SavingsKpiCalculatorTests
{
    private readonly SavingsKpiCalculator _calculator = new();

    private static SavingsOpportunitySnapshot Snapshot(
        SavingsOpportunityStatus status,
        string currency = "USD",
        decimal low = 100m,
        decimal high = 200m,
        double confidence = 0.8) =>
        new(status, currency, low, high, confidence);

    [Fact]
    public void No_opportunities_produces_three_honestly_empty_buckets()
    {
        var result = _calculator.Summarize([], []);

        Assert.Empty(result.Identified);
        Assert.Empty(result.InProgress);
        Assert.Empty(result.Realized);
    }

    [Fact]
    public void Each_status_lands_only_in_its_own_bucket()
    {
        var opportunities = new[]
        {
            Snapshot(SavingsOpportunityStatus.Identified),
            Snapshot(SavingsOpportunityStatus.InProgress),
            Snapshot(SavingsOpportunityStatus.Realized),
        };

        var result = _calculator.Summarize(opportunities, []);

        Assert.Single(result.Identified);
        Assert.Single(result.InProgress);
        Assert.Empty(result.Realized);
    }

    [Fact]
    public void Same_currency_same_status_rows_are_summed_and_counted_together()
    {
        var opportunities = new[]
        {
            Snapshot(SavingsOpportunityStatus.Identified, "USD", low: 100m, high: 200m, confidence: 0.6),
            Snapshot(SavingsOpportunityStatus.Identified, "USD", low: 300m, high: 500m, confidence: 1.0),
        };

        var result = _calculator.Summarize(opportunities, []);

        var bucket = Assert.Single(result.Identified);
        Assert.Equal("USD", bucket.Currency);
        Assert.Equal(400m, bucket.Low);
        Assert.Equal(700m, bucket.High);
        Assert.Equal(2, bucket.Count);
        Assert.Equal(0.8, bucket.AverageConfidence, precision: 10);
    }

    [Fact]
    public void Different_currencies_never_get_conflated_into_one_amount()
    {
        // Appendix C rule 10 / this codebase's own "no currency-conversion service anywhere"
        // convention: a naive Sum(EstimatedSavingsLow) across currencies would silently add a CHF
        // amount to a USD one. Two distinct, correctly-attributed rows is the only honest result.
        var opportunities = new[]
        {
            Snapshot(SavingsOpportunityStatus.Identified, "USD", low: 100m, high: 200m),
            Snapshot(SavingsOpportunityStatus.Identified, "CHF", low: 900m, high: 1_000m),
        };

        var result = _calculator.Summarize(opportunities, []);

        Assert.Equal(2, result.Identified.Count);
        var usd = Assert.Single(result.Identified, b => b.Currency == "USD");
        var chf = Assert.Single(result.Identified, b => b.Currency == "CHF");
        Assert.Equal(100m, usd.Low);
        Assert.Equal(200m, usd.High);
        Assert.Equal(900m, chf.Low);
        Assert.Equal(1_000m, chf.High);
    }

    [Fact]
    public void Differently_cased_same_currency_rows_are_merged_into_one_bucket()
    {
        // Nothing on the write side normalizes currency-code casing (SavingsOpportunityService
        // .CreateAsync validates only IsNullOrWhiteSpace; LLM-extracted currency is only
        // .Trim()-ed), so "USD" and "usd" must be treated as the same currency here — the same
        // case-insensitive convention PriceNormalizationCalculator already applies when comparing
        // currencies. A case-sensitive grouping would silently fragment this into two rows instead
        // of summing them into one, understating neither total but misreporting the count/shape.
        var opportunities = new[]
        {
            Snapshot(SavingsOpportunityStatus.Identified, "USD", low: 100m, high: 200m, confidence: 0.6),
            Snapshot(SavingsOpportunityStatus.Identified, "usd", low: 300m, high: 500m, confidence: 1.0),
        };

        var result = _calculator.Summarize(opportunities, []);

        var bucket = Assert.Single(result.Identified);
        Assert.Equal(400m, bucket.Low);
        Assert.Equal(700m, bucket.High);
        Assert.Equal(2, bucket.Count);
    }

    [Fact]
    public void Currency_buckets_are_ordered_deterministically_by_currency_code()
    {
        var opportunities = new[]
        {
            Snapshot(SavingsOpportunityStatus.Identified, "USD"),
            Snapshot(SavingsOpportunityStatus.Identified, "CHF"),
            Snapshot(SavingsOpportunityStatus.Identified, "EUR"),
        };

        var result = _calculator.Summarize(opportunities, []);

        Assert.Equal(["CHF", "EUR", "USD"], result.Identified.Select(b => b.Currency));
    }

    [Fact]
    public void Two_realized_rows_in_one_currency_sum_to_one_money_figure()
    {
        var realized = new[]
        {
            new RealizedSavingsSnapshot("CHF", 40_000m),
            new RealizedSavingsSnapshot("CHF", 45_000m),
        };

        var result = _calculator.Summarize([], realized);

        var bucket = Assert.Single(result.Realized);
        Assert.Equal("CHF", bucket.Currency);
        Assert.Equal(85_000m, bucket.Amount);
        Assert.Equal(2, bucket.Count);
        Assert.Empty(result.Identified);
        Assert.Empty(result.InProgress);
    }

    [Fact]
    public void Two_realized_currencies_produce_two_entries_and_no_combined_total()
    {
        var realized = new[]
        {
            new RealizedSavingsSnapshot("USD", 10_000m),
            new RealizedSavingsSnapshot("CHF", 85_000m),
        };

        var result = _calculator.Summarize([], realized);

        Assert.Equal(2, result.Realized.Count);
        var chf = Assert.Single(result.Realized, b => b.Currency == "CHF");
        var usd = Assert.Single(result.Realized, b => b.Currency == "USD");
        Assert.Equal(85_000m, chf.Amount);
        Assert.Equal(10_000m, usd.Amount);
        Assert.Equal(["CHF", "USD"], result.Realized.Select(b => b.Currency));
        Assert.DoesNotContain(result.Realized, b => b.Amount == 95_000m);
    }

    [Fact]
    public void An_opportunity_estimate_range_never_reaches_the_realized_member()
    {
        var opportunities = new[]
        {
            Snapshot(SavingsOpportunityStatus.Realized, "USD", low: 50m, high: 75m, confidence: 0.95),
            Snapshot(SavingsOpportunityStatus.Identified, "USD", low: 100m, high: 200m),
        };

        var result = _calculator.Summarize(opportunities, []);

        Assert.Empty(result.Realized);
        Assert.Single(result.Identified);
    }

    [Fact]
    public void Differently_cased_realized_currency_rows_are_merged_into_one_bucket()
    {
        var realized = new[]
        {
            new RealizedSavingsSnapshot("USD", 10_000m),
            new RealizedSavingsSnapshot("usd", 5_000m),
        };

        var result = _calculator.Summarize([], realized);

        var bucket = Assert.Single(result.Realized);
        Assert.Equal(15_000m, bucket.Amount);
        Assert.Equal(2, bucket.Count);
    }

    [Fact]
    public void Rejects_a_null_argument()
    {
        Assert.Throws<ArgumentNullException>(() => _calculator.Summarize(null!, []));
        Assert.Throws<ArgumentNullException>(() => _calculator.Summarize([], null!));
    }
}
