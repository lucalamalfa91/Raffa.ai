using Microsoft.EntityFrameworkCore;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Savings;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// The workspace's reporting settings for Spesa e risparmi, over the Identity module (Savings may
/// not read it): the Admin's reporting currency (else the workspace's own, else CHF), the fiscal
/// year start (a calendar year while it is not set) and the Admin's budget rates. A currency
/// without a rate is never converted: its contracts stay out of the totals and are listed.
/// </summary>
internal sealed class WorkspaceFinanceSettingsReader(IdentityWorkspaceDbContext dbContext, ITenantContext tenantContext)
    : IWorkspaceFinanceSettingsReader
{
    public async Task<WorkspaceFinanceSettings> GetAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var workspace = await dbContext.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);
        if (workspace is null)
        {
            return WorkspaceFinanceSettings.Default;
        }

        return new WorkspaceFinanceSettings(
            WorkspaceSettingsService.EffectiveCurrency(workspace),
            workspace.FiscalYearStartMonth is >= 1 and <= 12 ? workspace.FiscalYearStartMonth.Value : 1,
            WorkspaceSettingsService.RatesOf(workspace));
    }
}
