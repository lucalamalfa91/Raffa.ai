namespace Raffa.Chat.Application.Gaps;

/// <summary>Bound from <c>Chat:GapInvestigation</c>. The kill switch and the bounds of the
/// capability investigator (<see cref="CapabilityInvestigator"/>, ADR-031).</summary>
public sealed class GapInvestigationOptions
{
    public const string SectionName = "Chat:GapInvestigation";

    /// <summary>Off, and only the fixed <see cref="CapabilityGapCatalog"/> recognises a gap — no
    /// investigator call and no follow-up on any turn (the pre-ADR-031 behaviour, exactly).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>The lowest verdict confidence that appends a capability follow-up: <c>medium</c>
    /// (default) accepts medium and high, <c>high</c> accepts high only. A low-confidence verdict
    /// adds nothing.</summary>
    public string MinConfidence { get; set; } = CapabilityInvestigatorAgent.ConfidenceMedium;

    /// <summary>The investigator runs beside the answer and never delays it; past this budget
    /// the check ends as if nothing was found and no follow-up is appended. The web client polls
    /// for a pending follow-up a little longer than this.</summary>
    public int TimeoutSeconds { get; set; } = 12;
}
