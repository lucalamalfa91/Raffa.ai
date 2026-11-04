using Raffa.SharedKernel;
using Raffa.SharedKernel.Savings;

namespace Raffa.Savings.Infrastructure;

/// <summary>The reporting settings of a host that cannot read the workspace (no Identity module):
/// <see cref="WorkspaceFinanceSettings.Default"/>. The API host replaces it with the workspace's own.</summary>
internal sealed class DefaultWorkspaceFinanceSettingsReader : IWorkspaceFinanceSettingsReader
{
    public Task<WorkspaceFinanceSettings> GetAsync(TenantId tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(WorkspaceFinanceSettings.Default);
}
