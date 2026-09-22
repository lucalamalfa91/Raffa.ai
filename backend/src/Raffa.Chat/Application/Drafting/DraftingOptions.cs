namespace Raffa.Chat.Application.Drafting;

/// <summary>Bound from <c>Chat:Drafting</c>. The kill switch and the bounds of the drafting
/// workflow (<see cref="NegotiationDraftingWorkflow"/>, ADR-030 D3).</summary>
public sealed class DraftingOptions
{
    public const string SectionName = "Chat:Drafting";

    /// <summary>Off, and every draft comes from the deterministic template alone — no agent call.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Below this many pack items the agents have nothing to plan an offer from; the
    /// template writes the email from whatever facts exist.</summary>
    public int MinPackItems { get; set; } = 2;

    /// <summary>Items handed to each agent (the pack is already in priority order).</summary>
    public int MaxItemsPerAgent { get; set; } = 14;

    /// <summary>Longest email body accepted from the writer; a longer one is a guard violation.</summary>
    public int MaxBodyChars { get; set; } = 2500;
}
