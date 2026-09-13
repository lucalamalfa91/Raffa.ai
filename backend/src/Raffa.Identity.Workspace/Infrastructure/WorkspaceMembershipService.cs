using Raffa.Identity.Workspace.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>
/// Application service for task E01/F05/US01/T02 (story us-01-workspace-roles AC-3; ADR-010;
/// produces the `workspace-membership` artifact): composes the pure decision logic in
/// <c>Domain</c> — <see cref="WorkspaceRoleClaimResolver"/>, <see cref="WorkspaceMembershipFactory"/>,
/// <see cref="WorkspaceSignIn"/>, <see cref="WorkspaceMembershipRemoval"/> — with the EF Core reads
/// and writes those decisions need.
///
/// <para>
/// Task E15/F01/US01/T01 (wave w14, ADR-025 §D.1/§D.3/§D.5, ADR-026 implication 3) redesigns the
/// invite half: <see cref="InviteAsync"/> now writes <see cref="WorkspaceUser"/> (if absent) and a
/// <see cref="WorkspaceInvitation"/> row — <b>never a membership</b>. The membership is written only
/// at <see cref="AcceptInvitationAsync"/>, and <see cref="RemoveMemberAsync"/> is the new removal
/// half (ADR-025 Rule D.5a-c). The token itself — minting, hashing, verifying, the pre-accept read —
/// is <see cref="WorkspaceInvitationService"/>'s job, one layer up; this service stays the single
/// writer of <see cref="WorkspaceUser"/>/<see cref="WorkspaceMembership"/>/<see cref="WorkspaceInvitation"/>
/// rows, the same separation <see cref="WorkspaceProvisioningService"/> (create) already keeps from
/// this type (invite/accept/remove).
/// </para>
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
    IdentityWorkspaceDbContext db, ITenantContext tenantContext, IClock clock, IAuditWriter auditWriter)
{
    /// <summary>
    /// Invites <paramref name="email"/> into <paramref name="tenantId"/> with the role resolved
    /// from <paramref name="roleClaimValues"/> (AC-3: "Role assignment resolves from OIDC
    /// claims"). See <see cref="WorkspaceRoleClaimResolver"/> for the accepted claim shapes.
    /// </summary>
    public Task<InviteOutcome> InviteFromOidcClaimsAsync(
        TenantId tenantId,
        string email,
        IEnumerable<string> roleClaimValues,
        string invitedBy,
        string tokenHash,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        if (!WorkspaceRoleClaimResolver.TryResolve(roleClaimValues, out var role))
        {
            return Task.FromResult(InviteOutcome.Failure(
                MembershipOperationStatus.ValidationFailed,
                "no recognized workspace role (Admin/Procurement/Legal/Finance/Read-only) in the " +
                $"supplied OIDC claims: [{string.Join(", ", roleClaimValues)}]."));
        }

        return InviteAsync(tenantId, email, role, invitedBy, tokenHash, expiresAt, cancellationToken);
    }

    /// <summary>
    /// Invites <paramref name="email"/> into <paramref name="tenantId"/> with an explicit
    /// <paramref name="role"/> (ADR-025 Rule D.1d). Idempotent: an email not seen before in this
    /// tenant gets a new <see cref="WorkspaceUser"/> row (not yet signed in); an email already
    /// invited/linked in this tenant is reused. Writes <b>no membership</b> — that happens only at
    /// <see cref="AcceptInvitationAsync"/> — so the caller (<see cref="WorkspaceInvitationService.IssueAsync"/>)
    /// supplies the already-minted <paramref name="tokenHash"/>/<paramref name="expiresAt"/> for the
    /// <see cref="WorkspaceInvitation"/> row this method writes alongside the user.
    ///
    /// <para>
    /// Rejects (as <see cref="MembershipOperationStatus.Conflict"/>, ADR-025 §D.1d /
    /// implication 9) when <paramref name="email"/> already holds a <b>live</b> membership at
    /// <paramref name="role"/>, or already has <b>any</b> live (unaccepted, unrevoked) invitation in
    /// this tenant — the partial unique index <c>(tenant_id, lower(email)) WHERE accepted_at IS NULL
    /// AND revoked_at IS NULL</c> (ADR-026 §D4) allows at most one of the latter regardless of role,
    /// so a second, concurrent invite that slips past this pre-check still hits that index; the
    /// resulting <see cref="DbUpdateException"/> is caught and translated to the identical
    /// <see cref="MembershipOperationStatus.Conflict"/>, never a 500. A <b>removed</b> person (no
    /// live membership, and their prior invitation revoked in the same transaction as the removal —
    /// <see cref="RemoveMemberAsync"/>) passes both checks and can be re-invited at the same role
    /// (AC-8).
    /// </para>
    /// </summary>
    public async Task<InviteOutcome> InviteAsync(
        TenantId tenantId,
        string email,
        WorkspaceRoleName role,
        string invitedBy,
        string tokenHash,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var now = clock.UtcNow;

        // Normalised once, here — the one production write path that creates a WorkspaceUser row
        // from an Admin-typed request body (WorkspaceProvisioningService's own creator path is
        // already lower-cased upstream by HeaderCallerIdentity; this path is not). Keeps this row
        // and the WorkspaceInvitation row this method also writes in the exact same casing, which
        // is what WorkspaceMembershipService.ListMembersAsync's own exact-equality join on Email
        // depends on, and matches ADR-026 §D4's own "stored lower-cased" column note.
        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;

        var roleRow = await db.WorkspaceRoles
            .SingleOrDefaultAsync(r => r.TenantId == tenantId && r.Name == role, cancellationToken)
            .ConfigureAwait(false);
        if (roleRow is null)
        {
            return InviteOutcome.Failure(
                MembershipOperationStatus.ValidationFailed,
                $"workspace {tenantId} has no seeded '{role}' role; every workspace is expected " +
                "to be created via WorkspaceFactory.CreateWorkspaceWithDefaultRoles.");
        }

        var user = await db.WorkspaceUsers
            .SingleOrDefaultAsync(u => u.TenantId == tenantId && u.Email == normalizedEmail, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            var createResult = WorkspaceMembershipFactory.CreateInvitedUser(tenantId, normalizedEmail, now);
            if (createResult.IsFailure)
            {
                return InviteOutcome.Failure(MembershipOperationStatus.ValidationFailed, createResult.Error);
            }

            user = createResult.Value;
            db.WorkspaceUsers.Add(user);
        }

        var alreadyMember = await db.WorkspaceMemberships
            .AnyAsync(m => m.WorkspaceUserId == user.Id && m.WorkspaceRoleId == roleRow.Id, cancellationToken)
            .ConfigureAwait(false);
        if (alreadyMember)
        {
            return InviteOutcome.Failure(
                MembershipOperationStatus.Conflict, $"{normalizedEmail} already holds the {role} role in this workspace.");
        }

        var alreadyInvited = await db.WorkspaceInvitations
            .AnyAsync(
                i => i.TenantId == tenantId && i.Email == normalizedEmail && i.AcceptedAt == null && i.RevokedAt == null,
                cancellationToken)
            .ConfigureAwait(false);
        if (alreadyInvited)
        {
            return InviteOutcome.Failure(
                MembershipOperationStatus.Conflict, $"{normalizedEmail} already has a pending invitation to this workspace.");
        }

        var invitation = new WorkspaceInvitation
        {
            TenantId = tenantId,
            // user.Email, not normalizedEmail: when `user` already existed (a prior invite/sign-in),
            // its own stored casing is the value ListMembersAsync's exact-equality join must match —
            // see this method's own "normalised once" comment above for why the two can never
            // disagree for a user this method itself just created, and this covers the pre-existing
            // case too.
            Email = user.Email,
            WorkspaceRoleId = roleRow.Id,
            TokenHash = tokenHash,
            InvitedBy = invitedBy,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        };
        db.WorkspaceInvitations.Add(invitation);

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // ADR-025 implication 9 / task coding objective point 2: the partial unique index is the
            // real backstop for the two AnyAsync pre-checks above (a second, concurrent invite for
            // the same email can slip past both reads before either write commits) — translate the
            // violation to the identical clean Conflict, never let it surface as a 500.
            return InviteOutcome.Failure(
                MembershipOperationStatus.Conflict, $"{normalizedEmail} already has a pending invitation to this workspace.");
        }

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId, invitedBy, "workspace.invitation.issued", "WorkspaceInvitation",
                invitation.Id.Value.ToString(), now, $"email={normalizedEmail}; role={role}"),
            cancellationToken).ConfigureAwait(false);

        return InviteOutcome.Success(invitation);
    }

    /// <summary>
    /// Links a first sign-in (<see cref="WorkspaceUser.LinkExternalSubject"/>) or resolves a
    /// repeat one. See <see cref="WorkspaceSignIn.ResolveSignedInUser"/> for the decision itself.
    /// Does not (re-)assign a role: role assignment happens at invite time
    /// (<see cref="InviteAsync"/>/<see cref="InviteFromOidcClaimsAsync"/>) or at
    /// <see cref="AcceptInvitationAsync"/>; continuously re-syncing role claims on every sign-in is
    /// a deliberately separate concern left to a future task rather than guessed at here.
    /// <see cref="AcceptInvitationAsync"/> is this method's first real production caller
    /// (task E15/F01/US01/T01) — every prior caller was a test.
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
    /// The accept-time grant (task E15/F01/US01/T01, wave w14; ADR-025 Rule D.3c): binds the
    /// accepting identity's external subject onto the <see cref="WorkspaceUser"/> row the invite
    /// already wrote (via the unchanged <see cref="LinkSignInAsync"/>), inserts the membership at
    /// the invited role, and stamps <paramref name="invitation"/>'s <see cref="WorkspaceInvitation.AcceptedAt"/>
    /// — all in <b>one database transaction</b>, so partial acceptance is unreachable. The caller
    /// (<see cref="WorkspaceInvitationService.AcceptAsync"/>) has already entered
    /// <see cref="ITenantContext.BeginScope"/> for <paramref name="tenantId"/>, matched the token
    /// hash (ADR-025 Rule C5) and verified the email match (Rule D.3b); this method assumes all
    /// three already hold and re-validates only what a database round-trip legitimately can — the
    /// unique-membership-index race (Rule D.3d).
    ///
    /// <para>
    /// An explicit <see cref="Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction"/> is
    /// required (not merely one call to <see cref="DbContext.SaveChangesAsync"/>, EF Core's usual
    /// implicit-transaction shape) because <see cref="LinkSignInAsync"/> — deliberately
    /// <b>unchanged</b> — already calls <c>SaveChangesAsync</c> itself; without an outer transaction
    /// wrapping both calls, a failure after the link commits but before the membership insert would
    /// leave the subject bound with no membership, silently reachable on retry as if nothing had
    /// happened yet.
    /// </para>
    /// </summary>
    public async Task<AcceptOutcome> AcceptInvitationAsync(
        TenantId tenantId,
        WorkspaceInvitation invitation,
        string signedInIdentity,
        CancellationToken cancellationToken = default)
    {
        var roleRow = await db.WorkspaceRoles
            .SingleOrDefaultAsync(r => r.TenantId == tenantId && r.Id == invitation.WorkspaceRoleId, cancellationToken)
            .ConfigureAwait(false);
        if (roleRow is null)
        {
            return AcceptOutcome.Failure(MembershipOperationStatus.NotFound, "the invited role no longer exists.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        var linkResult = await LinkSignInAsync(tenantId, signedInIdentity, invitation.Email, cancellationToken)
            .ConfigureAwait(false);
        if (linkResult.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return AcceptOutcome.Failure(MembershipOperationStatus.ValidationFailed, linkResult.Error);
        }

        var user = linkResult.Value;
        var now = clock.UtcNow;
        var membershipResult = WorkspaceMembershipFactory.CreateMembership(user, roleRow, now);
        if (membershipResult.IsFailure)
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return AcceptOutcome.Failure(MembershipOperationStatus.ValidationFailed, membershipResult.Error);
        }

        db.WorkspaceMemberships.Add(membershipResult.Value);
        invitation.AcceptedAt = now;

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            // Rule D.3d: two concurrent accepts race this same insert against
            // ix_workspace_membership_workspace_user_id_workspace_role_id (identity-workspace.sql:90)
            // — already schema-enforced; this is the translation to a clean 409, never a 500.
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            return AcceptOutcome.Failure(MembershipOperationStatus.Conflict, "this invitation has already been accepted.");
        }

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId, signedInIdentity, "workspace.membership.granted", "WorkspaceMembership",
                membershipResult.Value.Id.Value.ToString(), now, $"role={roleRow.Name}"),
            cancellationToken).ConfigureAwait(false);
        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId, signedInIdentity, "workspace.invitation.accepted", "WorkspaceInvitation",
                invitation.Id.Value.ToString(), now),
            cancellationToken).ConfigureAwait(false);

        return AcceptOutcome.Success(user, membershipResult.Value, roleRow.Name);
    }

    /// <summary>
    /// Removal (task E15/F01/US01/T01, wave w14; ADR-025 Rule D.5a-c, §H T7/T9): deletes
    /// <paramref name="membershipId"/> — never the user, ADR-025 Rule D.5b, the FK is
    /// <c>ON DELETE CASCADE</c> from user to membership
    /// (<c>WorkspaceMembershipConfiguration.cs:30-33</c>), so deleting the user here would cascade —
    /// and revokes <paramref name="membershipId"/>'s own user's live invitations in the <b>same</b>
    /// <see cref="DbContext.SaveChangesAsync"/> call (one implicit transaction; no explicit one is
    /// needed here, unlike <see cref="AcceptInvitationAsync"/>, because every write below is a
    /// tracked-entity change flushed by a single <c>SaveChangesAsync</c>, not two independent calls).
    /// Without that same-transaction revoke, a still-valid link would re-admit the removed person
    /// with no new invite (breaking AC-8's own "re-adding needs a new invite").
    ///
    /// <para>
    /// The last-Admin guard (Rule D.5a) is <see cref="WorkspaceMembershipRemoval.CanRemove"/> — a
    /// pure function over the target's role and a live count this method queries; this method's own
    /// job is just supplying those two facts and translating a refusal to
    /// <see cref="MembershipOperationStatus.Conflict"/> (409, "well-formed and authorized, but
    /// violates a tenant invariant", ADR-025 §B).
    /// </para>
    /// </summary>
    public async Task<RemoveOutcome> RemoveMemberAsync(
        TenantId tenantId, EntityId membershipId, string removedBy, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);
        var now = clock.UtcNow;

        var membership = await db.WorkspaceMemberships
            .SingleOrDefaultAsync(m => m.TenantId == tenantId && m.Id == membershipId, cancellationToken)
            .ConfigureAwait(false);
        if (membership is null)
        {
            return RemoveOutcome.Failure(MembershipOperationStatus.NotFound);
        }

        var role = await db.WorkspaceRoles
            .SingleOrDefaultAsync(r => r.TenantId == tenantId && r.Id == membership.WorkspaceRoleId, cancellationToken)
            .ConfigureAwait(false);
        if (role is null)
        {
            return RemoveOutcome.Failure(MembershipOperationStatus.NotFound);
        }

        if (role.Name == WorkspaceRoleName.Admin)
        {
            var liveAdminMembershipCount = await (
                from candidateMembership in db.WorkspaceMemberships
                join candidateRole in db.WorkspaceRoles on candidateMembership.WorkspaceRoleId equals candidateRole.Id
                where candidateMembership.TenantId == tenantId
                    && candidateRole.TenantId == tenantId
                    && candidateRole.Name == WorkspaceRoleName.Admin
                select candidateMembership.Id)
                .CountAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!WorkspaceMembershipRemoval.CanRemove(role.Name, liveAdminMembershipCount))
            {
                return RemoveOutcome.Failure(
                    MembershipOperationStatus.Conflict,
                    "cannot remove the last Admin of this workspace; at least one must remain.");
            }
        }

        var user = await db.WorkspaceUsers
            .SingleOrDefaultAsync(u => u.TenantId == tenantId && u.Id == membership.WorkspaceUserId, cancellationToken)
            .ConfigureAwait(false);

        db.WorkspaceMemberships.Remove(membership);

        // Rule D.5c: revoke that email's unaccepted, unrevoked invitations in this tenant, in the
        // same SaveChangesAsync as the membership removal above.
        var staleInvitations = user is null
            ? []
            : await db.WorkspaceInvitations
                .Where(i => i.TenantId == tenantId && i.Email == user.Email && i.AcceptedAt == null && i.RevokedAt == null)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

        foreach (var invitation in staleInvitations)
        {
            invitation.RevokedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId, removedBy, "workspace.membership.removed", "WorkspaceMembership",
                membershipId.Value.ToString(), now, $"role={role.Name}"),
            cancellationToken).ConfigureAwait(false);

        foreach (var invitation in staleInvitations)
        {
            await auditWriter.WriteAsync(
                new AuditEntry(
                    tenantId, removedBy, "workspace.invitation.revoked", "WorkspaceInvitation",
                    invitation.Id.Value.ToString(), now, "revoked by member removal"),
                cancellationToken).ConfigureAwait(false);
        }

        return RemoveOutcome.Success();
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
    /// Correct both before and after task E15/F01/US01/T01's redesign: this method reads both
    /// tables and derives, and does not care which write path produced either row.
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
            select new MembershipRosterRow(user.Id, membership.Id, user.Email, user.DisplayName, role.Name))
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
                user.Id, invitation.Id, invitation.Email, user.DisplayName, role.Name,
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
                // The exposed membership id must belong to the SAME row the resolved role came
                // from: a person holding two memberships (e.g. Admin + Procurement) must not have
                // an arbitrary one's id displayed under the highest-role label, or DELETE would
                // remove the wrong membership.
                var highestRoleRow = group.FirstOrDefault(row => row.RoleName == highestRole) ?? first;
                return new WorkspaceMemberRecord(
                    first.UserId, first.Email, first.DisplayName, highestRole, WorkspaceMemberStatus.Active,
                    MembershipId: highestRoleRow.MembershipId, InvitationId: null);
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
                row.UserId, row.Email, row.DisplayName, row.RoleName, WorkspaceMemberStatus.Invited,
                MembershipId: null, InvitationId: row.InvitationId));

        return active
            .Concat(invited)
            .OrderBy(member => member.Email, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// <see langword="true"/> when <paramref name="exception"/> wraps a Postgres unique-violation
    /// (SQLSTATE 23505) — the shared translation-to-409 backstop <see cref="InviteAsync"/> and
    /// <see cref="AcceptInvitationAsync"/> both need (ADR-025 §D.1d / Rule D.3d: "the task asserts
    /// this rather than re-implementing it, and must translate the violation to 409, not 500"). The
    /// EF Core InMemory provider (used by some of this solution's own host-level tests) never
    /// throws this shape at all, since it does not enforce unique indexes the way Postgres does —
    /// harmless here, since those tests never exercise the race this guards.
    /// </summary>
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}

/// <summary>
/// Shared outcome status for the write operations <see cref="WorkspaceMembershipService"/> and
/// <see cref="WorkspaceInvitationService"/> perform on behalf of the invitation lifecycle endpoints
/// (ADR-025 §B): the endpoint maps each status to its own HTTP code, the same "small reason type,
/// no behavior" shape <see cref="Domain.WorkspaceAuthorizationFailure"/> already establishes
/// alongside <see cref="Domain.WorkspacePrincipalAuthorization.TryAuthorize"/>.
/// </summary>
public enum MembershipOperationStatus
{
    Success,
    ValidationFailed,
    Conflict,
    NotFound,
    Forbidden,
    Expired,
}

/// <summary>Outcome of <see cref="WorkspaceMembershipService.InviteAsync"/> — see
/// <see cref="MembershipOperationStatus"/>'s own doc comment for why this is not a bare
/// <see cref="Result{T}"/>.</summary>
public sealed class InviteOutcome
{
    private InviteOutcome(MembershipOperationStatus status, WorkspaceInvitation? invitation, string? error)
    {
        Status = status;
        Invitation = invitation;
        Error = error;
    }

    public MembershipOperationStatus Status { get; }

    public bool IsSuccess => Status == MembershipOperationStatus.Success;

    public bool IsFailure => !IsSuccess;

    public WorkspaceInvitation? Invitation { get; }

    public string? Error { get; }

    public static InviteOutcome Success(WorkspaceInvitation invitation) =>
        new(MembershipOperationStatus.Success, invitation, null);

    public static InviteOutcome Failure(MembershipOperationStatus status, string? error) =>
        new(status, null, error);
}

/// <summary>Outcome of <see cref="WorkspaceMembershipService.AcceptInvitationAsync"/>.</summary>
public sealed class AcceptOutcome
{
    private AcceptOutcome(
        MembershipOperationStatus status, WorkspaceUser? user, WorkspaceMembership? membership,
        WorkspaceRoleName? role, string? error)
    {
        Status = status;
        User = user;
        Membership = membership;
        Role = role;
        Error = error;
    }

    public MembershipOperationStatus Status { get; }

    public bool IsSuccess => Status == MembershipOperationStatus.Success;

    public bool IsFailure => !IsSuccess;

    public WorkspaceUser? User { get; }

    public WorkspaceMembership? Membership { get; }

    public WorkspaceRoleName? Role { get; }

    public string? Error { get; }

    public static AcceptOutcome Success(WorkspaceUser user, WorkspaceMembership membership, WorkspaceRoleName role) =>
        new(MembershipOperationStatus.Success, user, membership, role, null);

    public static AcceptOutcome Failure(MembershipOperationStatus status, string? error) =>
        new(status, null, null, null, error);
}

/// <summary>Outcome of <see cref="WorkspaceMembershipService.RemoveMemberAsync"/>.</summary>
public sealed class RemoveOutcome
{
    private RemoveOutcome(MembershipOperationStatus status, string? error)
    {
        Status = status;
        Error = error;
    }

    public MembershipOperationStatus Status { get; }

    public bool IsSuccess => Status == MembershipOperationStatus.Success;

    public bool IsFailure => !IsSuccess;

    public string? Error { get; }

    public static RemoveOutcome Success() => new(MembershipOperationStatus.Success, null);

    public static RemoveOutcome Failure(MembershipOperationStatus status, string? error = null) =>
        new(status, error);
}

/// <summary>One roster row (ADR-026 §D3): the wire shape is built from this at the host boundary
/// (<see cref="Raffa.Api.WorkspaceMembersEndpointExtensions"/>), never returned as-is — <see cref="Id"/>
/// is <see cref="WorkspaceUser"/>'s own id, stable across the Active/Invited transition an accept
/// performs, since both branches join back to the same user row (never the membership or
/// invitation row's own id, which would change on removal/re-invite).
///
/// <para>
/// <see cref="MembershipId"/> (set only when <see cref="Status"/> is
/// <see cref="WorkspaceMemberStatus.Active"/>) and <see cref="InvitationId"/> (set only when
/// <see cref="WorkspaceMemberStatus.Invited"/>) are the action ids the client actually needs:
/// <c>DELETE /members/{membershipId}</c> and <c>revoke /invitations/{invitationId}</c> each take a
/// different row's own id, never <see cref="Id"/> (E15/F02/US01/T01's halt — <see cref="Id"/> alone
/// cannot address either action).
/// </para></summary>
public sealed record WorkspaceMemberRecord(
    EntityId Id, string Email, string? Name, WorkspaceRoleName Role, WorkspaceMemberStatus Status,
    EntityId? MembershipId, EntityId? InvitationId);

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
public sealed record MembershipRosterRow(EntityId UserId, EntityId MembershipId, string Email, string? DisplayName, WorkspaceRoleName RoleName);

/// <summary>A flat, already-tenant-scoped <see cref="WorkspaceInvitation"/> row, joined the same
/// way as <see cref="MembershipRosterRow"/>, carrying the three columns
/// <see cref="WorkspaceMembershipService.ComposeRoster"/> needs to decide liveness
/// (<see cref="AcceptedAt"/>/<see cref="RevokedAt"/> null, <see cref="ExpiresAt"/> in the future).</summary>
public sealed record InvitationRosterRow(
    EntityId UserId,
    EntityId InvitationId,
    string Email,
    string? DisplayName,
    WorkspaceRoleName RoleName,
    DateTimeOffset? AcceptedAt,
    DateTimeOffset? RevokedAt,
    DateTimeOffset ExpiresAt);
