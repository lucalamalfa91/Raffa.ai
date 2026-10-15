namespace Raffa.Market.Infrastructure.Entities;

/// <summary>
/// F7-T08: one version of the market corpus (blue/green). Rows of <c>market_record</c> and
/// <c>market_embedding</c> belong to exactly one version; readers see only the
/// <see cref="MarketCorpusStates.Active"/> one, so a new corpus is built next to the live one and
/// becomes visible with one atomic switch.
/// </summary>
public sealed class MarketCorpusVersionEntity
{
    /// <summary>The version id (the key rows point at).</summary>
    public required string Version { get; set; }

    /// <summary><see cref="MarketCorpusStates"/>: building, active or retired.</summary>
    public required string State { get; set; }

    /// <summary>When the version was created.</summary>
    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>When it last became active (null while it never was).</summary>
    public DateTimeOffset? ActivatedAt { get; set; }

    /// <summary>The feed version it was built from, when known.</summary>
    public string? FeedVersion { get; set; }
}

/// <summary>Lifecycle states of a <see cref="MarketCorpusVersionEntity"/>.</summary>
public static class MarketCorpusStates
{
    /// <summary>Being built; invisible to readers.</summary>
    public const string Building = "building";

    /// <summary>The one version readers see.</summary>
    public const string Active = "active";

    /// <summary>Previously active; kept for rollback until purged.</summary>
    public const string Retired = "retired";
}

/// <summary>Well-known corpus version ids.</summary>
public static class MarketCorpusVersions
{
    /// <summary>The version every row that predates blue/green belongs to (seeded active by the migration).</summary>
    public const string Initial = "initial";
}
