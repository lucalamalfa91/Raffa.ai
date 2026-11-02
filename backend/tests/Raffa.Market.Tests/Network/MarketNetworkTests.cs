using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure.Entities;
using Raffa.Market.Network;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Market;

namespace Raffa.Market.Tests.Network;

public sealed class MarketNetworkTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    private static MarketObservationEntity Obs(
        string workspace, decimal? price, string supplier = "DocuSign", string product = "eSignature Business Pro", int term = 24,
        double? discount = null, double? uplift = null, int? notice = null, string? payment = null, DateTimeOffset? at = null, string currency = "CHF") => new()
    {
        Id = Guid.NewGuid(),
        WorkspaceKey = workspace,
        ObservationKey = Guid.NewGuid().ToString("N"),
        SupplierKey = supplier.ToLowerInvariant(),
        Supplier = supplier,
        Product = product,
        Currency = currency,
        TermMonths = term,
        UnitPrice = price,
        DiscountPct = discount,
        UpliftCapPct = uplift,
        NoticeDays = notice,
        PaymentTerms = payment,
        Source = "supplier-reply",
        ObservedAt = at ?? Now,
    };

    [Fact]
    public void Nothing_is_published_below_the_workspace_threshold()
    {
        var deals = MarketNetworkDealBuilder.Build([Obs("a", 40), Obs("b", 44)], minWorkspaces: 3);

        Assert.Empty(deals);
    }

    [Fact]
    public void Enough_workspaces_publish_one_deal_with_real_percentiles()
    {
        var deals = MarketNetworkDealBuilder.Build(
            [Obs("a", 40, discount: 8, uplift: 3, notice: 60, payment: "quarterly"), Obs("b", 44, discount: 10, uplift: 3, notice: 90, payment: "quarterly"), Obs("c", 52, discount: 6, uplift: 4, notice: 60, payment: "annual")],
            minWorkspaces: 3);

        var deal = Assert.Single(deals);
        Assert.Equal(MarketNetworkDealBuilder.Provider, deal.Provider);
        Assert.Equal(3, deal.SampleSize);
        Assert.Equal(44m, deal.UnitPriceP50);
        Assert.Equal(42m, deal.UnitPriceP25);
        Assert.Equal(48m, deal.UnitPriceP75);
        Assert.True(deal.UnitPriceP25 <= deal.UnitPriceP50 && deal.UnitPriceP50 <= deal.UnitPriceP75);
        Assert.Equal(8, deal.DiscountAchievedPct);
        Assert.Equal(3, deal.UpliftCapPct);
        Assert.Equal(60, deal.NoticeDays);
        Assert.Null(deal.PaymentTerms); // only two of three said "quarterly": below the threshold, so not shared
        Assert.False(deal.Representative);
        Assert.Contains("anonymised", deal.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void One_workspace_counts_once_whatever_it_sends()
    {
        var spam = Enumerable.Range(0, 10).Select(i => Obs("same", 40 + i, at: Now.AddDays(i))).ToList();

        Assert.Empty(MarketNetworkDealBuilder.Build(spam, minWorkspaces: 3));
        var withOthers = MarketNetworkDealBuilder.Build([.. spam, Obs("b", 44), Obs("c", 46)], minWorkspaces: 3);
        Assert.Equal(3, Assert.Single(withOthers).SampleSize);
    }

    [Fact]
    public void Terms_products_and_currencies_are_never_mixed()
    {
        var obs = new List<MarketObservationEntity>();
        foreach (var w in new[] { "a", "b", "c" })
        {
            obs.Add(Obs(w, 40));
            obs.Add(Obs(w, 30, term: 12));
            obs.Add(Obs(w, 41, currency: "EUR"));
        }

        var deals = MarketNetworkDealBuilder.Build(obs, 3);

        Assert.Equal(3, deals.Count);
        Assert.Equal(3, deals.Select(d => d.RecordId).Distinct().Count());
    }

    [Fact]
    public void Every_published_deal_clears_the_data_quality_rules_of_the_ingestion()
    {
        var deals = MarketNetworkDealBuilder.Build(
            [Obs("a", 40, discount: 8), Obs("b", 44, discount: 10), Obs("c", 52, discount: 6)], 3);

        var deal = Assert.Single(deals);
        Assert.Empty(Raffa.Market.Ingestion.MarketDealRules.Violations(deal, Now));
    }

    [Fact]
    public void An_observation_without_a_price_never_becomes_a_deal()
    {
        Assert.Empty(MarketNetworkDealBuilder.Build([Obs("a", null, discount: 5), Obs("b", null, discount: 6), Obs("c", null, discount: 7)], 3));
    }

    [Fact]
    public void A_long_record_id_stays_within_the_column_and_is_stable()
    {
        var a = MarketNetworkDealBuilder.RecordId(new string('x', 150), new string('y', 150), 24, "CHF");

        Assert.True(a.Length <= 200);
        Assert.Equal(a, MarketNetworkDealBuilder.RecordId(new string('x', 150), new string('y', 150), 24, "CHF"));
    }

    [Fact]
    public void The_recorder_keeps_only_plausible_values_and_nothing_identifying()
    {
        var clean = MarketObservationRecorder.Sanitize(new SupplierReplyObservation(
            "  DocuSign ", "e-signature", "CH", "chf", "eSignature", "ESIG-1", "seat / month", 47, 24, 150, 80, 9999, "quarterly in advance; call anna@x.com", Now));

        Assert.NotNull(clean);
        Assert.Equal("DocuSign", clean!.Supplier);
        Assert.Equal("CHF", clean.Currency);
        Assert.Null(clean.DiscountPct);     // 150% is not a discount
        Assert.Null(clean.UpliftCapPct);    // 80% is not an uplift cap
        Assert.Null(clean.NoticeDays);      // 9999 days is not a notice period
        Assert.Null(clean.PaymentTerms);    // an email or free text never goes to the shared store
        Assert.Equal(47m, clean.UnitPrice);
    }

    [Fact]
    public void A_reply_with_nothing_worth_keeping_is_dropped()
    {
        Assert.Null(MarketObservationRecorder.Sanitize(new SupplierReplyObservation("Acme", null, null, null, null, null, null, null, 12, null, null, null, null, Now)));
    }

    [Fact]
    public void Keys_are_one_way_and_depend_on_the_secret()
    {
        var tenant = Guid.NewGuid().ToString();
        var a = MarketObservationRecorder.Hash("secret-1", "ws", tenant);

        Assert.Equal(64, a.Length);
        Assert.DoesNotContain(tenant, a, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(a, MarketObservationRecorder.Hash("secret-1", "ws", tenant));
        Assert.NotEqual(a, MarketObservationRecorder.Hash("secret-2", "ws", tenant));
    }

    [Fact]
    public async Task The_provider_adds_network_deals_to_the_feed_and_survives_a_network_failure()
    {
        var feedDeal = SampleDeal.Create();
        var inner = new FixedFeed([feedDeal]);
        var netDeal = MarketNetworkDealBuilder.Build([Obs("a", 40), Obs("b", 44), Obs("c", 48)], 3).Single();
        var options = new MarketObservationOptions();

        var merged = await new NetworkAugmentedMarketProvider(inner, new FixedNetwork(netDeal), options).GetDealsAsync();
        var broken = await new NetworkAugmentedMarketProvider(inner, new FixedNetwork(null), options).GetDealsAsync();
        var off = await new NetworkAugmentedMarketProvider(inner, new FixedNetwork(netDeal), new MarketObservationOptions { Enabled = false }).GetDealsAsync();

        Assert.Equal(2, merged.Value.Deals.Count);
        Assert.Single(broken.Value.Deals);
        Assert.Single(off.Value.Deals);
    }

    private sealed class FixedFeed(IReadOnlyList<MarketDeal> deals) : IMarketIntelligenceProvider
    {
        public Task<Result<MarketFeedSnapshot>> GetDealsAsync(string? feedVersion = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<MarketFeedSnapshot>.Success(new MarketFeedSnapshot(deals, "feed-1")));
    }

    private sealed class FixedNetwork(MarketDeal? deal) : IMarketNetworkSource
    {
        public Task<MarketNetworkSnapshot> BuildAsync(CancellationToken cancellationToken = default) =>
            deal is null
                ? throw new InvalidOperationException("The store is down.")
                : Task.FromResult(new MarketNetworkSnapshot(3, 3, 1, [deal]));
    }
}
