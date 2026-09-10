using Raffa.Market.Contracts;
using Raffa.Market.Retrieval;

namespace Raffa.Market.Tests;

/// <summary>Proves R-MKT-03 Projection 2's "one narrative per record" composer against the task's
/// own worked example shape (Salesforce, CH, 500-2000 employees, uplift cap).</summary>
public class MarketNoteComposerTests
{
    [Fact]
    public void Compose_narrative_contains_the_company_size_band_and_the_uplift_cap()
    {
        var deal = SampleDeal.Create();

        var note = MarketNoteComposer.Compose(deal);

        Assert.Contains(deal.CompanySizeBand, note.Snippet, StringComparison.Ordinal);
        Assert.Contains("4% uplift cap", note.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_narrative_contains_the_notice_period_and_the_discount()
    {
        var deal = SampleDeal.Create();

        var note = MarketNoteComposer.Compose(deal);

        Assert.Contains("90-day notice", note.Snippet, StringComparison.Ordinal);
        Assert.Contains("8% discount", note.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_narrative_lists_negotiated_clauses_when_present()
    {
        var deal = SampleDeal.Create(negotiatedClauses:
        [
            new NegotiatedClause("UpliftCap", "4% annual cap"),
            new NegotiatedClause("TerminationForConvenience", "90 days notice, no penalty"),
        ]);

        var note = MarketNoteComposer.Compose(deal);

        Assert.Contains("UpliftCap", note.Snippet, StringComparison.Ordinal);
        Assert.Contains("TerminationForConvenience", note.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_never_throws_when_every_optional_field_is_absent()
    {
        var deal = SampleDeal.Create(
            discountAchievedPct: null,
            upliftCapPct: null,
            noticeDays: null,
            paymentTerms: null,
            negotiatedClauses: [],
            sku: null,
            licenseRestrictions: null);

        var note = MarketNoteComposer.Compose(deal);

        Assert.False(string.IsNullOrWhiteSpace(note.Snippet));
        Assert.DoesNotContain("uplift cap", note.Snippet, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_sets_provenance_to_MarketProvenance_Label()
    {
        var deal = SampleDeal.Create();

        var note = MarketNoteComposer.Compose(deal);

        Assert.Equal(MarketProvenance.Label(deal), note.Provenance);
    }

    [Fact]
    public void Compose_score_is_zero_before_any_search_ranks_it()
    {
        var note = MarketNoteComposer.Compose(SampleDeal.Create());

        Assert.Equal(0d, note.Score);
    }

    [Fact]
    public void Compose_title_includes_the_sku_when_present()
    {
        var deal = SampleDeal.Create(supplier: "AWS", product: "EC2 Compute", sku: "m5.large");

        var note = MarketNoteComposer.Compose(deal);

        Assert.Contains("m5.large", note.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Compose_echoes_record_id_category_geography_and_updated_at()
    {
        var deal = SampleDeal.Create(recordId: "MKT-TEST-9999", category: "Insurance", geography: "EU");

        var note = MarketNoteComposer.Compose(deal);

        Assert.Equal("MKT-TEST-9999", note.RecordId);
        Assert.Equal("Insurance", note.Category);
        Assert.Equal("EU", note.Geography);
        Assert.Equal(deal.UpdatedAt, note.UpdatedAt);
    }
}
