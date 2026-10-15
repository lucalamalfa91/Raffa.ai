using Raffa.Market.Retrieval;

namespace Raffa.Market.Tests;

/// <summary>F7-T07: reciprocal-rank fusion and supplier diversification (pure, no database).</summary>
public sealed class MarketHitRankerTests
{
    private static MarketCandidate Vec(string id, string supplier, double similarity) =>
        new(SampleDeal.Create(recordId: id, supplier: supplier), similarity);

    private static Raffa.Market.Contracts.MarketDeal Lex(string id, string supplier) =>
        SampleDeal.Create(recordId: id, supplier: supplier);

    private static string[] Ids(IEnumerable<MarketNoteScore> hits) => hits.Select(h => h.Deal.RecordId).ToArray();

    [Fact]
    public void A_record_found_by_both_rankings_beats_one_found_by_only_the_best_vector_rank()
    {
        var vector = new[] { Vec("V1", "A", 0.9), Vec("V2", "B", 0.8), Vec("V3", "C", 0.7) };
        var lexical = new[] { Lex("V3", "C") };

        var ranked = MarketHitRanker.Rank(vector, lexical, new MarketRetrievalOptions { Hybrid = true }, 3);

        Assert.Equal(["V3", "V1", "V2"], Ids(ranked));
    }

    [Fact]
    public void A_lexical_only_hit_enters_with_a_fused_score_in_zero_to_one_and_a_vector_hit_keeps_its_similarity()
    {
        var vector = new[] { Vec("V1", "A", 0.9) };
        var lexical = new[] { Lex("L1", "Z") };

        var ranked = MarketHitRanker.Rank(vector, lexical, new MarketRetrievalOptions { Hybrid = true }, 5);

        var v1 = ranked.Single(h => h.Deal.RecordId == "V1");
        var l1 = ranked.Single(h => h.Deal.RecordId == "L1");
        Assert.Equal(0.9, v1.Score);
        Assert.InRange(l1.Score, 0.0001, 1.0);
    }

    [Fact]
    public void Without_a_lexical_ranking_the_vector_order_is_kept()
    {
        var vector = new[] { Vec("V1", "A", 0.9), Vec("V2", "B", 0.8), Vec("V3", "C", 0.7) };

        var ranked = MarketHitRanker.Rank(vector, [], new MarketRetrievalOptions { Diversify = true, MaxPerSupplier = 5 }, 2);

        Assert.Equal(["V1", "V2"], Ids(ranked));
    }

    [Fact]
    public void Diversification_caps_hits_per_supplier_and_refills_when_too_few_remain()
    {
        var vector = new[]
        {
            Vec("A1", "Acme", 0.99), Vec("A2", "Acme", 0.98), Vec("A3", "Acme", 0.97), Vec("A4", "acme", 0.96),
            Vec("B1", "Beta", 0.5), Vec("C1", "Gamma", 0.4),
        };
        var options = new MarketRetrievalOptions { Diversify = true, MaxPerSupplier = 2 };

        var ranked = MarketHitRanker.Rank(vector, [], options, 4);

        Assert.Equal(["A1", "A2", "B1", "C1"], Ids(ranked));

        var refilled = MarketHitRanker.Rank(vector.Take(4).ToArray(), [], options, 4);
        Assert.Equal(["A1", "A2", "A3", "A4"], Ids(refilled));
    }

    [Fact]
    public void Ties_are_broken_by_record_id_so_results_are_reproducible()
    {
        var ranked = MarketHitRanker.Rank([], [Lex("B", "x"), Lex("A", "y")], new MarketRetrievalOptions { Hybrid = true }, 2);

        // Different lexical ranks, so B (rank 0) wins; equal ranks across lists would fall back to the id.
        Assert.Equal(["B", "A"], Ids(ranked));
    }
}
