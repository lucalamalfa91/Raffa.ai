using System.Globalization;
using Raffa.AiGateway.Contracts;

namespace Raffa.Market.Retrieval;

/// <summary>
/// Who produced an embedding vector (F7-T06): the model id and whether it is the deterministic
/// fixture pseudo-embedding. Two vectors are only comparable when both match — a cosine distance
/// between a fixture vector and a real one (or between two different models) is a number without
/// meaning, and ranking by it quietly returns noise.
/// </summary>
/// <param name="Model">Model id the embedding gateway reported (<c>AiCallMetadata.ModelId</c>).</param>
/// <param name="IsFixture">True for the fixture gateway's pseudo-embedding.</param>
public readonly record struct MarketEmbeddingIdentity(string Model, bool IsFixture)
{
    /// <summary>
    /// The fixture gateway stamps every call with a prompt version starting with this prefix
    /// (<c>"fixture-v1"</c>) while reporting the *same* model id as the real deployment it stands in
    /// for, so the prompt version is the only marker it leaves in <see cref="AiCallMetadata"/>. A
    /// test pins this against the real <c>FixtureAiGateway</c>, so a rename there fails loudly
    /// instead of silently turning the guard off.
    /// </summary>
    public const string FixturePromptVersionPrefix = "fixture";

    /// <summary>The identity of the vector a gateway call produced.</summary>
    public static MarketEmbeddingIdentity From(AiCallMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        return new MarketEmbeddingIdentity(
            metadata.ModelId,
            metadata.PromptVersion?.StartsWith(FixturePromptVersionPrefix, StringComparison.OrdinalIgnoreCase) == true);
    }
}

/// <summary>How many index rows were produced by one (model, fixture-or-not) combination.</summary>
/// <param name="Model">The row's <c>model</c> column.</param>
/// <param name="IsFixture">The row's <c>is_fixture</c> column; <see langword="null"/> = written before
/// the flag existed.</param>
/// <param name="Count">Rows with this combination.</param>
public sealed record MarketEmbeddingPopulation(string Model, bool? IsFixture, int Count);

/// <summary>What a query vector may be ranked against in the current index.</summary>
public enum MarketEmbeddingIndexState
{
    /// <summary>No embeddings at all: nothing to rank.</summary>
    Empty,

    /// <summary>Every row is comparable with the query.</summary>
    Compatible,

    /// <summary>Some rows are comparable and some are not (an index part-way through a re-embed).
    /// Only the comparable ones may be ranked.</summary>
    Mixed,

    /// <summary>No row is comparable: the index was built with another model, or with fixture
    /// vectors (or the query is a fixture and the index is real). Ranking would be noise.</summary>
    Incompatible,
}

/// <summary>
/// The query-time guard of F7-T06: decides whether a query embedding may be ranked against the
/// <c>market_embedding</c> index. Pure functions, so the rule is testable without a database.
/// </summary>
public static class MarketEmbeddingCompatibility
{
    /// <summary>
    /// True when a row (<paramref name="rowModel"/>, <paramref name="rowIsFixture"/>) is comparable
    /// with <paramref name="query"/>: same model id and same kind. A row with no flag (written before
    /// the flag existed) is judged on the model id alone — it is tolerated, never silently trusted
    /// for a re-embed (see <see cref="Infrastructure.Entities.MarketEmbeddingEntity.IsFixture"/>).
    /// </summary>
    public static bool IsComparable(string rowModel, bool? rowIsFixture, MarketEmbeddingIdentity query) =>
        string.Equals(rowModel, query.Model, StringComparison.Ordinal)
        && (rowIsFixture is null || rowIsFixture.Value == query.IsFixture);

    /// <summary>Classifies the index for <paramref name="query"/>.</summary>
    public static MarketEmbeddingIndexState Evaluate(
        IEnumerable<MarketEmbeddingPopulation> populations, MarketEmbeddingIdentity query)
    {
        ArgumentNullException.ThrowIfNull(populations);

        var total = 0;
        var comparable = 0;
        foreach (var population in populations)
        {
            total += population.Count;
            if (IsComparable(population.Model, population.IsFixture, query))
            {
                comparable += population.Count;
            }
        }

        if (total == 0)
        {
            return MarketEmbeddingIndexState.Empty;
        }

        if (comparable == 0)
        {
            return MarketEmbeddingIndexState.Incompatible;
        }

        return comparable == total ? MarketEmbeddingIndexState.Compatible : MarketEmbeddingIndexState.Mixed;
    }

    /// <summary>Rows in <paramref name="populations"/> that are comparable with <paramref name="query"/>.</summary>
    public static int ComparableCount(IEnumerable<MarketEmbeddingPopulation> populations, MarketEmbeddingIdentity query) =>
        populations.Where(p => IsComparable(p.Model, p.IsFixture, query)).Sum(p => p.Count);

    /// <summary>
    /// The error text for an <see cref="MarketEmbeddingIndexState.Incompatible"/> index: what the
    /// query was embedded with, what the index holds, and the remedy. Stable prefix
    /// (<see cref="IncompatibleIndexErrorPrefix"/>) so callers and logs can match on it.
    /// </summary>
    public static string DescribeIncompatibility(
        IEnumerable<MarketEmbeddingPopulation> populations, MarketEmbeddingIdentity query)
    {
        static string Kind(bool? isFixture) => isFixture switch
        {
            true => "fixture",
            false => "real",
            null => "unmarked",
        };

        var held = string.Join(
            ", ",
            populations
                .OrderByDescending(p => p.Count)
                .Select(p => $"{p.Model} ({Kind(p.IsFixture)}, {p.Count.ToString(CultureInfo.InvariantCulture)} rows)"));

        return $"{IncompatibleIndexErrorPrefix}: the query was embedded with {query.Model} " +
               $"({(query.IsFixture ? "fixture" : "real")}) but the market index holds {held}. " +
               "Their vectors are not comparable; re-run the market ingestion (`ingest-market`) with the " +
               "embedding gateway the application uses.";
    }

    /// <summary>Start of the error returned for an incompatible index.</summary>
    public const string IncompatibleIndexErrorPrefix = "Market embedding index mismatch";
}
