using Raffa.Api.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;

namespace Raffa.Api.Tests;

/// <summary>The daily ECB reference rates: the feed parser, the "is a download due" rule and the cross into a reporting currency.</summary>
public sealed class FxReferenceRateTests
{
    private const string Feed = """
        <?xml version="1.0" encoding="UTF-8"?>
        <gesmes:Envelope xmlns:gesmes="http://www.gesmes.org/xml/2002-08-01" xmlns="http://www.ecb.int/vocabulary/2002-08-01/eurofxref">
          <gesmes:subject>Reference rates</gesmes:subject>
          <Cube>
            <Cube time='2026-11-12'>
              <Cube currency='USD' rate='1.1081'/>
              <Cube currency='CHF' rate='0.9232'/>
              <Cube currency='GBP' rate='0.83595'/>
              <Cube currency='XXX' rate='nope'/>
            </Cube>
          </Cube>
        </gesmes:Envelope>
        """;

    [Fact]
    public void Parse_reads_the_date_and_every_valid_rate()
    {
        var rates = EcbReferenceRateClient.Parse(Feed);

        Assert.Equal(new DateOnly(2026, 11, 12), rates.RateDate);
        Assert.Equal(3, rates.PerEur.Count);
        Assert.Equal(1.1081m, rates.PerEur["USD"]);
        Assert.Equal(0.9232m, rates.PerEur["CHF"]);
    }

    [Fact]
    public void Parse_rejects_a_feed_without_rates()
    {
        Assert.Throws<FormatException>(() => EcbReferenceRateClient.Parse("<Envelope><Cube/></Envelope>"));
    }

    [Fact]
    public void RatesInto_crosses_through_the_euro()
    {
        var market = new FxReferenceRates(
            new Dictionary<string, decimal> { ["USD"] = 1.1081m, ["CHF"] = 0.9232m }, new DateOnly(2026, 11, 12), DateTimeOffset.UtcNow);

        var intoChf = market.RatesInto("CHF");

        Assert.Equal(0.9232m, intoChf["EUR"]);
        Assert.Equal(Math.Round(0.9232m / 1.1081m, 8), intoChf["USD"]);
        Assert.False(intoChf.ContainsKey("CHF"));

        var intoEur = market.RatesInto("eur");
        Assert.Equal(Math.Round(1m / 0.9232m, 8), intoEur["CHF"]);
    }

    [Fact]
    public void RatesInto_is_empty_when_the_reporting_currency_has_no_reference_rate()
    {
        var market = new FxReferenceRates(new Dictionary<string, decimal> { ["USD"] = 1.1m }, new DateOnly(2026, 11, 12), DateTimeOffset.UtcNow);

        Assert.Empty(market.RatesInto("AED"));
        Assert.Empty(FxReferenceRates.Empty.RatesInto("CHF"));
    }

    [Theory]
    // Never fetched: due at once.
    [InlineData(null, "2026-11-13T03:00:00Z", true)]
    // Fetched yesterday morning, now 07:30 Zurich (06:30Z): due.
    [InlineData("2026-11-12T05:10:00Z", "2026-11-13T06:30:00Z", true)]
    // Fetched yesterday morning, now 05:30 Zurich (04:30Z): not yet.
    [InlineData("2026-11-12T05:10:00Z", "2026-11-13T04:30:00Z", false)]
    // Already fetched this morning after 06:00 Zurich (05:00Z): not due.
    [InlineData("2026-11-13T05:05:00Z", "2026-11-13T15:00:00Z", false)]
    // Last fetch two days ago, now before the hour: yesterday's slot was missed, due.
    [InlineData("2026-11-11T05:05:00Z", "2026-11-13T04:00:00Z", true)]
    public void IsDue_follows_the_morning_slot(string? last, string now, bool expected)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Zurich");
        DateTimeOffset? lastFetched = last is null ? null : DateTimeOffset.Parse(last, System.Globalization.CultureInfo.InvariantCulture);

        var due = FxReferenceRateRefreshService.IsDue(lastFetched, DateTimeOffset.Parse(now, System.Globalization.CultureInfo.InvariantCulture), zone, 6);

        Assert.Equal(expected, due);
    }
}
