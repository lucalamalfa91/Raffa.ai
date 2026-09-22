namespace Raffa.Chat.Application.WebResearch;

/// <summary>
/// <c>Chat:WebResearch</c> — the three gates and the bounds of the one path that may leave the
/// tenant's data (ADR-030). <see cref="Enabled"/> is the kill switch and is <b>off</b> by default:
/// an environment that never binds this section has no web research at all, no interview option
/// offering it, and an explicit "cerca sul web" gets a redirect explaining why. Bound by the host
/// before <c>AddChatModule</c>, the same TryAdd override contract as <c>Chat:Interview</c>.
/// </summary>
public sealed class WebResearchOptions
{
    public const string SectionName = "Chat:WebResearch";

    /// <summary>Gate 1 — the kill switch. Off: nothing is ever searched, offered or authorised.</summary>
    public bool Enabled { get; set; }

    /// <summary>Gate 2 — require the workspace's own Admin opt-in
    /// (<c>IWorkspaceWebResearchPolicy</c>) on top of the kill switch.</summary>
    public bool RequireWorkspaceOptIn { get; set; } = true;

    /// <summary>Gate 3 — calls per tenant per UTC day (<see cref="WebResearchBudget"/>). Zero or
    /// negative closes the gate.</summary>
    public int DailyCallsPerTenant { get; set; } = 20;

    /// <summary>Sources kept per research turn (1–10).</summary>
    public int MaxSources { get; set; } = 5;

    /// <summary>Upper bound on the sanitised query's length, in characters.</summary>
    public int MaxQueryChars { get; set; } = 300;

    /// <summary>The source cap, clamped to the range the research role accepts.</summary>
    public int EffectiveMaxSources => Math.Clamp(MaxSources, 1, 10);
}
