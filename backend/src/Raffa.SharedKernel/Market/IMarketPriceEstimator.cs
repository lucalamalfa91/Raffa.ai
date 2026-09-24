namespace Raffa.SharedKernel.Market;

/// <summary>
/// Cross-module port (same split as <see cref="IMarketPriceMatcher"/>: port here, implementation in
/// <c>Raffa.Market</c>) for the <b>last resort</b> behind the market column: an estimated band for a
/// line the matcher could not price — no record for its own product, none similar, in the
/// contract's own currency.
///
/// <para>
/// An estimate is never market data. It is only ever asked for a line whose
/// <see cref="IMarketPriceMatcher"/> result was <see langword="null"/>, is stored apart from the
/// matched band (so every reader of the matched band — savings, insights, Ask — never sees it), and
/// is dropped the moment a real match exists. <see cref="MarketPriceEstimate.Kind"/> says how it was
/// obtained, <see cref="MarketPriceEstimate.Basis"/> says from what, in words a buyer can check.
/// </para>
/// </summary>
public interface IMarketPriceEstimator
{
    /// <summary>
    /// One entry per <paramref name="lines"/> element, in the same order: an estimated band in
    /// <see cref="MarketPriceContext.Currency"/>, or <see langword="null"/> when not even an
    /// estimate can be made. Never throws for a failed estimate — a line simply gets none.
    /// </summary>
    Task<IReadOnlyList<MarketPriceEstimate?>> EstimateAsync(
        MarketPriceContext context,
        IReadOnlyList<MarketPriceLine> lines,
        CancellationToken cancellationToken);
}

/// <summary>How an estimate was obtained, from the most to the least grounded.</summary>
public enum MarketEstimateKind
{
    /// <summary>The line's own product (or a similar one) exists in the corpus, but only in another
    /// currency: its band converted at an indicative fixed rate.</summary>
    Converted,

    /// <summary>No record anywhere close: an AI estimate of what customers typically pay, with its
    /// reasoning — the least grounded figure, and labelled so.</summary>
    AiEstimate,
}

/// <summary>
/// An estimated unit-price band for one line, in the contract's own currency. Not a market record:
/// it has no record id and no sample, only the <see cref="Basis"/> it was worked out from.
/// </summary>
/// <param name="Kind">How it was obtained — see <see cref="MarketEstimateKind"/>.</param>
/// <param name="Currency">Always the contract's own currency.</param>
/// <param name="UnitPriceP25">Low end of the estimated band.</param>
/// <param name="UnitPriceP50">The estimated typical unit price.</param>
/// <param name="UnitPriceP75">High end of the estimated band.</param>
/// <param name="Basis">What the figure rests on, in plain words, e.g. <c>"Ariba Buying (SAP), EU
/// market, EUR 118 converted at 0.94"</c> or the AI's own one-line reasoning.</param>
/// <param name="Product">The product the estimate is for (the converted record's product, or the
/// line's own product as the estimator understood it).</param>
public sealed record MarketPriceEstimate(
    MarketEstimateKind Kind,
    string Currency,
    decimal UnitPriceP25,
    decimal UnitPriceP50,
    decimal UnitPriceP75,
    string Basis,
    string? Product);
