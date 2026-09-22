using Raffa.Benchmark.Contracts;
using Raffa.Chat.Application.Guards;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Market.Contracts;
using Raffa.SharedKernel;

namespace Raffa.Api.Tests;

/// <summary>
/// Persona v2.5's market safety net: when a contract lacks its annual amounts, Ask says so and gives
/// the narrowest market estimate the data supports — the contract's own quantities at market prices,
/// a comparable contract's yearly value, or the value band most comparable deals fall in — never a
/// range too wide to mean anything, and every figure a pack value the numeric guard accepts.
/// </summary>
public sealed class MarketSafetyNetTests
{
    private static readonly Guid ContractId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static MarketDeal Deal(
        string product = "Database Enterprise Edition",
        string currency = "EUR",
        string sizeBand = "500-2000",
        string annualValueBand = "250k-500k",
        decimal p25 = 1_500m,
        decimal p50 = 1_800m,
        decimal p75 = 2_100m,
        int sampleSize = 40,
        double? discount = 8,
        double? upliftCap = 4,
        int? noticeDays = 90,
        int termMonths = 12) =>
        new(
            "mock",
            $"rec-{product}-{currency}-{annualValueBand}-{sampleSize}",
            "Oracle",
            "Enterprise Software",
            product,
            currency == "EUR" ? "EU" : "CH",
            currency,
            sizeBand,
            termMonths,
            annualValueBand,
            p25,
            p50,
            p75,
            [],
            "2026-Q2",
            sampleSize,
            "mock feed",
            Representative: true,
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            DiscountAchievedPct: discount,
            UpliftCapPct: upliftCap,
            NoticeDays: noticeDays);

    private static PricedLine Line(string description, decimal? quantity, BenchmarkDistribution? benchmark, string currency = "EUR") =>
        new(null, description, quantity, null, currency, 12, benchmark, benchmark is null ? null : 40);

    [Theory]
    [InlineData("250k-500k", 250_000, 500_000)]
    [InlineData("500k-1m", 500_000, 1_000_000)]
    [InlineData("1m-5m", 1_000_000, 5_000_000)]
    public void A_bounded_band_parses_to_its_amounts(string band, int low, int high)
    {
        Assert.Equal(((decimal?)low, (decimal?)high), MarketSafetyNet.ParseBand(band));
    }

    [Fact]
    public void Open_bands_parse_to_one_bound_and_junk_to_nothing()
    {
        Assert.Equal(((decimal?)null, (decimal?)100_000), MarketSafetyNet.ParseBand("<100k"));
        Assert.Equal(((decimal?)5_000_000, (decimal?)null), MarketSafetyNet.ParseBand("5m+"));
        Assert.Null(MarketSafetyNet.ParseBand("about a million"));
    }

    [Fact]
    public void The_contracts_own_quantities_at_market_prices_come_first()
    {
        var lines = new[] { Line("Database Enterprise Edition", 200m, new BenchmarkDistribution(1_500m, 1_800m, 2_100m)) };

        var estimate = MarketSafetyNet.EstimateAnnualValue("EUR", lines, [Deal()]);

        Assert.NotNull(estimate);
        Assert.Equal(MarketSafetyNet.AnnualEstimateBasis.QuantitiesAtMarketPrices, estimate.Basis);
        Assert.Equal(300_000m, estimate.Low);
        Assert.Equal(420_000m, estimate.High);
    }

    [Fact]
    public void A_deal_priced_as_the_whole_yearly_contract_gives_its_own_narrow_range()
    {
        var premium = Deal(product: "Commercial Property Insurance", currency: "CHF", annualValueBand: "100k-250k",
            p25: 85_000m, p50: 98_000m, p75: 112_000m, sampleSize: 46);

        var estimate = MarketSafetyNet.EstimateAnnualValue("CHF", [], [premium]);

        Assert.NotNull(estimate);
        Assert.Equal(MarketSafetyNet.AnnualEstimateBasis.ComparableContractValue, estimate.Basis);
        Assert.Equal(85_000m, estimate.Low);
        Assert.Equal(112_000m, estimate.High);
        Assert.Equal("Commercial Property Insurance", estimate.Product);
    }

    [Fact]
    public void Otherwise_the_band_most_comparable_deals_fall_in()
    {
        var deals = new[] { Deal(sampleSize: 40), Deal(sampleSize: 25, sizeBand: "2000-5000"), Deal(annualValueBand: "500k-1m", sampleSize: 10) };

        var estimate = MarketSafetyNet.EstimateAnnualValue("EUR", [], deals);

        Assert.NotNull(estimate);
        Assert.Equal(MarketSafetyNet.AnnualEstimateBasis.ComparableValueBand, estimate.Basis);
        Assert.Equal(250_000m, estimate.Low);
        Assert.Equal(500_000m, estimate.High);
        Assert.Equal("500-2000", estimate.CompanySizeBand);
    }

    [Fact]
    public void A_range_too_wide_to_mean_anything_is_never_published()
    {
        Assert.Null(MarketSafetyNet.EstimateAnnualValue("EUR", [], [Deal(annualValueBand: "1m-5m")]));
    }

    [Fact]
    public void Deals_spread_over_very_different_values_give_no_made_up_middle()
    {
        var deals = new[]
        {
            Deal(product: "Database", annualValueBand: "100k-250k", sampleSize: 30),
            Deal(product: "Cloud Infrastructure", annualValueBand: "500k-1m", sampleSize: 30),
            Deal(product: "Fusion ERP", annualValueBand: "250k-500k", sampleSize: 30),
        };

        Assert.Null(MarketSafetyNet.EstimateAnnualValue("EUR", [], deals));
    }

    [Fact]
    public void Another_currencys_deals_never_estimate_this_contract()
    {
        Assert.Null(MarketSafetyNet.EstimateAnnualValue("CHF", [], [Deal(currency: "EUR")]));
    }

    [Fact]
    public void Deals_about_the_contracts_own_products_win_over_the_suppliers_other_products()
    {
        var database = Deal(product: "Database Enterprise Edition");
        var erp = Deal(product: "Fusion ERP Cloud");

        var matching = MarketSafetyNet.MatchingProducts([database, erp], [Line("Oracle Database EE processor licences", 8m, null)]);

        Assert.Equal([database], matching);
    }

    [Fact]
    public void The_estimate_item_says_it_is_a_market_estimate_and_its_figures_pass_the_numeric_guard()
    {
        var estimate = MarketSafetyNet.EstimateAnnualValue("EUR", [], [Deal()])!;

        var item = MarketSafetyNet.EstimateItem(ContractId, "Oracle", estimate);

        Assert.StartsWith("market:", item.CitationKey, StringComparison.Ordinal);
        Assert.Contains("Market estimate, not a figure from your contract", item.Snippet, StringComparison.Ordinal);
        Assert.Contains("between EUR 250,000 and EUR 500,000", item.Snippet, StringComparison.Ordinal);
        Assert.Contains("companies of 500-2000 employees", item.Snippet, StringComparison.Ordinal);
        Assert.True(NumericGuard.Validate("Per clienti simili il valore annuo è tra EUR 250,000 e EUR 500,000 [1].", [item]).Passed);
        Assert.False(NumericGuard.Validate("Per clienti simili il valore annuo è tra EUR 200,000 e EUR 600,000 [1].", [item]).Passed);
    }

    [Fact]
    public void The_terms_item_gives_narrow_ranges_or_only_the_median()
    {
        var deals = new[]
        {
            Deal(discount: 6, upliftCap: 1, noticeDays: 60),
            Deal(discount: 8, upliftCap: 2, noticeDays: 90),
            Deal(discount: 9, upliftCap: 5, noticeDays: 90),
            Deal(discount: 10, upliftCap: 9, noticeDays: 90),
        };

        var item = MarketSafetyNet.TermsItem(ContractId, "Oracle", deals)!;

        Assert.Contains("not terms of your contract", item.Snippet, StringComparison.Ordinal);
        Assert.Contains("discount achieved 7.5%–9.3%", item.Snippet, StringComparison.Ordinal);
        // Uplift caps between 1.75% and 6% are wider than the ratio allows: the median alone.
        Assert.Contains("annual uplift cap typically 3.5%", item.Snippet, StringComparison.Ordinal);
        Assert.Contains("notice period typically 90 days", item.Snippet, StringComparison.Ordinal);
        Assert.True(NumericGuard.Validate("Clienti simili ottengono uno sconto del 7.5%–9.3% e un cap del 3.5% [1].", [item]).Passed);
    }

    [Fact]
    public void Missing_fields_name_what_the_contract_lacks_and_line_totals_count_as_an_annual_amount()
    {
        var bare = Contract(annualSpend: null, lineItemAnnualTotal: null);
        var withLines = Contract(annualSpend: null, lineItemAnnualTotal: 250_000m);

        Assert.Equal(
            ["annual spend", "end date", "cancellation (notice) deadline", "renewal term", "payment terms", "priced line items"],
            MarketSafetyNet.MissingFields(bare));
        Assert.DoesNotContain("annual spend", MarketSafetyNet.MissingFields(withLines));
        Assert.Null(MarketSafetyNet.GapsItem(ContractId, "Oracle", []));
        Assert.Contains(
            "no value for: annual spend",
            MarketSafetyNet.GapsItem(ContractId, "Oracle", MarketSafetyNet.MissingFields(bare))!.Snippet,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_lead_is_honest_about_the_gap_and_labels_the_estimate_in_the_questions_language()
    {
        var estimate = MarketSafetyNet.EstimateAnnualValue("EUR", [], [Deal()]);

        var italian = MarketSafetyNet.Lead("Oracle", ["annual spend"], estimate, italian: true)!;
        var english = MarketSafetyNet.Lead("Oracle", ["annual spend"], estimate, italian: false)!;

        Assert.StartsWith("Sul contratto Oracle mancano gli importi annuali.", italian, StringComparison.Ordinal);
        Assert.Contains("per aziende di 500-2000 dipendenti il valore annuo tipico è tra EUR 250,000 e EUR 500,000", italian, StringComparison.Ordinal);
        Assert.Contains("è una stima, non un dato del tuo contratto", italian, StringComparison.Ordinal);
        Assert.StartsWith("The Oracle contract has no annual amounts on file.", english, StringComparison.Ordinal);
        Assert.Null(MarketSafetyNet.Lead("Oracle", ["end date"], estimate, italian: true));
        Assert.Null(MarketSafetyNet.Lead("Oracle", ["annual spend"], null, italian: true));
    }

    private static Contract360Result Contract(decimal? annualSpend, decimal? lineItemAnnualTotal)
    {
        var id = new EntityId(ContractId);
        return new Contract360Result(
            id,
            new Contract360Header(id, null, ContractDocumentType.OrderForm, "Active", annualSpend, null, null, null, null, null, false, null),
            new Contract360Overview("EUR", null, null, null, null, null, 1, DateTimeOffset.UtcNow),
            new Contract360Commercials(annualSpend, null, "EUR", null, false, null, 0, lineItemAnnualTotal, null),
            [],
            [],
            [],
            [],
            [],
            [],
            new Contract360Renewal(null, null, null, false, null),
            []);
    }
}
