using Raffa.Market.Contracts;
using Raffa.Market.Mock;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel.Market;

namespace Raffa.Market.Tests;

/// <summary>
/// <see cref="MarketPriceMatcher"/>: a contract line is priced against the corpus only when the
/// supplier, the product it names and the currency all line up — never an edition it does not
/// name, never another currency, never a thin sample.
/// </summary>
public sealed class MarketPriceMatcherTests
{
    private static readonly MarketPriceContext SalesforceGbp12 = new("Salesforce, Inc.", "GBP", 12);

    [Fact]
    public void A_line_naming_an_edition_matches_that_edition_not_its_siblings()
    {
        var deals = new[]
        {
            SampleDeal.Create(recordId: "ENT", product: "Sales Cloud Enterprise", geography: "UK", currency: "GBP", unitPriceP50: 120m),
            SampleDeal.Create(recordId: "UNL", product: "Sales Cloud Unlimited", geography: "UK", currency: "GBP", unitPriceP50: 240m),
        };

        var matches = MarketPriceMatcher.Match(
            SalesforceGbp12,
            [new MarketPriceLine("Sales Cloud Unlimited named users / licenses", "named users / licenses")],
            deals);

        var match = Assert.Single(matches);
        Assert.NotNull(match);
        Assert.Equal("UNL", match.RecordId);
        Assert.Equal("Sales Cloud Unlimited", match.Product);
        Assert.Equal(240m, match.UnitPriceP50);
        Assert.Equal("representative market data · mock feed · updated 2026-06-20", match.Provenance);
    }

    [Fact]
    public void A_line_the_corpus_has_no_product_for_stays_unmatched()
    {
        var deals = new[] { SampleDeal.Create(product: "Service Cloud Enterprise", geography: "UK", currency: "GBP") };

        var matches = MarketPriceMatcher.Match(
            SalesforceGbp12,
            [new MarketPriceLine("Premium Support annual support service", "enterprise package")],
            deals);

        Assert.Null(Assert.Single(matches));
    }

    [Fact]
    public void Only_the_contracts_own_currency_is_ever_compared_and_its_home_region_is_preferred()
    {
        var line = new MarketPriceLine("Sales Cloud Enterprise", null);
        var deals = new[]
        {
            SampleDeal.Create(recordId: "EUR", product: "Sales Cloud Enterprise", geography: "EU", currency: "EUR", sampleSize: 500),
            SampleDeal.Create(recordId: "APAC", product: "Sales Cloud Enterprise", geography: "APAC", currency: "USD", sampleSize: 400),
            SampleDeal.Create(recordId: "US", product: "Sales Cloud Enterprise", geography: "US", currency: "USD", sampleSize: 20),
        };

        Assert.Equal("US", MarketPriceMatcher.Match(new("Salesforce", "USD", 12), [line], deals)[0]?.RecordId);
        Assert.Equal("EUR", MarketPriceMatcher.Match(new("Salesforce", "eur", 12), [line], deals)[0]?.RecordId);
        Assert.Null(MarketPriceMatcher.Match(new("Salesforce", "GBP", 12), [line], deals)[0]);
    }

    [Fact]
    public void The_contracts_own_term_wins_then_the_larger_sample_and_a_thin_sample_never_counts()
    {
        var line = new MarketPriceLine("Sales Cloud Enterprise user licences", null);
        var deals = new[]
        {
            SampleDeal.Create(recordId: "T36-BIG", termMonths: 36, sampleSize: 400),
            SampleDeal.Create(recordId: "T12-SMALL", termMonths: 12, sampleSize: 12),
            SampleDeal.Create(recordId: "T12-BIG", termMonths: 12, sampleSize: 80),
            SampleDeal.Create(recordId: "T12-THIN", termMonths: 12, sampleSize: 4),
        };

        Assert.Equal("T12-BIG", MarketPriceMatcher.Match(new("Salesforce", "CHF", 12), [line], deals)[0]?.RecordId);
        Assert.Equal("T36-BIG", MarketPriceMatcher.Match(new("Salesforce", "CHF", 36), [line], deals)[0]?.RecordId);
        Assert.Null(MarketPriceMatcher.Match(new("Salesforce", "CHF", 12), [line], [deals[3]])[0]);
    }

    [Fact]
    public void An_equal_sku_matches_even_when_the_description_does_not_name_the_product()
    {
        var deals = new[] { SampleDeal.Create(product: "Sales Cloud Starter", sku: "SFDC-SALES-STARTER") };

        var match = MarketPriceMatcher.Match(
            new("Salesforce", "CHF", 12),
            [new MarketPriceLine("CRM seats", "sfdc sales starter")],
            deals)[0];

        Assert.Equal("Sales Cloud Starter", match?.Product);
    }

    [Fact]
    public async Task No_supplier_means_no_match_and_no_corpus_read()
    {
        var lookup = new CountingLookup([SampleDeal.Create()]);
        var matcher = new MarketPriceMatcher(lookup);

        var matches = await matcher.MatchAsync(
            new MarketPriceContext(null, "CHF", 12), [new MarketPriceLine("Sales Cloud Enterprise", null)], CancellationToken.None);

        Assert.Null(Assert.Single(matches));
        Assert.Equal(0, lookup.Calls);
    }

    [Fact]
    public async Task Against_the_checked_in_feed_a_uk_unlimited_line_gets_a_gbp_band_and_premium_support_gets_none()
    {
        var matcher = new MarketPriceMatcher(new ProviderMarketDealLookup(new MockMarketIntelligenceProvider()));

        var matches = await matcher.MatchAsync(
            SalesforceGbp12,
            [
                new MarketPriceLine("Sales Cloud Unlimited named users / licenses", "named users / licenses"),
                new MarketPriceLine("Premium Support annual support service", "enterprise package"),
            ],
            CancellationToken.None);

        Assert.Equal(2, matches.Count);
        var unlimited = matches[0];
        Assert.NotNull(unlimited);
        Assert.Equal("Sales Cloud Unlimited", unlimited.Product);
        Assert.Equal("GBP", unlimited.Currency);
        Assert.Equal("UK", unlimited.Geography);
        Assert.True(unlimited.SampleSize >= MarketPriceMatcher.MinimumSampleSize);
        Assert.True(unlimited.UnitPriceP25 <= unlimited.UnitPriceP50 && unlimited.UnitPriceP50 <= unlimited.UnitPriceP75);
        Assert.Null(matches[1]);
    }

    private sealed class CountingLookup(IReadOnlyList<MarketDeal> deals) : IMarketDealLookup
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<MarketDeal>> GetBySupplierAsync(string supplierName, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(deals);
        }
    }
}
