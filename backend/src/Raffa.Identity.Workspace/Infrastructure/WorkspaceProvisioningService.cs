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
///
/// <para>
/// Task E14/F02/US01/T01 (wave w14; ADR-025 §D.2, ADR-009 w14 footer clause 6): the creator of a
/// workspace becomes its Admin <i>by virtue of creating it</i>. <see cref="CreateWorkspaceAsync"/>
/// now also writes the caller's own <see cref="WorkspaceUser"/> and Admin
/// <see cref="WorkspaceMembership"/> row, reusing
/// <see cref="WorkspaceMembershipFactory.CreateInvitedUser"/> and
/// <see cref="WorkspaceMembershipFactory.CreateMembership"/> rather than new construction logic —
/// in the <i>same</i> single <see cref="ITenantContext.BeginScope"/> and one
/// <c>SaveChangesAsync</c> this method already used for the workspace and its role catalog, so a
/// partial bootstrap is never
/// reachable — not even by a failure path (ADR-009 w14 footer clause 6: "strictly
/// one-scope-per-request"; the scope is entered only after every validation below has already
/// succeeded).
/// </para>
/// </summary>
public sealed class WorkspaceProvisioningService(
    IdentityWorkspaceDbContext db, ITenantContext tenantContext, IClock clock)
{
    /// <summary>
    /// Creates a new workspace named <paramref name="name"/> together with its full default role
    /// catalog (Admin/Procurement/Legal/Finance/Read-only) and makes <paramref name="callerIdentity"/>
    /// its Admin (ADR-025 Rule D.2b) — four writes, one scope, one <c>SaveChangesAsync</c>. Fails
    /// cleanly instead of surfacing a raw EF/Postgres constraint error when <paramref name="name"/>
    /// is blank, <paramref name="callerIdentity"/> is blank, or <paramref name="callerIdentity"/>
    /// does not parse as the email <see cref="WorkspaceMembershipFactory.CreateInvitedUser"/>
    /// expects (ADR-025 §B: "identity presented but malformed" stays a 400 through this existing
    /// <see cref="Result{T}"/> failure path, exactly like a blank name always has). Every failure
    /// below returns before <see cref="ITenantContext.BeginScope"/> is ever entered, so none of
    /// them can leave a partial tenant behind.
    /// </summary>
    public async Task<Result<WorkspaceTenant>> CreateWorkspaceAsync(
        string name, string callerIdentity, CancellationToken cancellationToken = default)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0)
        {
            return Result<WorkspaceTenant>.Failure("A workspace 'name' is required.");
        }

        var trimmedIdentity = callerIdentity?.Trim() ?? string.Empty;
        if (trimmedIdentity.Length == 0)
        {
            return Result<WorkspaceTenant>.Failure("A caller identity is required to create a workspace.");
        }

        var (workspace, roles) = WorkspaceFactory.CreateWorkspaceWithDefaultRoles(trimmedName, clock);
        var now = workspace.CreatedAt;
        var adminRole = roles.Single(r => r.Name == WorkspaceRoleName.Admin);

        var creatorResult = WorkspaceMembershipFactory.CreateInvitedUser(workspace.TenantId, trimmedIdentity, now);
        if (creatorResult.IsFailure)
        {
            return Result<WorkspaceTenant>.Failure(creatorResult.Error);
        }

        var membershipResult = WorkspaceMembershipFactory.CreateMembership(creatorResult.Value, adminRole, now);
        if (membershipResult.IsFailure)
        {
            return Result<WorkspaceTenant>.Failure(membershipResult.Error);
        }

        using var _ = tenantContext.BeginScope(workspace.TenantId);

        db.Workspaces.Add(workspace);
        db.WorkspaceRoles.AddRange(roles);
        db.WorkspaceUsers.Add(creatorResult.Value);
        db.WorkspaceMemberships.Add(membershipResult.Value);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result<WorkspaceTenant>.Success(workspace);
    }
}
