using Raffa.SharedKernel;

namespace Raffa.Chat.Application.WebResearch;

/// <summary>
/// Gate 2 of ADR-030 — whether this workspace's Admin opted into web research. The workspace row
/// lives in <c>Raffa.Identity.Workspace</c>, which this module may not reference (ADR-002
/// allow-list <c>[SharedKernel, AiGateway]</c>), so the composition root implements this port and
/// registers it; nothing in this module ever assumes a default of "enabled".
/// </summary>
public interface IWorkspaceWebResearchPolicy
{
    Task<bool> IsEnabledAsync(TenantId tenantId, CancellationToken cancellationToken = default);
}
