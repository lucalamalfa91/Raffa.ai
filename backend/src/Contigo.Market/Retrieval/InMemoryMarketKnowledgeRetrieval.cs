using Contigo.Market.Contracts;
using Contigo.SharedKernel;

namespace Contigo.Market.Retrieval;

/// <summary>
/// This task's (T01) default <see cref="IMarketKnowledgeRetrieval"/>: plain token overlap over
/// <see cref="MarketNoteComposer"/>'s composed notes, held entirely in memory — no index, no
/// database, no embedding call. T02 replaces this registration with a pgvector-backed
/// implementation over the shared `market_embedding` index (R-MKT-03); callers depend on
/// <see cref="IMarketKnowledgeRetrieval"/> only; see that interface's own doc comment.
///
/// Scoring is deliberately simple and explainable rather than semantically clever: the fraction
/// of the (lower-cased, punctuation-stripped) query tokens that also appear in a note's title +
/// snippet. A note with zero overlapping tokens is dropped rather than returned with a
/// meaningless zero score.
/// </summary>
public sealed class InMemoryMarketKnowledgeRetrieval(IMarketIntelligenceProvider provider)
    : IMarketKnowledgeRetrieval
{
    private static readonly char[] TokenTrimCharacters = ['.', ',', ';', ':', '(', ')', '%', '·', '"', '\'', '!', '?'];

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<MarketNote>>> SearchAsync(
        string query,
        int topK,
        MarketKnowledgeSearchFilters? filters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Result<IReadOnlyList<MarketNote>>.Failure("Query text is required.");
        }

        if (topK <= 0)
        {
            return Result<IReadOnlyList<MarketNote>>.Failure("topK must be a positive number.");
        }

        var feedResult = await provider.GetDealsAsync(feedVersion: null, cancellationToken).ConfigureAwait(false);
        if (feedResult.IsFailure)
        {
            return Result<IReadOnlyList<MarketNote>>.Failure(feedResult.Error);
        }

        IEnumerable<MarketDeal> candidates = feedResult.Value.Deals;

        if (filters?.Category is { } category)
        {
            candidates = candidates.Where(d => string.Equals(d.Category, category, StringComparison.OrdinalIgnoreCase));
        }

        if (filters?.Geography is { } geography)
        {
            candidates = candidates.Where(d => string.Equals(d.Geography, geography, StringComparison.OrdinalIgnoreCase));
        }

        var queryTokens = Tokenize(query);

        var hits = candidates
            .Select(MarketNoteComposer.Compose)
            .Select(note => note with { Score = ComputeOverlapScore(queryTokens, Tokenize(note.Title + " " + note.Snippet)) })
            .Where(note => note.Score > 0)
            .OrderByDescending(note => note.Score)
            .ThenBy(note => note.RecordId, StringComparer.Ordinal)
            .Take(topK)
            .ToList();

        return Result<IReadOnlyList<MarketNote>>.Success(hits);
    }

    private static HashSet<string> Tokenize(string text) =>
        text
            .ToLowerInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim(TokenTrimCharacters))
            .Where(token => token.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

    private static double ComputeOverlapScore(HashSet<string> queryTokens, HashSet<string> noteTokens)
    {
        if (queryTokens.Count == 0)
        {
            return 0;
        }

        var overlap = queryTokens.Count(noteTokens.Contains);
        return overlap / (double)queryTokens.Count;
    }
}
