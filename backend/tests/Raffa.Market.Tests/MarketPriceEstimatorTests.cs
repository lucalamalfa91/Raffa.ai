using Raffa.AiGateway;
using Raffa.AiGateway.Contracts;
using Raffa.Market.Mock;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Market;

namespace Raffa.Market.Tests;

/// <summary>
/// <see cref="MarketPriceEstimator"/>: the last resort for a line the matcher could not price — a
/// record in another currency converted at an indicative rate first, an AI band second, and never
/// a band that is not a sane P25 ≤ P50 ≤ P75.
/// </summary>
public sealed class MarketPriceEstimatorTests
{
    private static readonly MarketPriceContext SapChf12 = new("SAP (Schweiz) AG", "CHF", 12);

    [Fact]
    public void A_product_only_in_another_currency_is_converted_and_says_so()
    {
        var deals = new[]
        {
            SampleDeal.Create(recordId: "S4-EU", supplier: "SAP", product: "S/4HANA Cloud Professional Use", geography: "EU", currency: "EUR",
                unitPriceP25: 100m, unitPriceP50: 113m, unitPriceP75: 130m),
        };

        var estimate = Assert.Single(MarketPriceEstimator.Convert(
            SapChf12, [new MarketPriceLine("S/4HANA Cloud Professional Use FUE", null)], deals));

        Assert.NotNull(estimate);
        Assert.Equal(MarketEstimateKind.Converted, estimate.Kind);
        Assert.Equal("CHF", estimate.Currency);
        var rate = MarketPriceEstimator.UsdPerUnit["EUR"] / MarketPriceEstimator.UsdPerUnit["CHF"];
        Assert.Equal(Math.Round(113m * rate, 2), estimate.UnitPriceP50);
        Assert.Contains("EUR 113", estimate.Basis, StringComparison.Ordinal);
        Assert.Contains("indicative fixed rate", estimate.Basis, StringComparison.Ordinal);
    }

    [Fact]
    public void A_record_in_the_contracts_own_currency_is_never_converted()
    {
        // Same-currency records are the matcher's; an estimate only ever converts other currencies.
        var deals = new[] { SampleDeal.Create(supplier: "SAP", product: "Ariba Buying", geography: "CH", currency: "CHF") };

        var estimate = Assert.Single(MarketPriceEstimator.Convert(SapChf12, [new MarketPriceLine("Ariba Buying users", null)], deals));

        Assert.Null(estimate);
    }

    [Fact]
    public async Task A_line_with_nothing_in_the_corpus_gets_an_ai_band_and_a_malformed_band_is_dropped()
    {
        var gateway = new AnalyzeOnlyGateway("""
            {"estimates":[
              {"index":0,"canEstimate":true,"product":"Coupa Procure-to-Pay","p25":110,"p50":140,"p75":175,"rationale":"Typical P2P suite seat price."},
              {"index":1,"canEstimate":true,"product":"Onboarding","p25":9000,"p50":5000,"p75":12000,"rationale":"x"}
            ]}
            """);
        var estimator = new MarketPriceEstimator(new ProviderMarketDealLookup(new MockMarketIntelligenceProvider()), gateway);

        var estimates = await estimator.EstimateAsync(
            new MarketPriceContext("Coupa Software", "CHF", 12),
            [new MarketPriceLine("Coupa Procure-to-Pay named users / licenses", null), new MarketPriceLine("One-time implementation / onboarding fee", null)],
            CancellationToken.None);

        Assert.Equal(2, estimates.Count);
        var coupa = estimates[0];
        Assert.NotNull(coupa);
        Assert.Equal(MarketEstimateKind.AiEstimate, coupa.Kind);
        Assert.Equal("CHF", coupa.Currency);
        Assert.Equal(140m, coupa.UnitPriceP50);
        Assert.Equal("Typical P2P suite seat price.", coupa.Basis);
        Assert.Null(estimates[1]);

        // The model is never shown what the customer pays, only the line and the corpus's own level.
        var request = Assert.Single(gateway.Requests);
        Assert.Equal(MarketPriceEstimator.AgentName, request.AgentName);
        Assert.Contains("referencePrices", request.InputJson, StringComparison.Ordinal);
        Assert.DoesNotContain("unitPrice\"", request.InputJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_gateway_and_nothing_to_convert_means_no_estimate()
    {
        var estimator = new MarketPriceEstimator(new ProviderMarketDealLookup(new MockMarketIntelligenceProvider()));

        var estimate = Assert.Single(await estimator.EstimateAsync(
            new MarketPriceContext("Coupa Software", "CHF", 12),
            [new MarketPriceLine("Coupa Procure-to-Pay named users / licenses", null)],
            CancellationToken.None));

        Assert.Null(estimate);
    }

    [Fact]
    public void A_payload_of_another_shape_parses_to_nothing() =>
        Assert.Empty(MarketPriceEstimator.Parse("""{"plays":[],"verdict":"x"}"""));

    private sealed class AnalyzeOnlyGateway(string payloadJson) : IAiGateway
    {
        public List<AiAnalysisRequest> Requests { get; } = [];

        public Task<Result<AiAnalysisResult>> AnalyzeAsync(AiAnalysisRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(Result<AiAnalysisResult>.Success(new AiAnalysisResult(
                payloadJson, new AiCallMetadata("fake", "1", request.PromptVersion, DateTimeOffset.UnixEpoch, "hash"))));
        }

        public Task<Result<AiClassificationResult>> ClassifyAsync(AiClassificationRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiExtractionResult>> ExtractAsync(AiExtractionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiEmbeddingResult>> EmbedAsync(AiEmbeddingRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiAnswerResult>> AnswerAsync(AiAnswerRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<AiOcrResult>> OcrAsync(AiOcrRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
