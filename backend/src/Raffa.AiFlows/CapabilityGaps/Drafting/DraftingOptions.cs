namespace Raffa.AiFlows.CapabilityGaps.Drafting;

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

    /// <summary>Backstop deadline of each drafting agent on the step runner (the planner and the
    /// writer). The workflow had no deadline of its own, only the gateway's per-call timeout and
    /// retries, so this is deliberately far above them: it bounds a hung call and does not change a
    /// normal turn. A step that overruns degrades by its policy with an explicit "deadline" reason.</summary>
    public TimeSpan AgentDeadline { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>F4-T05: when the pack already carries the turn's council plays
    /// (<c>calc:council:play[n]</c>), skip the offer-planner call and plan the offer from them
    /// (<see cref="DraftPlan.FromPack"/>) — one model call fewer per draft. <b>Off by default</b>
    /// (today's behaviour: the planner always runs); the decision to keep or retire the planner
    /// (R2-04) rests on a rubric comparison, so this is an A/B switch.</summary>
    public bool ReuseCouncilPlays { get; set; }

    /// <summary>When on, a draft that fails the formal / legal-tone check
    /// (<see cref="DraftToneChecker"/>) is sent back to the writer once with the finding named, like a
    /// grounding violation. <b>Off by default</b>: findings are only reported on the draft
    /// (<see cref="DraftOutcome.ToneFindings"/>) and never change what is returned.</summary>
    public bool EnforceTone { get; set; }
}
