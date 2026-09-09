using Contigo.Market.Mock;

namespace Contigo.Market.Tests;

/// <summary>
/// Proves R-MKT-01/R-MKT-02 against the actual checked-in
/// <c>backend/fixtures/market-intelligence.mock.json</c>, embedded into this same test assembly's
/// referenced <c>Contigo.Market.dll</c> — not a hand-typed stand-in (see
/// <see cref="MockMarketIntelligenceProvider"/>'s own doc comment for why embedding, not a runtime
/// file path).
/// </summary>
public class MockMarketIntelligenceProviderTests
{
    private readonly MockMarketIntelligenceProvider _provider = new();

    [Fact]
    public async Task Loads_at_least_60_records()
    {
        var result = await _provider.GetDealsAsync();

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Deals.Count >= 60,
            $"R-MKT-02 requires >= 60 records; found {result.Value.Deals.Count}.");
    }

    [Fact]
    public async Task Every_record_is_labelled_mock_and_representative()
    {
        var result = await _provider.GetDealsAsync();

        Assert.All(result.Value.Deals, deal =>
        {
            Assert.Equal("mock", deal.Source);
            Assert.True(deal.Representative);
        });
    }

    [Fact]
    public async Task Every_record_id_is_unique()
    {
        var result = await _provider.GetDealsAsync();

        var distinctIds = result.Value.Deals.Select(d => d.RecordId).Distinct(StringComparer.Ordinal).Count();
        Assert.Equal(result.Value.Deals.Count, distinctIds);
    }

    [Fact]
    public async Task Dataset_includes_an_insurance_row_usable_as_Allianz()
    {
        var result = await _provider.GetDealsAsync();

        Assert.Contains(result.Value.Deals, deal =>
            deal.Supplier == "Allianz" && deal.Category == "Insurance" && deal.SampleSize >= 5);
    }

    [Fact]
    public async Task Dataset_includes_several_deliberately_thin_rows()
    {
        var result = await _provider.GetDealsAsync();

        var thinRows = result.Value.Deals.Count(deal => deal.SampleSize < 5);
        Assert.True(thinRows >= 5, $"expected several thin rows (sampleSize < 5); found {thinRows}.");
    }

    [Fact]
    public async Task Dataset_spans_the_required_geographies_and_currencies()
    {
        var result = await _provider.GetDealsAsync();

        var geographies = result.Value.Deals.Select(d => d.Geography).ToHashSet(StringComparer.Ordinal);
        var currencies = result.Value.Deals.Select(d => d.Currency).ToHashSet(StringComparer.Ordinal);

        foreach (var expectedGeography in new[] { "EU", "CH", "US" })
        {
            Assert.Contains(expectedGeography, geographies);
        }

        foreach (var expectedCurrency in new[] { "CHF", "EUR", "USD" })
        {
            Assert.Contains(expectedCurrency, currencies);
        }
    }

    [Fact]
    public async Task GetDealsAsync_with_null_feed_version_returns_the_current_feed()
    {
        var result = await _provider.GetDealsAsync(feedVersion: null);

        Assert.True(result.IsSuccess);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.FeedVersion));
    }

    [Fact]
    public async Task GetDealsAsync_with_the_current_feed_version_succeeds_idempotently()
    {
        var first = await _provider.GetDealsAsync();
        var second = await _provider.GetDealsAsync(first.Value.FeedVersion);

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.FeedVersion, second.Value.FeedVersion);
        Assert.Equal(first.Value.Deals.Count, second.Value.Deals.Count);
    }

    [Fact]
    public async Task GetDealsAsync_with_an_unknown_feed_version_fails_honestly()
    {
        var result = await _provider.GetDealsAsync(feedVersion: "not-a-real-version");

        Assert.True(result.IsFailure);
        Assert.Contains("not-a-real-version", result.Error, StringComparison.Ordinal);
    }
}
