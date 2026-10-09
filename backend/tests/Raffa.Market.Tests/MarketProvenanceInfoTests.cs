using System.Text.Json;
using Raffa.Market.Contracts;
using Raffa.Market.Mock;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel.Market;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Market.Tests;

/// <summary>
/// F7-T11 / F7-D04 acceptance: every market figure carries a structured provenance
/// <c>{sourceClass,isDemo,n,asOf,unit,source}</c>, it is internal metadata that is never rendered to
/// the user (decision D5: no demo badge), and a label or a real source name can be switched on by
/// configuration without touching the data.
/// </summary>
public sealed class MarketProvenanceInfoTests
{
    private static readonly DateTimeOffset AsOf = new(2026, 6, 20, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_mock_record_is_classed_mock_and_demo_with_its_sample_date_unit_and_source()
    {
        var deal = SampleDeal.Create(sampleSize: 210, updatedAt: AsOf, unitMetric: "per user / year");

        var info = MarketProvenance.Info(deal);

        Assert.Equal(MarketSourceClasses.Mock, info.SourceClass);
        Assert.True(info.IsDemo);
        Assert.Equal(210, info.N);
        Assert.Equal(AsOf, info.AsOf);
        Assert.Equal("per user / year", info.Unit);
        Assert.Equal("mock", info.Source);
    }

    [Fact]
    public void A_record_without_a_unit_metric_has_no_unit_rather_than_a_guess()
    {
        Assert.Null(MarketProvenance.Info(SampleDeal.Create(unitMetric: null)).Unit);
        Assert.Null(MarketProvenance.Info(SampleDeal.Create(unitMetric: "  ")).Unit);
    }

    [Theory]
    [InlineData("mock", MarketSourceClasses.Mock, true)]
    [InlineData("Demo", MarketSourceClasses.Mock, true)]
    [InlineData("list-price", MarketSourceClasses.ListPrice, false)]
    [InlineData("Consip", MarketSourceClasses.PublicProcurement, false)]
    [InlineData("tenant pool", MarketSourceClasses.TenantPool, false)]
    [InlineData("vendr", MarketSourceClasses.Provider, false)]
    public void The_source_text_maps_onto_a_source_class_and_only_mock_is_demo(string source, string expectedClass, bool expectedDemo)
    {
        var info = MarketProvenance.Info(SampleDeal.Create(source: source));

        Assert.Equal(expectedClass, info.SourceClass);
        Assert.Equal(expectedDemo, info.IsDemo);
    }

    [Fact]
    public void A_bundle_takes_the_smallest_sample_the_oldest_date_and_is_demo_if_any_component_is()
    {
        var old = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var info = MarketProvenance.Combine(
        [
            SampleDeal.Create(recordId: "A", sampleSize: 48, updatedAt: AsOf, unitMetric: "per user / month"),
            SampleDeal.Create(recordId: "B", sampleSize: 6, updatedAt: old, unitMetric: "per user / month"),
        ]);

        Assert.Equal(6, info.N);
        Assert.Equal(old, info.AsOf);
        Assert.True(info.IsDemo);
        Assert.Equal(MarketSourceClasses.Mock, info.SourceClass);
        Assert.Equal("per user / month", info.Unit);
    }

    [Fact]
    public void A_bundle_of_different_classes_is_mixed()
    {
        var info = MarketProvenance.Combine(
        [
            SampleDeal.Create(recordId: "A", source: "mock"),
            SampleDeal.Create(recordId: "B", source: "list_price"),
        ]);

        Assert.Equal(MarketSourceClasses.Mixed, info.SourceClass);
        Assert.True(info.IsDemo);
    }

    [Fact]
    public void The_dto_serialises_with_exactly_the_agreed_field_names()
    {
        var json = JsonSerializer.Serialize(
            MarketProvenance.Info(SampleDeal.Create(updatedAt: AsOf, unitMetric: "per user / year")),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using var document = JsonDocument.Parse(json);
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).ToList();

        Assert.Equal(["sourceClass", "isDemo", "n", "asOf", "unit", "source"], names);
    }

    // ---- never rendered ------------------------------------------------------------------

    [Fact]
    public void The_structured_provenance_adds_nothing_to_the_text_the_user_reads()
    {
        var deal = SampleDeal.Create(updatedAt: AsOf, unitMetric: "per user / year");

        var note = MarketNoteComposer.Compose(deal);

        // The visible text is exactly what it was before the DTO existed...
        Assert.Equal("representative market data · mock feed · updated 2026-06-20", note.Provenance);
        // ...and nothing in it announces demo data or leaks the DTO's fields.
        foreach (var visible in new[] { note.Title, note.Snippet, note.Provenance })
        {
            Assert.DoesNotContain("demo", visible, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("dimostrativ", visible, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("sourceClass", visible, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("per user / year", visible, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void The_note_carries_the_structured_provenance_next_to_the_unchanged_label()
    {
        var note = MarketNoteComposer.Compose(SampleDeal.Create(sampleSize: 64, updatedAt: AsOf));

        Assert.NotNull(note.ProvenanceInfo);
        Assert.Equal(64, note.ProvenanceInfo.N);
        Assert.True(note.ProvenanceInfo.IsDemo);
    }

    [Fact]
    public async Task Notes_from_the_retrieval_carry_the_structured_provenance()
    {
        var retrieval = new InMemoryMarketKnowledgeRetrieval(new MockMarketIntelligenceProvider());

        var result = await retrieval.SearchAsync("uplift cap Salesforce", topK: 5);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value);
        Assert.All(result.Value, note => Assert.NotNull(note.ProvenanceInfo));
    }

    [Fact]
    public void Nothing_is_displayed_by_default()
    {
        var info = MarketProvenance.Info(SampleDeal.Create());

        Assert.Null(MarketProvenance.Display(info, null));
        Assert.Null(MarketProvenance.Display(info, new MarketProvenanceOptions()));
        Assert.Null(MarketProvenance.Display(info, new MarketProvenanceOptions { RealSourceName = "Consip", DemoLabel = "Illustrative" }));
    }

    // ---- activatable by configuration ------------------------------------------------------

    [Fact]
    public void A_label_shows_the_internal_source_sample_and_date_once_switched_on()
    {
        var info = MarketProvenance.Info(SampleDeal.Create(sampleSize: 64, updatedAt: AsOf));

        var line = MarketProvenance.Display(info, new MarketProvenanceOptions { ShowLabel = true });

        Assert.Equal("mock · n=64 · 2026-06-20", line);
    }

    [Fact]
    public void A_real_source_name_replaces_the_internal_one_without_touching_the_data()
    {
        var info = MarketProvenance.Info(SampleDeal.Create(sampleSize: 64, updatedAt: AsOf));

        var line = MarketProvenance.Display(info, new MarketProvenanceOptions { ShowLabel = true, RealSourceName = "Consip convenzioni" });

        Assert.Equal("Consip convenzioni · n=64 · 2026-06-20", line);
    }

    [Fact]
    public void A_demo_label_is_shown_only_for_demo_figures_and_only_when_configured()
    {
        var options = new MarketProvenanceOptions { ShowLabel = true, DemoLabel = "Illustrative data" };
        var demo = MarketProvenance.Info(SampleDeal.Create(sampleSize: 10, updatedAt: AsOf));
        var real = MarketProvenance.Info(SampleDeal.Create(source: "list_price", sampleSize: 10, updatedAt: AsOf));

        Assert.Equal("Illustrative data · n=10 · 2026-06-20", MarketProvenance.Display(demo, options));
        Assert.Equal("list_price · n=10 · 2026-06-20", MarketProvenance.Display(real, options));
    }

    [Fact]
    public void The_switches_bind_from_the_Market_Provenance_configuration_section()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Market:Provenance:ShowLabel"] = "true",
                ["Market:Provenance:RealSourceName"] = "ANAC prezzi di riferimento",
            })
            .Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddMarketModule();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<MarketProvenanceOptions>();

        Assert.True(options.ShowLabel);
        Assert.Equal("ANAC prezzi di riferimento", options.RealSourceName);
    }

    [Fact]
    public void Without_configuration_every_switch_is_off()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddMarketModule();
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<MarketProvenanceOptions>();

        Assert.False(options.ShowLabel);
        Assert.Null(options.RealSourceName);
        Assert.Null(options.DemoLabel);
    }

    // ---- every market number ---------------------------------------------------------------

    [Fact]
    public void A_matched_price_band_carries_the_structured_provenance()
    {
        var deals = new[]
        {
            SampleDeal.Create(recordId: "ENT", product: "Sales Cloud Enterprise", geography: "UK", currency: "GBP",
                sampleSize: 210, updatedAt: AsOf, unitMetric: "per user / year"),
        };

        var match = Assert.Single(MarketPriceMatcher.Match(
            new MarketPriceContext("Salesforce, Inc.", "GBP", 12),
            [new MarketPriceLine("Sales Cloud Enterprise", null)],
            deals));

        Assert.NotNull(match);
        Assert.NotNull(match.ProvenanceInfo);
        Assert.Equal(210, match.ProvenanceInfo.N);
        Assert.Equal(AsOf, match.ProvenanceInfo.AsOf);
        Assert.Equal("per user / year", match.ProvenanceInfo.Unit);
        Assert.True(match.ProvenanceInfo.IsDemo);
        // The label string consumers already read is untouched.
        Assert.Equal("representative market data · mock feed · updated 2026-06-20", match.Provenance);
    }

    [Fact]
    public void A_bundle_band_carries_the_combined_provenance()
    {
        var deals = new[]
        {
            SampleDeal.Create(recordId: "JIRA", supplier: "Atlassian", product: "Jira Software Premium", geography: "UK", currency: "GBP", sampleSize: 6),
            SampleDeal.Create(recordId: "CONF", supplier: "Atlassian", product: "Confluence Premium", geography: "UK", currency: "GBP", sampleSize: 48),
        };

        var match = MarketPriceMatcher.Match(
            new MarketPriceContext("Atlassian", "GBP", 12),
            [new MarketPriceLine("Jira Software Premium + Confluence Premium", "named users / licenses")],
            deals)[0];

        Assert.NotNull(match);
        Assert.Equal(MarketMatchKind.Bundle, match.Kind);
        Assert.NotNull(match.ProvenanceInfo);
        Assert.Equal(6, match.ProvenanceInfo.N);
    }

    // ---- fields the corpus carries are kept ---------------------------------------------------

    [Fact]
    public void Industry_and_unit_metric_survive_a_json_round_trip()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var deal = SampleDeal.Create(industry: "Financial Services", unitMetric: "per user / year");

        var back = JsonSerializer.Deserialize<MarketDeal>(JsonSerializer.Serialize(deal, options), options);

        Assert.NotNull(back);
        Assert.Equal("Financial Services", back.Industry);
        Assert.Equal("per user / year", back.UnitMetric);
    }

    [Fact]
    public void A_record_json_without_those_fields_still_loads()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(SampleDeal.Create(), options);

        var back = JsonSerializer.Deserialize<MarketDeal>(json, options);

        Assert.NotNull(back);
        Assert.Null(back.Industry);
        Assert.Null(back.UnitMetric);
    }

    [Fact]
    public async Task The_checked_in_corpus_no_longer_discards_industry()
    {
        var feed = await new MockMarketIntelligenceProvider().GetDealsAsync();

        Assert.True(feed.IsSuccess);
        Assert.True(
            feed.Value.Deals.Count(d => d.Industry is not null) > 25_000,
            "the corpus carries `industry` on its generated records; MarketDeal must keep it");
    }
}
