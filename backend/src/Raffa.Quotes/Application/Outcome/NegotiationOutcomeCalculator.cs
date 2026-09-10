namespace Raffa.Quotes.Application.Outcome;

/// <summary>
/// The deterministic negotiation-outcome calculator (task E05/F03/US02/T01, negotiation-outcome;
/// parent story us-02-outcome-capture AC-1 "records original/target/final/saving/discount/
/// duration/levers"; spec §12.2's own worked example). Pure and synchronous — no database call, no
/// HTTP call, no LLM call — the same <paramref name="originalQuoteTotal"/>/<paramref
/// name="finalPrice"/> input always produces the same <see cref="NegotiationOutcomeCalculation"/>
/// (Appendix C rule 6: "prefer deterministic arithmetic ... to LLM reasoning"), the same
/// determinism convention <c>Raffa.Quotes.Application.Strategy.NegotiationStrategyCalculator
/// .Compute</c> and <c>Assessment.TargetSavingCalculator.Compute</c> already established for this
/// module's other calculators.
/// </summary>
public static class NegotiationOutcomeCalculator
{
    /// <summary>
    /// Reproduces spec §12.2's own illustrative example exactly: original 520,000, final 435,000 -&gt;
    /// saving 85,000 (520,000 - 435,000), discount ~16.35% (85,000 / 520,000 * 100 — the spec's own
    /// "16.3%" is that same figure rounded for display, not a different formula). Never clamps
    /// <see cref="NegotiationOutcomeCalculation.RealizedSaving"/> at zero: a <paramref
    /// name="finalPrice"/> above <paramref name="originalQuoteTotal"/> is an honest, if unusual,
    /// negative saving (Appendix C rule 10 — no fabricated precision, not even an optimistic floor).
    /// </summary>
    public static NegotiationOutcomeCalculation Compute(decimal originalQuoteTotal, decimal finalPrice)
    {
        var realizedSaving = originalQuoteTotal - finalPrice;

        // originalQuoteTotal is always > 0 by the time this runs
        // (NegotiationOutcomeService.OriginalQuoteTotalMustBePositiveError rejects anything else
        // before this calculator is ever called) — no division-by-zero guard needed, but the
        // calculator itself stays honest about that precondition rather than silently assuming it.
        var discountPercent = realizedSaving / originalQuoteTotal * 100m;

        return new NegotiationOutcomeCalculation(realizedSaving, discountPercent);
    }
}
