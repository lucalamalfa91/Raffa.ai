using Raffa.Identity.Workspace.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>
/// Application service for task E01/F05/US01/T02 (story us-01-workspace-roles AC-3; ADR-010;
/// produces the `workspace-membership` artifact): composes the pure decision logic in
/// <c>Domain</c> — <see cref="WorkspaceRoleClaimResolver"/>, <see cref="WorkspaceMembershipFactory"/>,
/// <see cref="WorkspaceSignIn"/> — with the EF Core reads and writes those decisions need. Not yet
/// called by a host (no `/api/workspaces` endpoint exists — AC-2 — see
/// <see cref="ServiceCollectionExtensions"/>); registered defensively so whichever future task adds
/// that endpoint only has to inject this type.
///
/// Every public method opens its own <see cref="ITenantContext.BeginScope"/> for the tenant it is
/// given, rather than trusting the caller to have already entered one: ADR-009's RLS backstop
/// fails *closed* when no tenant claim is set on the connection, so an inherited-but-wrong ambient
/// scope would silently return "not found" instead of a clear error. Scoping to the exact tenant a
/// call is about removes that failure mode entirely — nested scopes restore the previous value on
/// dispose (<see cref="ITenantContext.BeginScope"/>'s own doc comment), so calling this from
/// within an already-scoped request for the *same* tenant is harmless.
/// </summary>
public sealed class WorkspaceMembershipService(
    IdentityWorkspaceDbContext db, ITenantContext tenantContext, IClock clock)
{
    /// <summary>
    /// Invites <paramref name="email"/> into <paramref name="tenantId"/> with the role resolved
    /// from <paramref name="roleClaimValues"/> (AC-3: "Role assignment resolves from OIDC
    /// claims"). See <see cref="WorkspaceRoleClaimResolver"/> for the accepted claim shapes.
    /// </summary>
    public Task<Result<WorkspaceMembership>> InviteFromOidcClaimsAsync(
        TenantId tenantId,
        string email,
        IEnumerable<string> roleClaimValues,
        CancellationToken cancellationToken = default)
    {
        if (!WorkspaceRoleClaimResolver.TryResolve(roleClaimValues, out var role))
        {
            return Task.FromResult(Result<WorkspaceMembership>.Failure(
                "no recognized workspace role (Admin/Procurement/Legal/Finance/Read-only) in the " +
                $"supplied OIDC claims: [{string.Join(", ", roleClaimValues)}]."));
        }

        return InviteAsync(tenantId, email, role, cancellationToken);
    }

    /// <summary>
    /// Invites <paramref name="email"/> into <paramref name="tenantId"/> with an explicit
    /// <paramref name="role"/>. Idempotent: an email not seen before in this tenant gets a new
    /// <see cref="WorkspaceUser"/> row (not yet signed in); an email already invited/linked in
    /// this tenant is reused, so a second invite with a *different* role adds a second membership
    /// instead of erroring, while a repeat of the *same* role fails cleanly (no duplicate row).
    /// </summary>
    public async Task<Result<WorkspaceMembership>> InviteAsync(
        TenantId tenantId,
        string email,
        WorkspaceRoleName role,
        CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var now = clock.UtcNow;

        var roleRow = await db.WorkspaceRoles
            .SingleOrDefaultAsync(r => r.TenantId == tenantId && r.Name == role, cancellationToken)
            .ConfigureAwait(false);
        if (roleRow is null)
        {
            return Result<WorkspaceMembership>.Failure(
                $"workspace {tenantId} has no seeded '{role}' role; every workspace is expected " +
                "to be created via WorkspaceFactory.CreateWorkspaceWithDefaultRoles.");
        }

        var user = await db.WorkspaceUsers
            .SingleOrDefaultAsync(u => u.TenantId == tenantId && u.Email == email, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            var createResult = WorkspaceMembershipFactory.CreateInvitedUser(tenantId, email, now);
            if (createResult.IsFailure)
            {
                return Result<WorkspaceMembership>.Failure(createResult.Error);
            }

            user = createResult.Value;
            db.WorkspaceUsers.Add(user);
        }

        var alreadyMember = await db.WorkspaceMemberships
            .AnyAsync(m => m.WorkspaceUserId == user.Id && m.WorkspaceRoleId == roleRow.Id, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyMember)
        {
            return Result<WorkspaceMembership>.Failure($"{email} already holds the {role} role in this workspace.");
        }

        var membershipResult = WorkspaceMembershipFactory.CreateMembership(user, roleRow, now);
        if (membershipResult.IsFailure)
        {
            return membershipResult;
        }

        db.WorkspaceMemberships.Add(membershipResult.Value);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return membershipResult;
    }

    /// <summary>
    /// Links a first sign-in (<see cref="WorkspaceUser.LinkExternalSubject"/>) or resolves a
    /// repeat one. See <see cref="WorkspaceSignIn.ResolveSignedInUser"/> for the decision itself.
    /// Does not (re-)assign a role: role assignment happens at invite time
    /// (<see cref="InviteAsync"/>/<see cref="InviteFromOidcClaimsAsync"/>); continuously
    /// re-syncing role claims on every sign-in is a deliberately separate concern left to a future
    /// task rather than guessed at here.
    /// </summary>
    public async Task<Result<WorkspaceUser>> LinkSignInAsync(
        TenantId tenantId,
        string externalSubjectId,
        string email,
        CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);

        var existingByExternalSubject = await db.WorkspaceUsers
            .SingleOrDefaultAsync(
                u => u.TenantId == tenantId && u.ExternalSubjectId == externalSubjectId, cancellationToken)
            .ConfigureAwait(false);

        var existingByEmail = existingByExternalSubject is null
            ? await db.WorkspaceUsers
                .SingleOrDefaultAsync(u => u.TenantId == tenantId && u.Email == email, cancellationToken)
                .ConfigureAwait(false)
            : null;

        var result = WorkspaceSignIn.ResolveSignedInUser(existingByExternalSubject, existingByEmail, externalSubjectId);
        if (result.IsFailure)
        {
            return result;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return result;
    }
}
