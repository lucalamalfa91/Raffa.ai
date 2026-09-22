using Raffa.Chat.Application.WebResearch;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// ADR-030 gate 2, host side: <see cref="IWorkspaceWebResearchPolicy"/> over the workspace row's
/// <c>web_research_enabled</c>. Lives in the host because it joins two modules that may not
/// reference each other (Chat asks, Identity/Workspace owns the row) -- the same reason
/// <see cref="WorkspaceRoleResolver"/> is here. Fail-closed: no row, no scope, no flag means
/// <see langword="false"/>.
/// </summary>
internal sealed class WorkspaceWebResearchPolicy(IdentityWorkspaceDbContext dbContext, ITenantContext tenantContext)
    : IWorkspaceWebResearchPolicy
{
    public async Task<bool> IsEnabledAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        return await dbContext.Workspaces
            .AsNoTracking()
            .Where(w => w.TenantId == tenantId)
            .Select(w => w.WebResearchEnabled)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
