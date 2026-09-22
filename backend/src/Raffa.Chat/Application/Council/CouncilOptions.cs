namespace Raffa.Chat.Application.Council;

/// <summary>Bound from <c>Chat:Council</c>. The kill switch and the bounds of the negotiation
/// council (<see cref="NegotiationCouncil"/>).</summary>
public sealed class CouncilOptions
{
    public const string SectionName = "Chat:Council";

    /// <summary>Off, and every savings/negotiation turn is answered from the deterministic pack
    /// alone — exactly the behaviour before the council existed.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The council only convenes over a pack with at least this many items; a thinner
    /// pack has nothing for three agents to reason about.</summary>
    public int MinPackItems { get; set; } = 3;

    /// <summary>Items handed to each round-one analyst (the pack is already in priority order).</summary>
    public int MaxItemsPerAgent { get; set; } = 14;

    /// <summary>Plays kept from the strategist, in its own rank order.</summary>
    public int MaxPlays { get; set; } = 4;

    /// <summary>The market researcher (<see cref="MarketResearcher"/>): off, and no Ask turn queries
    /// the market RAG — the market data check and the council still run.</summary>
    public bool MarketResearchEnabled { get; set; } = true;

    /// <summary>Market RAG queries run per turn, in the researcher's own order.</summary>
    public int MarketResearchMaxQueries { get; set; } = 3;

    /// <summary>Notes retrieved per query.</summary>
    public int MarketResearchTopK { get; set; } = 3;

    /// <summary>Market notes the researcher may add to one turn's pack, all queries together.</summary>
    public int MarketResearchMaxItems { get; set; } = 6;
}
