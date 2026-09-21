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
}
