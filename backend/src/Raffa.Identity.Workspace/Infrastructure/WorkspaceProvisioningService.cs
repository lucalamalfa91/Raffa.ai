using Raffa.Identity.Workspace.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>
/// Application service for task E01/F09/US01/T01 (r0-integration, AC-1 "create workspace" step):
/// persists the <see cref="WorkspaceTenant"/> + default <see cref="WorkspaceRole"/> catalog that
/// <see cref="WorkspaceFactory.CreateWorkspaceWithDefaultRoles"/> only builds in memory. Not
/// previously called by any host — no `/api/workspaces` endpoint existed yet (see
/// <see cref="ServiceCollectionExtensions"/>'s own doc comment and
/// <see cref="WorkspaceMembershipService"/>'s "not yet called by a host" note, which this task
/// resolves for the "create" half; <see cref="WorkspaceMembershipService"/> already covers the
/// "invite" half).
///
/// Opens its own <see cref="ITenantContext.BeginScope"/> for the *new* workspace's own tenant id
/// before inserting — required, not optional: <see cref="WorkspaceTenant"/>'s `Id == TenantId`
/// invariant means the very first row for this tenant is the workspace row itself, and ADR-009's
/// RLS `WITH CHECK` only accepts a write whose `tenant_id` matches the connection's active claim.
/// Mirrors <see cref="WorkspaceMembershipService"/>'s own per-call scoping convention.
/// </summary>
public sealed class WorkspaceProvisioningService(
    IdentityWorkspaceDbContext db, ITenantContext tenantContext, IClock clock)
{
    /// <summary>
    /// Creates a new workspace named <paramref name="name"/> together with its full default role
    /// catalog (Admin/Procurement/Legal/Finance/Read-only). Fails cleanly instead of surfacing a
    /// raw EF/Postgres constraint error when <paramref name="name"/> is blank.
    /// </summary>
    public async Task<Result<WorkspaceTenant>> CreateWorkspaceAsync(
        string name, CancellationToken cancellationToken = default)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0)
        {
            return Result<WorkspaceTenant>.Failure("A workspace 'name' is required.");
        }

        var (workspace, roles) = WorkspaceFactory.CreateWorkspaceWithDefaultRoles(trimmedName, clock);

        using var _ = tenantContext.BeginScope(workspace.TenantId);

        db.Workspaces.Add(workspace);
        db.WorkspaceRoles.AddRange(roles);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<WorkspaceTenant>.Success(workspace);
    }
}
