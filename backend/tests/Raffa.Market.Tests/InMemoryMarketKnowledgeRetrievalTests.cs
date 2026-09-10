using Raffa.Market.Mock;
using Raffa.Market.Retrieval;

namespace Raffa.Market.Tests;

/// <summary>
/// Proves <see cref="InMemoryMarketKnowledgeRetrieval"/> against the real, checked-in mock feed
/// (<see cref="MockMarketIntelligenceProvider"/>) — including the task's own named scenario:
/// "in-memory search returns the Salesforce note for 'uplift cap Salesforce'".
/// </summary>
public class InMemoryMarketKnowledgeRetrievalTests
{
    private readonly InMemoryMarketKnowledgeRetrieval _retrieval = new(new MockMarketIntelligenceProvider());

    [Fact]
    public async Task SearchAsync_returns_the_salesforce_ch_note_for_uplift_cap_salesforce()
    {
        var result = await _retrieval.SearchAsync("uplift cap Salesforce", topK: 5);

        Assert.True(result.IsSuccess);
        Assert.Contains(result.Value, note => note.RecordId == "MKT-SFDC-CH-01");
        Assert.Contains(result.Value, note => note.Title.Contains("Salesforce", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_ranks_full_token_overlap_above_partial_overlap()
    {
        var result = await _retrieval.SearchAsync("uplift cap Salesforce", topK: 20);

        Assert.True(result.IsSuccess);
        var chNote = Assert.Single(result.Value, note => note.RecordId == "MKT-SFDC-CH-01");
        Assert.True(result.Value.All(note => note.Score <= chNote.Score),
            "the Salesforce CH note (an exact three-token match) should rank at or above every other hit.");
    }

    [Fact]
    public async Task SearchAsync_returns_an_empty_success_when_nothing_overlaps()
    {
        var result = await _retrieval.SearchAsync("xyzzy-no-such-token-anywhere", topK: 5);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task SearchAsync_respects_the_category_filter()
    {
        var result = await _retrieval.SearchAsync(
            "Allianz", topK: 10, new MarketKnowledgeSearchFilters(Category: "Insurance"));

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value);
        Assert.All(result.Value, note => Assert.Equal("Insurance", note.Category));
    }

    [Fact]
    public async Task SearchAsync_category_filter_excludes_non_matching_records()
    {
        // "Salesforce" only ever appears in Enterprise Software rows — filtering to Insurance
        // must exclude every Salesforce hit even though the token would otherwise overlap.
        var result = await _retrieval.SearchAsync(
            "Salesforce", topK: 10, new MarketKnowledgeSearchFilters(Category: "Insurance"));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SearchAsync_fails_for_a_blank_query(string blankQuery)
    {
        var result = await _retrieval.SearchAsync(blankQuery, topK: 5);

        Assert.True(result.IsFailure);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SearchAsync_fails_for_a_non_positive_topK(int invalidTopK)
    {
        var result = await _retrieval.SearchAsync("Salesforce", invalidTopK);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SearchAsync_never_returns_more_than_topK_hits()
    {
        var result = await _retrieval.SearchAsync("enterprise", topK: 2);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Count <= 2);
    }
}
