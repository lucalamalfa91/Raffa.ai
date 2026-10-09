namespace Raffa.Chat.Application.WebResearch;

/// <summary>
/// The one number <see cref="WebResearchBudget"/> needs from the web-research configuration: how
/// many research calls a tenant may take per UTC day (gate 3 of ADR-030). The budget is persistence
/// and lives in this module; the configuration it reads is owned by the web-research flow, which
/// implements this port (<see cref="WebResearchOptions"/> today) so the budget does not depend on the
/// flow's options type.
/// </summary>
public interface IWebResearchBudgetLimit
{
    /// <summary>Calls per tenant per UTC day. Zero or negative closes the gate.</summary>
    int DailyCallsPerTenant { get; }
}

/// <summary>
/// The limit of a host that configures no web research: zero, so the gate is closed — consistent
/// with the kill switch being off by default.
/// </summary>
public sealed class ClosedWebResearchBudgetLimit : IWebResearchBudgetLimit
{
    public int DailyCallsPerTenant => 0;
}
