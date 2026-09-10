using Raffa.Market.Contracts;

namespace Raffa.Market.Tests;

/// <summary>Locks in the task's own verbatim provenance label format (R-MKT-04) so an accidental
/// wording change is caught here rather than silently drifting from what Ask/Renewals/Quote check
/// show a user.</summary>
public class MarketProvenanceTests
{
    [Fact]
    public void Label_formats_representative_market_data_mock_feed_and_the_update_date()
    {
        var deal = SampleDeal.Create(updatedAt: new DateTimeOffset(2026, 6, 20, 0, 0, 0, TimeSpan.Zero));

        var label = MarketProvenance.Label(deal);

        Assert.Equal("representative market data · mock feed · updated 2026-06-20", label);
    }

    [Fact]
    public void Label_uses_the_deals_own_updated_at_not_the_current_date()
    {
        var oldDeal = SampleDeal.Create(updatedAt: new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero));

        var label = MarketProvenance.Label(oldDeal);

        Assert.Contains("updated 2020-01-01", label, StringComparison.Ordinal);
    }

    [Fact]
    public void Label_throws_for_a_null_deal()
    {
        Assert.Throws<ArgumentNullException>(() => MarketProvenance.Label(null!));
    }
}
