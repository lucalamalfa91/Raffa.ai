using Raffa.Market.Contracts;

namespace Raffa.Market.Retrieval;

/// <summary>Options of the market search (<c>Market:Retrieval</c>). Every default is the retrieval as it was.</summary>
public sealed class MarketRetrievalOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "Market:Retrieval";

    /// <summary>F7-T07: also run a lexical (facet) search -- records whose supplier or product is named in
    /// the query -- and fuse it with the vector ranking (reciprocal rank fusion). Off by default.</summary>
    public bool Hybrid { get; set; }

    /// <summary>F7-T07: cap how many hits of the same supplier the top-k may hold, so one vendor's many
    /// near-identical rows cannot crowd out the others. Off by default.</summary>
    public bool Diversify { get; set; }

    /// <summary>Most hits per supplier when <see cref="Diversify"/> is on; shortfalls are filled from the
    /// remainder, so the result still has <c>topK</c> hits when enough exist.</summary>
    public int MaxPerSupplier { get; set; } = 2;

    /// <summary>Reciprocal-rank-fusion constant (60 is the value from the original RRF paper).</summary>
    public int RrfK { get; set; } = 60;

    /// <summary>Weight of the lexical ranking in the fusion. Above 1 on purpose: a record whose supplier or
    /// product the query names outranks an equally ranked vector hit that merely sounds similar.</summary>
    public double LexicalWeight { get; set; } = 1.5;

    /// <summary>How many candidates each ranking contributes, as a multiple of <c>topK</c> (at least 20 rows).</summary>
    public int PoolFactor { get; set; } = 5;
}

/// <summary>A vector-search hit before it becomes a <see cref="MarketNote"/>.</summary>
/// <param name="Deal">The record.</param>
/// <param name="Similarity">Vector similarity in [0, 1] (higher is closer); 0 for a lexical-only hit.</param>
public sealed record MarketCandidate(MarketDeal Deal, double Similarity);

/// <summary>
/// F7-T07: fuses the vector ranking with the lexical one and diversifies the result. Pure -- no
/// database, no clock -- so the behaviour is testable on its own. The vector similarity stays the
/// note's score when it has one; a hit that only the lexical ranking found carries the fused score.
/// </summary>
public static class MarketHitRanker
{
    /// <summary>The first <paramref name="topK"/> hits after fusion and diversification, best first.</summary>
    public static IReadOnlyList<MarketNoteScore> Rank(
        IReadOnlyList<MarketCandidate> vector,
        IReadOnlyList<MarketDeal> lexical,
        MarketRetrievalOptions options,
        int topK)
    {
        ArgumentNullException.ThrowIfNull(vector);
        ArgumentNullException.ThrowIfNull(lexical);
        ArgumentNullException.ThrowIfNull(options);

        var k = Math.Max(1, options.RrfK);
        var fused = new Dictionary<string, (MarketDeal Deal, double Similarity, double Rrf)>(StringComparer.Ordinal);

        for (var rank = 0; rank < vector.Count; rank++)
        {
            var candidate = vector[rank];
            var rrf = 1d / (k + rank + 1);
            fused[candidate.Deal.RecordId] = fused.TryGetValue(candidate.Deal.RecordId, out var seen)
                ? (seen.Deal, Math.Max(seen.Similarity, candidate.Similarity), seen.Rrf + rrf)
                : (candidate.Deal, candidate.Similarity, rrf);
        }

        for (var rank = 0; rank < lexical.Count; rank++)
        {
            var deal = lexical[rank];
            var rrf = Math.Max(0d, options.LexicalWeight) / (k + rank + 1);
            fused[deal.RecordId] = fused.TryGetValue(deal.RecordId, out var seen)
                ? (seen.Deal, seen.Similarity, seen.Rrf + rrf)
                : (deal, 0d, rrf);
        }

        // Highest fused score first; ties keep a stable order (record id) so results are reproducible.
        var ordered = fused.Values
            .OrderByDescending(x => x.Rrf)
            .ThenBy(x => x.Deal.RecordId, StringComparer.Ordinal)
            .ToList();

        // Scale the fusion to (0, 1] against the best score the two rankings could give one record.
        var ceiling = (1d + Math.Max(0d, options.LexicalWeight)) / (k + 1);
        var scored = ordered
            .Select(x => new MarketNoteScore(x.Deal, x.Similarity > 0 ? x.Similarity : Math.Min(1d, x.Rrf / ceiling)))
            .ToList();

        return options.Diversify ? Diversify(scored, options.MaxPerSupplier, topK) : scored.Take(topK).ToList();
    }

    /// <summary>Takes at most <paramref name="maxPerSupplier"/> hits per supplier in rank order, then fills the shortfall from the skipped ones.</summary>
    public static IReadOnlyList<MarketNoteScore> Diversify(
        IReadOnlyList<MarketNoteScore> ranked, int maxPerSupplier, int topK)
    {
        var cap = Math.Max(1, maxPerSupplier);
        var perSupplier = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var picked = new List<MarketNoteScore>(topK);
        var skipped = new List<MarketNoteScore>();

        foreach (var hit in ranked)
        {
            if (picked.Count == topK)
            {
                break;
            }

            var count = perSupplier.GetValueOrDefault(hit.Deal.Supplier);
            if (count >= cap)
            {
                skipped.Add(hit);
                continue;
            }

            perSupplier[hit.Deal.Supplier] = count + 1;
            picked.Add(hit);
        }

        foreach (var hit in skipped)
        {
            if (picked.Count == topK)
            {
                break;
            }

            picked.Add(hit);
        }

        return picked;
    }
}

/// <summary>A ranked record with the score its note will carry.</summary>
public sealed record MarketNoteScore(MarketDeal Deal, double Score);
