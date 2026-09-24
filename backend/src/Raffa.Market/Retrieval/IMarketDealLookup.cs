using Raffa.Market.Contracts;

namespace Raffa.Market.Retrieval;

/// <summary>
/// Structured lookup of the shared market corpus by supplier — the deals themselves
/// (<see cref="MarketDeal"/>: bands, discount achieved, uplift cap, notice days, payment terms,
/// negotiated clauses, term), not the narrative <see cref="MarketNote"/> the vector search returns.
/// The savings calculators (<c>Raffa.Insights</c>) need these fields as numbers; the vector
/// search only carries them as prose. Same two backings as <see cref="IMarketKnowledgeRetrieval"/>:
/// Postgres (<see cref="MarketRecordQueryService"/>) when the Market connection string is set,
/// the provider's own feed (<see cref="ProviderMarketDealLookup"/>) otherwise.
/// </summary>
public interface IMarketDealLookup
{
    /// <summary>Every deal of the corpus whose supplier matches <paramref name="supplierName"/>
    /// (case-insensitive, punctuation-insensitive, legal suffix-insensitive), newest first. Empty
    /// when the supplier is unknown to the corpus — never an error.</summary>
    Task<IReadOnlyList<MarketDeal>> GetBySupplierAsync(string supplierName, CancellationToken cancellationToken = default);

    /// <summary>Every deal of the corpus in <paramref name="category"/> (case-insensitive), newest
    /// first — the wider market a similar product is looked for in when the supplier's own
    /// catalogue has nothing close. Empty for an unknown category, and by default for a backing
    /// that cannot list by category.</summary>
    Task<IReadOnlyList<MarketDeal>> GetByCategoryAsync(string category, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MarketDeal>>([]);

    /// <summary>Every deal of the corpus, newest first — what an estimate for a line nothing else
    /// priced may be converted from. Empty by default for a backing that cannot list it.</summary>
    Task<IReadOnlyList<MarketDeal>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<MarketDeal>>([]);

    /// <summary>A fingerprint of the corpus (feed version and record count) that changes when it is
    /// re-ingested; <see langword="null"/> when the backing cannot say.</summary>
    Task<string?> GetCorpusVersionAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);
}

/// <summary>Supplier-name matching shared by both <see cref="IMarketDealLookup"/> backings.</summary>
public static class MarketSupplierMatch
{
    private static readonly string[] LegalSuffixes =
        ["inc", "inc.", "llc", "ltd", "ltd.", "gmbh", "ag", "sa", "s.a.", "spa", "s.p.a.", "srl", "s.r.l.", "plc", "corp", "corporation", "co", "co."];

    public static bool Matches(string dealSupplier, string wanted)
    {
        var a = Normalize(dealSupplier);
        var b = Normalize(wanted);
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        return string.Equals(a, b, StringComparison.Ordinal) ||
               $" {a} ".Contains($" {b} ", StringComparison.Ordinal) ||
               $" {b} ".Contains($" {a} ", StringComparison.Ordinal);
    }

    public static string Normalize(string name)
    {
        var chars = name.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray();
        var words = new string(chars)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !LegalSuffixes.Contains(w, StringComparer.Ordinal))
            .ToList();
        return string.Join(' ', words);
    }
}

/// <summary>The feed-backed <see cref="IMarketDealLookup"/> for hosts with no Market database.</summary>
public sealed class ProviderMarketDealLookup(IMarketIntelligenceProvider provider) : IMarketDealLookup
{
    public async Task<IReadOnlyList<MarketDeal>> GetBySupplierAsync(string supplierName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(supplierName))
        {
            return [];
        }

        var feed = await provider.GetDealsAsync(feedVersion: null, cancellationToken).ConfigureAwait(false);
        if (feed.IsFailure)
        {
            return [];
        }

        return feed.Value.Deals
            .Where(d => MarketSupplierMatch.Matches(d.Supplier, supplierName))
            .OrderByDescending(d => d.UpdatedAt)
            .ToList();
    }

    public async Task<IReadOnlyList<MarketDeal>> GetByCategoryAsync(string category, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return [];
        }

        var feed = await provider.GetDealsAsync(feedVersion: null, cancellationToken).ConfigureAwait(false);
        if (feed.IsFailure)
        {
            return [];
        }

        return feed.Value.Deals
            .Where(d => string.Equals(d.Category, category.Trim(), StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(d => d.UpdatedAt)
            .ToList();
    }
    public async Task<IReadOnlyList<MarketDeal>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var feed = await provider.GetDealsAsync(feedVersion: null, cancellationToken).ConfigureAwait(false);
        return feed.IsFailure ? [] : feed.Value.Deals.OrderByDescending(d => d.UpdatedAt).ToList();
    }

    public async Task<string?> GetCorpusVersionAsync(CancellationToken cancellationToken = default)
    {
        var feed = await provider.GetDealsAsync(feedVersion: null, cancellationToken).ConfigureAwait(false);
        return feed.IsFailure ? null : $"{feed.Value.FeedVersion}:{feed.Value.Deals.Count}";
    }
}
