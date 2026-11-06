using Raffa.AiFlows.Ask.Artifacts;
using Raffa.AiFlows.Shared.ContractContext;
using Raffa.Benchmark.Contracts;
using Raffa.Market.Contracts;

namespace Raffa.AiFlows.Tests.Shared;

/// <summary>
/// <see cref="MarketBandFallback"/>: a contract line whose description is not the corpus's exact
/// product name ("Microsoft 365 E5 named users / licenses" vs "Microsoft 365 E5") still gets the
/// market band of the deals whose product it names, in the contract's own currency/geography —
/// the gap that made every "compare with the market" answer say there was no benchmark.
/// </summary>
public sealed class MarketBandFallbackTests
{
    private static MarketDeal Deal(string product, string geography, string currency, decimal p25, decimal p50, decimal p75, int sample = 100) => new(
        "Internal Dataset", Guid.NewGuid().ToString("N"), "Microsoft", "Enterprise Software", product, geography, currency,
        "2000-5000", 12, "100k-500k", p25, p50, p75, [], "2026-Q2", sample, "mock", true, new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
        NoticeDays: 45, UpliftCapPct: 3, DiscountAchievedPct: 12);

    private static PricedLine Line(string description, decimal? unitPrice = 411m) =>
        new(null, description, 535m, unitPrice, "CHF", 12, null, null);

    [Fact]
    public void A_line_naming_a_corpus_product_gets_that_products_band_in_its_own_currency()
    {
        var deals = new[]
        {
            Deal("Microsoft 365 E5", "CH", "CHF", 357m, 411m, 475m),
            Deal("Microsoft 365 E5", "CH", "CHF", 326m, 371m, 413m),
            Deal("Microsoft 365 E5", "CH", "CHF", 340m, 384m, 430m),
            Deal("Microsoft 365 E5", "DE", "EUR", 1m, 2m, 3m),
            Deal("Microsoft 365 E3", "CH", "CHF", 200m, 220m, 240m),
            Deal("Microsoft 365 E5 (no Teams)", "CH", "CHF", 427m, 474m, 536m),
        };

        var filled = MarketBandFallback.Fill([Line("Microsoft 365 E5 named users / licenses")], deals, "Microsoft", "CHF", "CH");

        var band = Assert.IsType<BenchmarkDistribution>(filled[0].Benchmark);
        Assert.Equal(340m, band.P25);
        Assert.Equal(384m, band.P50);
        Assert.Equal(430m, band.P75);
        Assert.Equal(300, filled[0].SampleSize);
        Assert.Contains("Microsoft 365 E5", filled[0].AdapterName);
        Assert.Contains("representative", filled[0].AdapterName);
    }

    [Fact]
    public void A_line_with_a_band_already_is_kept_and_an_unrelated_line_stays_without_one()
    {
        var existing = Line("Microsoft 365 E5") with { Benchmark = new BenchmarkDistribution(1m, 2m, 3m) };
        var deals = new[] { Deal("Microsoft 365 E5", "CH", "CHF", 357m, 411m, 475m) };

        var filled = MarketBandFallback.Fill([existing, Line("One-time implementation / onboarding fee")], deals, "Microsoft", "CHF", "CH");

        Assert.Equal(2m, filled[0].Benchmark!.P50);
        Assert.Null(filled[1].Benchmark);
    }

    [Fact]
    public void Deals_in_another_currency_never_price_the_line()
    {
        var deals = new[] { Deal("Microsoft 365 E5", "DE", "EUR", 300m, 350m, 400m) };

        var filled = MarketBandFallback.Fill([Line("Microsoft 365 E5 named users")], deals, "Microsoft", "CHF", null);

        Assert.Null(filled[0].Benchmark);
    }

    [Fact]
    public void The_artifact_line_fact_carries_the_position_and_delta_vs_the_median()
    {
        var line = Line("Microsoft 365 E5 named users") with { Benchmark = new BenchmarkDistribution(357m, 400m, 475m), SampleSize = 300 };

        var market = ArtifactComposer.BuildLineMarketFact(line, 535m);

        Assert.NotNull(market);
        Assert.Equal(400m, market!.P50);
        Assert.Equal(11m, market.DeltaVsP50);
        Assert.Equal(2.8m, market.DeltaVsP50Pct);
        Assert.Equal(5885m, market.AnnualDeltaVsP50);
        Assert.Equal("p50-p75", market.Position);
    }
}
