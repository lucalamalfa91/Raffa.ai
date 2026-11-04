using Microsoft.EntityFrameworkCore;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Savings;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// The workspace's reporting settings for Spesa e risparmi, over the Identity module (Savings may
/// not read it). Today: the workspace's own currency (CHF when it has none), a calendar fiscal year
/// and no budget rates — so a contract in another currency stays out of the totals instead of being
/// converted at a guessed rate.
/// </summary>
internal sealed class WorkspaceFinanceSettingsReader(IdentityWorkspaceDbContext dbContext, ITenantContext tenantContext)
    : IWorkspaceFinanceSettingsReader
{
    public async Task<WorkspaceFinanceSettings> GetAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var currency = await dbContext.Workspaces
            .Where(w => w.TenantId == tenantId)
            .Select(w => w.Currency)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return WorkspaceFinanceSettings.Default with
        {
            ReportingCurrency = string.IsNullOrWhiteSpace(currency) ? WorkspaceFinanceSettings.DefaultCurrency : currency.Trim().ToUpperInvariant(),
        };
    }
}
