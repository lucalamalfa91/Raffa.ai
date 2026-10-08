using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Contracts;
using Raffa.AiGateway.Fixtures;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel;

namespace Raffa.Market.Tests;

/// <summary>
/// F7-T06 acceptance: the query-time guard refuses an index built with a different model, or with
/// fixture (non-semantic) vectors against a real query and the reverse. Pure rules, so no database is
/// needed; the end-to-end refusal against a real index is in <c>PgVectorMarketKnowledgeRetrievalTests</c>
/// (Testcontainers).
/// </summary>
public sealed class MarketEmbeddingCompatibilityTests
{
    private static readonly MarketEmbeddingIdentity RealQuery = new("text-embedding-3-small", IsFixture: false);
    private static readonly MarketEmbeddingIdentity FixtureQuery = new("text-embedding-3-small", IsFixture: true);

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
    }

    // The identity detection rests on the prompt version the fixture gateway stamps. Pinned against
    // the real FixtureAiGateway so a rename there fails here instead of silently disabling the guard.
    [Fact]
    public async Task The_real_fixture_gateway_is_recognised_as_fixture_even_though_it_reports_the_real_model_id()
    {
        var gateway = new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock());

        var result = await gateway.EmbedAsync(new AiEmbeddingRequest("salesforce uplift cap"));

        Assert.True(result.IsSuccess);
        var identity = MarketEmbeddingIdentity.From(result.Value.Metadata);
        Assert.True(identity.IsFixture);
        Assert.Equal(new AiGatewayModelOptions().Embed.ModelId, identity.Model);
    }

    [Theory]
    [InlineData("foundry-embed-v2")]
    [InlineData("stub-v1")]
    [InlineData("v1")]
    public void A_real_gateway_prompt_version_is_not_fixture(string promptVersion)
    {
        var metadata = new AiCallMetadata("text-embedding-3-small", "2024-01", promptVersion, DateTimeOffset.UtcNow, "hash");

        Assert.False(MarketEmbeddingIdentity.From(metadata).IsFixture);
    }

    [Fact]
    public void Same_model_and_same_kind_is_comparable()
    {
        Assert.True(MarketEmbeddingCompatibility.IsComparable("text-embedding-3-small", false, RealQuery));
        Assert.True(MarketEmbeddingCompatibility.IsComparable("text-embedding-3-small", true, FixtureQuery));
    }

    [Fact]
    public void A_different_model_is_not_comparable()
    {
        Assert.False(MarketEmbeddingCompatibility.IsComparable("text-embedding-3-large", false, RealQuery));
    }

    [Fact]
    public void A_fixture_row_is_not_comparable_with_a_real_query_and_the_reverse()
    {
        Assert.False(MarketEmbeddingCompatibility.IsComparable("text-embedding-3-small", true, RealQuery));
        Assert.False(MarketEmbeddingCompatibility.IsComparable("text-embedding-3-small", false, FixtureQuery));
    }

    [Fact]
    public void A_row_written_before_the_flag_existed_is_judged_on_the_model_alone()
    {
        Assert.True(MarketEmbeddingCompatibility.IsComparable("text-embedding-3-small", null, RealQuery));
        Assert.True(MarketEmbeddingCompatibility.IsComparable("text-embedding-3-small", null, FixtureQuery));
        Assert.False(MarketEmbeddingCompatibility.IsComparable("other-model", null, RealQuery));
    }

    [Fact]
    public void An_empty_index_is_empty_not_incompatible()
    {
        Assert.Equal(MarketEmbeddingIndexState.Empty, MarketEmbeddingCompatibility.Evaluate([], RealQuery));
    }

    [Fact]
    public void An_index_built_entirely_with_fixture_vectors_is_incompatible_with_a_real_query()
    {
        var populations = new[] { new MarketEmbeddingPopulation("text-embedding-3-small", true, 25_727) };

        Assert.Equal(MarketEmbeddingIndexState.Incompatible, MarketEmbeddingCompatibility.Evaluate(populations, RealQuery));
        Assert.Equal(MarketEmbeddingIndexState.Compatible, MarketEmbeddingCompatibility.Evaluate(populations, FixtureQuery));
    }

    [Fact]
    public void An_index_built_with_another_model_is_incompatible()
    {
        var populations = new[] { new MarketEmbeddingPopulation("text-embedding-ada-002", false, 100) };

        Assert.Equal(MarketEmbeddingIndexState.Incompatible, MarketEmbeddingCompatibility.Evaluate(populations, RealQuery));
    }

    [Fact]
    public void An_index_part_way_through_a_re_embed_is_mixed_and_only_the_comparable_rows_count()
    {
        var populations = new[]
        {
            new MarketEmbeddingPopulation("text-embedding-3-small", false, 40),
            new MarketEmbeddingPopulation("text-embedding-3-small", true, 60),
        };

        Assert.Equal(MarketEmbeddingIndexState.Mixed, MarketEmbeddingCompatibility.Evaluate(populations, RealQuery));
        Assert.Equal(40, MarketEmbeddingCompatibility.ComparableCount(populations, RealQuery));
        Assert.Equal(60, MarketEmbeddingCompatibility.ComparableCount(populations, FixtureQuery));
    }

    [Fact]
    public void Unmarked_legacy_rows_with_the_right_model_keep_the_index_usable()
    {
        var populations = new[] { new MarketEmbeddingPopulation("text-embedding-3-small", null, 25_727) };

        Assert.Equal(MarketEmbeddingIndexState.Compatible, MarketEmbeddingCompatibility.Evaluate(populations, RealQuery));
        Assert.Equal(MarketEmbeddingIndexState.Compatible, MarketEmbeddingCompatibility.Evaluate(populations, FixtureQuery));
    }

    [Fact]
    public void The_refusal_message_names_both_sides_and_the_remedy()
    {
        var populations = new[] { new MarketEmbeddingPopulation("text-embedding-3-small", true, 25_727) };

        var message = MarketEmbeddingCompatibility.DescribeIncompatibility(populations, RealQuery);

        Assert.StartsWith(MarketEmbeddingCompatibility.IncompatibleIndexErrorPrefix, message, StringComparison.Ordinal);
        Assert.Contains("(real)", message, StringComparison.Ordinal);
        Assert.Contains("fixture, 25727 rows", message, StringComparison.Ordinal);
        Assert.Contains("ingest-market", message, StringComparison.Ordinal);
    }
}

/// <summary>
/// F7-T01: the arithmetic behind the vector query's <c>LIMIT</c> -- how many rows to fetch, and when
/// to widen. The point is that no window is ever the whole table by default, yet a filtered search can
/// still reach every comparable row.
/// </summary>
public sealed class MarketRetrievalWindowTests
{
    [Fact]
    public void An_unfiltered_search_fetches_a_small_multiple_of_topK_not_the_table()
    {
        Assert.Equal(10, MarketRetrievalWindow.Initial(topK: 5, filtered: false, comparableRows: 25_727));
    }

    [Fact]
    public void A_filtered_search_over_fetches_because_the_filter_runs_after_the_vector_query()
    {
        Assert.Equal(100, MarketRetrievalWindow.Initial(topK: 5, filtered: true, comparableRows: 25_727));
        Assert.Equal(200, MarketRetrievalWindow.Initial(topK: 20, filtered: true, comparableRows: 25_727));
    }

    [Fact]
    public void The_first_window_never_exceeds_the_index_ceiling_but_never_drops_below_topK()
    {
        Assert.Equal(MarketRetrievalWindow.MaxIndexWindow, MarketRetrievalWindow.Initial(topK: 500, filtered: true, comparableRows: 25_727));
        Assert.Equal(1_500, MarketRetrievalWindow.Initial(topK: 1_500, filtered: false, comparableRows: 25_727));
    }

    [Fact]
    public void The_window_is_capped_by_the_rows_that_exist()
    {
        Assert.Equal(3, MarketRetrievalWindow.Initial(topK: 5, filtered: false, comparableRows: 3));
        Assert.Equal(1, MarketRetrievalWindow.Initial(topK: 5, filtered: true, comparableRows: 0));
    }

    [Fact]
    public void Widening_grows_geometrically_and_ends_at_the_whole_comparable_index()
    {
        var window = MarketRetrievalWindow.Initial(topK: 5, filtered: true, comparableRows: 25_727);
        var steps = new List<int> { window };

        while (window < 25_727)
        {
            window = MarketRetrievalWindow.Next(window, 25_727);
            steps.Add(window);
        }

        Assert.Equal([100, 800, 6_400, 25_727], steps);
    }

    [Fact]
    public void Widening_always_makes_progress()
    {
        Assert.True(MarketRetrievalWindow.Next(window: 1, comparableRows: 10) > 1);
    }

    [Theory]
    [InlineData(10, 40)]
    [InlineData(40, 40)]
    [InlineData(200, 200)]
    [InlineData(5_000, 1_000)]
    public void The_hnsw_ef_search_covers_the_window_within_pgvectors_limits(int window, int expected)
    {
        Assert.Equal(expected, MarketRetrievalWindow.EfSearch(window));
    }
}
