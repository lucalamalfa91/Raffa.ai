using Raffa.Quotes.Application.Outcome;

namespace Raffa.Quotes.Tests;

/// <summary>
/// Proves <see cref="NegotiationOutcomeCalculator"/> — task E05/F03/US02/T01 (negotiation-outcome);
/// parent story us-02-outcome-capture AC-1 "saving/discount" — with no database, no HTTP call and
/// no LLM call anywhere in the path (Appendix C rule 6). Mirrors
/// <c>Raffa.Quotes.Tests.NegotiationStrategyCalculatorTests</c>'s own shape/style (this module's
/// other "pure calculator, determinism" test).
/// </summary>
public sealed class NegotiationOutcomeCalculatorTests
{
    [Fact]
    public void Reproduces_spec_12_2s_own_worked_example()
    {
        // spec §12.2: Original Quote 520k, Final Price 435k -> Realized Saving 85k, Discount ~16.3%.
        var result = NegotiationOutcomeCalculator.Compute(520_000m, 435_000m);

        Assert.Equal(85_000m, result.RealizedSaving);
        Assert.Equal(Math.Round(16.3m, 1), Math.Round(result.DiscountPercent, 1));
    }

    [Fact]
    public void Realized_saving_is_original_minus_final()
    {
        var result = NegotiationOutcomeCalculator.Compute(1000m, 800m);

        Assert.Equal(200m, result.RealizedSaving);
    }

    [Fact]
    public void Discount_percent_is_realized_saving_as_a_percentage_of_the_original_total()
    {
        var result = NegotiationOutcomeCalculator.Compute(1000m, 800m);

        Assert.Equal(20m, result.DiscountPercent);
    }

    [Fact]
    public void Is_honest_with_a_negative_saving_when_the_final_price_exceeds_the_original_total()
    {
        // Never clamped to zero (Appendix C rule 10) — an unusual but real outcome.
        var result = NegotiationOutcomeCalculator.Compute(1000m, 1200m);

        Assert.Equal(-200m, result.RealizedSaving);
        Assert.Equal(-20m, result.DiscountPercent);
    }

    [Fact]
    public void Zero_saving_when_the_final_price_equals_the_original_total()
    {
        var result = NegotiationOutcomeCalculator.Compute(1000m, 1000m);

        Assert.Equal(0m, result.RealizedSaving);
        Assert.Equal(0m, result.DiscountPercent);
    }

    [Fact]
    public void Same_inputs_produce_the_same_result_every_time()
    {
        var first = NegotiationOutcomeCalculator.Compute(520_000m, 435_000m);
        var second = NegotiationOutcomeCalculator.Compute(520_000m, 435_000m);

        Assert.Equal(first.RealizedSaving, second.RealizedSaving);
        Assert.Equal(first.DiscountPercent, second.DiscountPercent);
    }
}
