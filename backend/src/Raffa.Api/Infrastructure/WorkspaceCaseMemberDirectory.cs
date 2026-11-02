using Microsoft.EntityFrameworkCore;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.Renewals.Application.CaseWork;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// The workspace members a renewal case can name (AskRaffa 4.0, BACKEND-SPEC §2.3): display name,
/// identity, and the title the Oggi role line shows — Admin is the CPO ("Head of Procurement", the
/// default approver), Procurement a "Buyer". Composition root only: Renewals may not read Identity.
/// </summary>
internal sealed class WorkspaceCaseMemberDirectory(IdentityWorkspaceDbContext dbContext, ITenantContext tenantContext) : ICaseMemberDirectory
{
    public async Task<IReadOnlyList<CaseMember>> ListAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var rows = await (
                from user in dbContext.WorkspaceUsers
                join membership in dbContext.WorkspaceMemberships on user.Id equals membership.WorkspaceUserId
                join role in dbContext.WorkspaceRoles on membership.WorkspaceRoleId equals role.Id
                where user.TenantId == tenantId && membership.TenantId == tenantId && role.TenantId == tenantId
                select new { user.Id, user.ExternalSubjectId, user.DisplayName, user.Email, role.Name })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.GroupBy(r => r.Id)
            .Select(g =>
            {
                var first = g.First();
                var roles = g.Select(x => x.Name).ToHashSet();
                var isAdmin = roles.Contains(WorkspaceRoleName.Admin);
                var title = isAdmin ? "Head of Procurement"
                    : roles.Contains(WorkspaceRoleName.Procurement) ? "Buyer"
                    : roles.First().ToString();
                return new CaseMember(first.Id.Value, first.ExternalSubjectId, string.IsNullOrWhiteSpace(first.DisplayName) ? first.Email : first.DisplayName!, title, isAdmin);
            })
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<string?> CompanyNameAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var name = await dbContext.Workspaces
            .Where(w => w.TenantId == tenantId)
            .Select(w => w.Name)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(name) ? null : name.Trim();
    }

}
