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

    /// <summary>
    /// The roster (task E14/F04/US01/T01, story us-01-members-api; ADR-026 §D3, ADR-025 §D.4): a
    /// live <see cref="WorkspaceMembership"/> renders <see cref="WorkspaceMemberStatus.Active"/>;
    /// no membership plus a live (unaccepted, unrevoked, unexpired) <see cref="WorkspaceInvitation"/>
    /// renders <see cref="WorkspaceMemberStatus.Invited"/>. <b>Never a scan of
    /// <see cref="WorkspaceUser"/></b> — a removed member keeps their user row by design (audit
    /// continuity; <see cref="WorkspaceSignIn"/> needs it), so a user-driven roster would list them
    /// and, because <see cref="WorkspaceUser.ExternalSubjectId"/> stays bound, render them
    /// <see cref="WorkspaceMemberStatus.Active"/> — the defect this method exists to prevent. The
    /// two queries below anchor on <see cref="WorkspaceMembership"/> and
    /// <see cref="WorkspaceInvitation"/> respectively; <see cref="WorkspaceUser"/> is joined only
    /// for the id/email/display-name this roster needs, the same "materialize flat rows, then
    /// derive in memory" shape <c>WorkspaceRoleResolver.ResolveMembershipRoleAsync</c> already uses
    /// for its own highest-role pick.
    ///
    /// <para>
    /// Correct for both this phase's <see cref="InviteAsync"/> (still writes a live membership at
    /// invite time) and the phase-3 redesign (writes only <see cref="WorkspaceInvitation"/> until
    /// accept): this method reads both tables and derives, and does not care which write path
    /// produced either row.
    /// </para>
    /// </summary>
    public async Task<IReadOnlyList<WorkspaceMemberRecord>> ListMembersAsync(
        TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var now = clock.UtcNow;

        var membershipRows = await (
            from membership in db.WorkspaceMemberships
            join user in db.WorkspaceUsers on membership.WorkspaceUserId equals user.Id
            join role in db.WorkspaceRoles on membership.WorkspaceRoleId equals role.Id
            where membership.TenantId == tenantId && user.TenantId == tenantId && role.TenantId == tenantId
            select new MembershipRosterRow(user.Id, user.Email, user.DisplayName, role.Name))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // A WorkspaceUser row always exists for an invited email by the time an invitation exists
        // -- InviteAsync writes it unconditionally today, and the phase-3 redesign still writes it
        // at invite time (only the membership write moves to accept) -- so this join is exact, not
        // a LEFT JOIN standing in for a guess. ComposeRoster below decides which invitations are
        // still live and which already lost to an active membership.
        var invitationRows = await (
            from invitation in db.WorkspaceInvitations
            join user in db.WorkspaceUsers on invitation.Email equals user.Email
            join role in db.WorkspaceRoles on invitation.WorkspaceRoleId equals role.Id
            where invitation.TenantId == tenantId && user.TenantId == tenantId && role.TenantId == tenantId
            select new InvitationRosterRow(
                user.Id, invitation.Email, user.DisplayName, role.Name,
                invitation.AcceptedAt, invitation.RevokedAt, invitation.ExpiresAt))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return ComposeRoster(membershipRows, invitationRows, now);
    }

    /// <summary>
    /// The pure derivation half of <see cref="ListMembersAsync"/> — no database, so
    /// <c>Raffa.Identity.Workspace.Tests.WorkspaceRosterTests</c> can prove every rule directly
    /// against hand-built rows, the same way <see cref="WorkspaceRoleClaimResolver"/>'s own
    /// precedence logic is proven without one. Public for that reason, not because a caller outside
    /// this file is expected.
    /// </summary>
    public static IReadOnlyList<WorkspaceMemberRecord> ComposeRoster(
        IReadOnlyList<MembershipRosterRow> memberships,
        IReadOnlyList<InvitationRosterRow> invitations,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(memberships);
        ArgumentNullException.ThrowIfNull(invitations);

        var active = memberships
            .GroupBy(row => row.UserId)
            .Select(group =>
            {
                // AC-5: a person holding two memberships appears once, at the highest role,
                // reusing the precedence WorkspaceRoleResolver.cs:101-103,116-118 already applies
                // (via WorkspaceRoleClaimResolver, the one place that precedence is defined). No
                // second ordering is invented.
                WorkspaceRoleClaimResolver.TryResolve(
                    group.Select(row => row.RoleName.ToString()), out var highestRole);
                var first = group.First();
                return new WorkspaceMemberRecord(
                    first.UserId, first.Email, first.DisplayName, highestRole, WorkspaceMemberStatus.Active);
            })
            .ToList();

        var activeEmails = new HashSet<string>(active.Select(member => member.Email), StringComparer.Ordinal);

        var invited = invitations
            // ADR-025 Rule D.4e / ADR-026 §D3: only a *live* invitation -- unaccepted, unrevoked,
            // unexpired as of `now` -- renders Invited; an accepted/revoked/expired one is silently
            // absent, never a status value of its own.
            .Where(row => row.AcceptedAt is null && row.RevokedAt is null && row.ExpiresAt > now)
            // "No membership and a live invitation" (ADR-026 §D3): a live invitation for someone
            // who already holds a membership contributes nothing here -- the Active row already
            // speaks for them.
            .Where(row => !activeEmails.Contains(row.Email))
            .Select(row => new WorkspaceMemberRecord(
                row.UserId, row.Email, row.DisplayName, row.RoleName, WorkspaceMemberStatus.Invited));

        return active
            .Concat(invited)
            .OrderBy(member => member.Email, StringComparer.Ordinal)
            .ToList();
    }
}

/// <summary>One roster row (ADR-026 §D3): the wire shape is built from this at the host boundary
/// (<see cref="Raffa.Api.WorkspaceMembersEndpointExtensions"/>), never returned as-is — <see cref="Id"/>
/// is <see cref="WorkspaceUser"/>'s own id, stable across the Active/Invited transition an accept
/// performs, since both branches join back to the same user row (never the membership or
/// invitation row's own id, which would change on removal/re-invite).</summary>
public sealed record WorkspaceMemberRecord(
    EntityId Id, string Email, string? Name, WorkspaceRoleName Role, WorkspaceMemberStatus Status);

/// <summary>ADR-026 §D3: derived, never stored — see <see cref="WorkspaceMembershipService.ComposeRoster"/>.</summary>
public enum WorkspaceMemberStatus
{
    Active,
    Invited,
}

/// <summary>A flat, already-tenant-scoped <see cref="WorkspaceMembership"/> row, joined to its
/// <see cref="WorkspaceUser"/> and <see cref="WorkspaceRole"/>, as <see cref="WorkspaceMembershipService.ListMembersAsync"/>
/// materializes it before <see cref="WorkspaceMembershipService.ComposeRoster"/> collapses one
/// person's several memberships to their highest role.</summary>
public sealed record MembershipRosterRow(EntityId UserId, string Email, string? DisplayName, WorkspaceRoleName RoleName);

/// <summary>A flat, already-tenant-scoped <see cref="WorkspaceInvitation"/> row, joined the same
/// way as <see cref="MembershipRosterRow"/>, carrying the three columns
/// <see cref="WorkspaceMembershipService.ComposeRoster"/> needs to decide liveness
/// (<see cref="AcceptedAt"/>/<see cref="RevokedAt"/> null, <see cref="ExpiresAt"/> in the future).</summary>
public sealed record InvitationRosterRow(
    EntityId UserId,
    string Email,
    string? DisplayName,
    WorkspaceRoleName RoleName,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset ExpiresAt);
