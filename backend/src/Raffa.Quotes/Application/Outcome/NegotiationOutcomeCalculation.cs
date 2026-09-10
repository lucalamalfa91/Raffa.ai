namespace Raffa.Quotes.Application.Outcome;

/// <summary>
/// The two deterministic figures <see cref="NegotiationOutcomeCalculator.Compute"/> derives from a
/// captured outcome's own <c>originalQuoteTotal</c>/<c>finalPrice</c> (task E05/F03/US02/T01,
/// negotiation-outcome; parent story us-02-outcome-capture AC-1 "saving/discount"; Appendix C rule
/// 6). Mirrors <c>Raffa.Quotes.Application.Assessment.LineTargetSaving</c>'s own "a small,
/// named-field record for a calculator's pure output" shape.
/// </summary>
/// <param name="RealizedSaving"><c>originalQuoteTotal - finalPrice</c> — can be negative when the
/// final price ends up above the original quote (never clamped; see
/// <see cref="Domain.NegotiationOutcome.FinalPrice"/>'s own doc comment).</param>
/// <param name="DiscountPercent"><see cref="RealizedSaving"/> expressed as a percentage of
/// <c>originalQuoteTotal</c>.</param>
public readonly record struct NegotiationOutcomeCalculation(decimal RealizedSaving, decimal DiscountPercent);
