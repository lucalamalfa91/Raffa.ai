namespace Raffa.Chat.Application.Gaps;

/// <summary>When the capability investigator (<see cref="CapabilityInvestigator"/>, ADR-031)
/// runs on a fresh in-domain turn.</summary>
public enum GapInvestigationMode
{
    /// <summary>Default (INV-03, decision D3): only a turn the deterministic
    /// <see cref="InvestigatorTrigger"/> flags runs the investigator — T1 no intent recognised,
    /// T2 Raffa could not answer, T3 an operational request nothing in the catalog covers. No
    /// token is spent on an ordinary question Raffa answered.</summary>
    Triggered = 0,

    /// <summary>The pre-INV-02 behaviour, for diagnosis only: one investigator call on every fresh
    /// typed in-domain turn. Switched on by configuration (and back off the same way), never by
    /// code; it is also the baseline the reduction in <c>gaps-v1</c> calls is measured against.</summary>
    Always = 1,
}

/// <summary>Bound from <c>Chat:GapInvestigation</c> and read through <c>IOptionsMonitor</c>, so
/// the mode and the kill switch change without a restart. The kill switch and the bounds of the
/// capability investigator (<see cref="CapabilityInvestigator"/>, ADR-031).</summary>
public sealed class GapInvestigationOptions
{
    public const string SectionName = "Chat:GapInvestigation";

    /// <summary>Off, and only the fixed <see cref="CapabilityGapCatalog"/> recognises a gap — no
    /// investigator call and no follow-up on any turn (the pre-ADR-031 behaviour, exactly).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary><see cref="GapInvestigationMode.Triggered"/> (default) or
    /// <see cref="GapInvestigationMode.Always"/> (diagnosis). Meaningless while
    /// <see cref="Enabled"/> is false.</summary>
    public GapInvestigationMode Mode { get; set; } = GapInvestigationMode.Triggered;

    /// <summary>The lowest verdict confidence that appends a capability follow-up: <c>medium</c>
    /// (default) accepts medium and high, <c>high</c> accepts high only. A low-confidence verdict
    /// adds nothing.</summary>
    public string MinConfidence { get; set; } = CapabilityInvestigatorAgent.ConfidenceMedium;

    /// <summary>The investigator runs beside the answer and never delays it; past this budget
    /// the check ends as if nothing was found and no follow-up is appended. The web client polls
    /// for a pending follow-up a little longer than this.</summary>
    public int TimeoutSeconds { get; set; } = 12;

    /// <summary>
    /// Jev classify-role pilot (see <c>Raffa.AiGateway.Configuration.AiGatewayJevOptions</c>):
    /// when that pilot's own <c>Enabled</c> flag is on, <see cref="CapabilityInvestigator"/> asks
    /// Jev for the verdict/knownGapKey/nearestCapabilityKey decision first -- a real classification
    /// choice, never delegated to the LLM -- and only falls through to the `analyst`-role Foundry
    /// call for the <c>gap</c> verdict's free-text feature description (Jev's choice/noul/score
    /// primitives cannot write that). A Jev call failure falls back to the pre-existing
    /// Foundry-only path unchanged (fail-open never regresses below today's behaviour).
    /// </summary>
    /// <remarks>
    /// <see cref="JevHighConfidenceThreshold"/>/<see cref="JevMediumConfidenceThreshold"/> bucket
    /// Jev's raw <c>[0,1]</c> calibrated confidence into the existing high/medium/low vocabulary
    /// <see cref="MinConfidence"/> and <see cref="CapabilityInvestigator.MeetsThreshold"/> already
    /// use -- starting points, not a measured calibration for this specific taxonomy (the
    /// documented risk in <c>AiGatewayJevOptions</c>: Jev's confidence needs per-question
    /// calibration, and none exists yet for this four-way verdict). Revisit once the pilot has
    /// scored volume against a held-out set.
    /// </remarks>
    public double JevHighConfidenceThreshold { get; set; } = 0.85;

    /// <summary>See <see cref="JevHighConfidenceThreshold"/>'s own doc comment.</summary>
    public double JevMediumConfidenceThreshold { get; set; } = 0.6;
}
