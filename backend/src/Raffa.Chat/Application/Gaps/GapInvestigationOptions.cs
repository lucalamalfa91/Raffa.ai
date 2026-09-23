namespace Raffa.Chat.Application.Gaps;

/// <summary>Bound from <c>Chat:GapInvestigation</c>. The kill switch and the bounds of the
/// capability investigator (<see cref="CapabilityInvestigator"/>, ADR-031).</summary>
public sealed class GapInvestigationOptions
{
    public const string SectionName = "Chat:GapInvestigation";

    /// <summary>Off, and only the fixed <see cref="CapabilityGapCatalog"/> recognises a gap — no
    /// investigator call on any turn (the pre-ADR-031 behaviour, exactly).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The lowest verdict confidence that turns a turn into a gap reply: <c>medium</c>
    /// (default) accepts medium and high, <c>high</c> accepts high only. A low-confidence verdict
    /// never hijacks a turn.</summary>
    public string MinConfidence { get; set; } = CapabilityInvestigatorAgent.ConfidenceMedium;

    /// <summary>The investigator runs before the turn is answered, so a slow call must not hold
    /// the answer: past this budget the turn goes on as if nothing was found.</summary>
    public int TimeoutSeconds { get; set; } = 8;
}
