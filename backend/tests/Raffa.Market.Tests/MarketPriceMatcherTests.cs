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
        Assert.Equal(MarketMatchKind.Exact, match.Kind);
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

    [Fact]
    public void A_line_naming_two_products_is_priced_as_their_bundle_not_as_one_of_them()
    {
        var deals = new[]
        {
            SampleDeal.Create(recordId: "JIRA", supplier: "Atlassian", product: "Jira Software Premium", geography: "UK", currency: "GBP",
                unitPriceP25: 109m, unitPriceP50: 120m, unitPriceP75: 131m, sampleSize: 6),
            SampleDeal.Create(recordId: "CONF", supplier: "Atlassian", product: "Confluence Premium", geography: "UK", currency: "GBP",
                unitPriceP25: 76.3m, unitPriceP50: 85m, unitPriceP75: 94m, sampleSize: 48),
        };

        var match = MarketPriceMatcher.Match(
            new("Atlassian", "GBP", 12),
            [new MarketPriceLine("Jira Software Premium + Confluence Premium", "named users / licenses")],
            deals)[0];

        Assert.NotNull(match);
        Assert.Equal(MarketMatchKind.Bundle, match.Kind);
        Assert.Equal("Jira Software Premium + Confluence Premium", match.Product);
        Assert.Equal("JIRA+CONF", match.RecordId);
        Assert.Equal(185.3m, match.UnitPriceP25);
        Assert.Equal(205m, match.UnitPriceP50);
        Assert.Equal(225m, match.UnitPriceP75);
        Assert.Equal(6, match.SampleSize);
    }

    [Fact]
    public void Two_editions_of_one_product_or_a_family_and_its_edition_are_never_a_bundle()
    {
        var editions = new[]
        {
            SampleDeal.Create(recordId: "E3", supplier: "Microsoft", product: "Microsoft 365 E3", geography: "UK", currency: "GBP"),
            SampleDeal.Create(recordId: "E5", supplier: "Microsoft", product: "Microsoft 365 E5", geography: "UK", currency: "GBP"),
        };
        var mixed = MarketPriceMatcher.Match(
            new("Microsoft", "GBP", 12), [new MarketPriceLine("Microsoft 365 E3 and E5 seats", null)], editions)[0];
        Assert.Equal(MarketMatchKind.Exact, mixed?.Kind);

        var family = new[]
        {
            SampleDeal.Create(recordId: "FAMILY", product: "Sales Cloud", geography: "UK", currency: "GBP"),
            SampleDeal.Create(recordId: "UNL", product: "Sales Cloud Unlimited", geography: "UK", currency: "GBP"),
        };
        var edition = MarketPriceMatcher.Match(SalesforceGbp12, [new MarketPriceLine("Sales Cloud Unlimited", null)], family)[0];
        Assert.Equal("UNL", edition?.RecordId);
        Assert.Equal(MarketMatchKind.Exact, edition?.Kind);
    }

    [Fact]
    public void With_no_record_for_its_own_product_a_line_gets_a_similar_product_from_customers_of_the_same_type()
    {
        var deals = new[]
        {
            SampleDeal.Create(recordId: "BIG-BUYER", supplier: "Atlassian", product: "Confluence Premium", geography: "UK", currency: "GBP",
                annualValueBand: "1m-5m", sampleSize: 400),
            SampleDeal.Create(recordId: "SAME-TYPE", supplier: "Atlassian", product: "Confluence Premium", geography: "UK", currency: "GBP",
                annualValueBand: "100k-250k", sampleSize: 48),
            SampleDeal.Create(recordId: "JIRA", supplier: "Atlassian", product: "Jira Software Enterprise", geography: "UK", currency: "GBP"),
        };

        var match = MarketPriceMatcher.Match(
            new("Atlassian", "GBP", 12, AnnualValue: 167_000m),
            [new MarketPriceLine("Confluence Enterprise named users", null)],
            deals)[0];

        Assert.NotNull(match);
        Assert.Equal(MarketMatchKind.Similar, match.Kind);
        Assert.Equal("SAME-TYPE", match.RecordId);
        Assert.Equal("Confluence Premium", match.Product);
    }

    [Fact]
    public void A_similar_product_may_come_from_another_supplier_in_the_same_market_but_the_suppliers_own_comes_first()
    {
        var salesforce = new[] { SampleDeal.Create(recordId: "SFDC", product: "Sales Cloud Enterprise", geography: "UK", currency: "GBP") };
        var market = new[]
        {
            SampleDeal.Create(recordId: "SLACK", supplier: "Slack", product: "Enterprise Grid", geography: "UK", currency: "GBP"),
            SampleDeal.Create(recordId: "SFDC", product: "Sales Cloud Enterprise", geography: "UK", currency: "GBP"),
        };

        var fromMarket = MarketPriceMatcher.Match(
            SalesforceGbp12, [new MarketPriceLine("Slack Enterprise Grid seats", null)], salesforce, market)[0];
        Assert.Equal("SLACK", fromMarket?.RecordId);
        Assert.Equal(MarketMatchKind.Similar, fromMarket?.Kind);

        var ownCatalogue = MarketPriceMatcher.Match(
            SalesforceGbp12, [new MarketPriceLine("Sales Cloud Performance", null)], salesforce, market)[0];
        Assert.Equal("SFDC", ownCatalogue?.RecordId);
        Assert.Equal(MarketMatchKind.Similar, ownCatalogue?.Kind);
    }

    [Fact]
    public void Edition_and_packaging_words_alone_never_make_a_product_similar()
    {
        var deals = new[]
        {
            SampleDeal.Create(recordId: "JSM", supplier: "Atlassian", product: "Jira Service Management Premium", geography: "UK", currency: "GBP"),
            SampleDeal.Create(recordId: "SC", supplier: "Atlassian", product: "Service Cloud Enterprise", geography: "UK", currency: "GBP"),
            SampleDeal.Create(recordId: "JSP", supplier: "Atlassian", product: "Jira Software Premium", geography: "UK", currency: "GBP"),
        };

        var match = MarketPriceMatcher.Match(
            new("Atlassian", "GBP", 12),
            [new MarketPriceLine("Premium Support annual support service", "enterprise package")],
            deals,
            deals)[0];

        Assert.Null(match);
    }

    [Theory]
    [InlineData("<100k", 99_000, true)]
    [InlineData("<100k", 100_000, false)]
    [InlineData("100k-250k", 167_000, true)]
    [InlineData("1m-5m", 5_000_000, false)]
    [InlineData("5m+", 6_000_000, true)]
    [InlineData("unknown", 1_000, false)]
    public void A_customer_of_the_same_type_is_one_whose_annual_value_falls_in_the_records_band(string band, double value, bool expected) =>
        Assert.Equal(expected, MarketPriceMatcher.IsInValueBand(band, (decimal)value));

    [Fact]
    public void An_unknown_annual_value_is_never_the_same_type() =>
        Assert.False(MarketPriceMatcher.IsInValueBand("100k-250k", null));

    [Fact]
    public async Task Against_the_checked_in_feed_a_jira_and_confluence_line_is_their_uk_bundle()
    {
        var matcher = new MarketPriceMatcher(new ProviderMarketDealLookup(new MockMarketIntelligenceProvider()));

        var matches = await matcher.MatchAsync(
            new MarketPriceContext("Atlassian Pty Ltd", "GBP", 12, AnnualValue: 167_000m),
            [
                new MarketPriceLine("Jira Software Premium + Confluence Premium", "named users / licenses"),
                new MarketPriceLine("Premium Support annual support service", "enterprise package"),
            ],
            CancellationToken.None);

        var bundle = matches[0];
        Assert.NotNull(bundle);
        Assert.Equal(MarketMatchKind.Bundle, bundle.Kind);
        Assert.Equal("Jira Software Premium + Confluence Premium", bundle.Product);
        Assert.Equal("GBP", bundle.Currency);
        Assert.Equal("UK", bundle.Geography);
        Assert.True(bundle.UnitPriceP25 <= bundle.UnitPriceP50 && bundle.UnitPriceP50 <= bundle.UnitPriceP75);
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
