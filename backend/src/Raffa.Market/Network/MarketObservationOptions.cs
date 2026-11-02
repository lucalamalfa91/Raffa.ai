namespace Raffa.Market.Network;

/// <summary>
/// The customer network benchmark: what supplier replies teach the shared market base. Bound from
/// <c>Market:Observations</c>.
/// </summary>
public sealed class MarketObservationOptions
{
    public const string SectionName = "Market:Observations";

    /// <summary>Whether replies are recorded and published into the shared market base at all.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Server secret of the one-way workspace and observation keys. Set a long random value per installation.</summary>
    public string Pepper { get; set; } = "raffa-market-network-v1";

    /// <summary>
    /// How many different workspaces a benchmark rests on before it is published. Below it nothing is shared,
    /// so a published figure can never be traced to one customer.
    /// </summary>
    public int MinWorkspaces { get; set; } = 3;

    /// <summary>Older observations are not used any more.</summary>
    public int MaxAgeDays { get; set; } = 730;

    /// <summary>How often the worker refreshes the shared market base, in hours (0 = never on its own).</summary>
    public int RefreshHours { get; set; } = 24;
}
