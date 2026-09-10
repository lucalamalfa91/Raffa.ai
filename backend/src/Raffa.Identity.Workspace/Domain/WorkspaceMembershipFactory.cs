using Raffa.SharedKernel;

namespace Raffa.Identity.Workspace.Domain;

/// <summary>
/// Pure creation/validation logic for the workspace invite flow (task E01/F05/US01/T02; ADR-010;
/// story us-01-workspace-roles AC-3). Mirrors <see cref="WorkspaceFactory"/>'s role as the single
/// place that keeps this module's own structural invariants true no matter which caller (today:
/// <c>Infrastructure.WorkspaceMembershipService</c>; tomorrow: whichever task adds the
/// `/api/workspaces` invite endpoint, AC-2) drives the flow. Every method here takes
/// already-loaded entities and returns a <see cref="Result{T}"/> instead of querying or saving
/// anything itself, so every rule is provable with in-memory objects and no database — the EF/RLS
/// plumbing itself is already proven by `IdentityWorkspaceMigrationTests` and
/// `WorkspaceRlsCrossTenantIsolationTests` (task E01/F05/US01/T01).
/// </summary>
public static class WorkspaceMembershipFactory
{
    /// <summary>
    /// Creates a new <see cref="WorkspaceUser"/> row for someone who has been invited by email but
    /// has never signed in — <see cref="WorkspaceUser.ExternalSubjectId"/> stays
    /// <see langword="null"/> until <see cref="WorkspaceSignIn.ResolveSignedInUser"/> links it on
    /// first sign-in. Email validation here is deliberately minimal (non-empty, contains "@", fits
    /// the column's RFC 5321 length limit) rather than a full RFC 5322 parser: it exists to turn
    /// an obviously-wrong invite into a clean <see cref="Result{T}"/> failure instead of a raw
    /// Postgres length-constraint error at save time, not to be a complete validator.
    /// </summary>
    public static Result<WorkspaceUser> CreateInvitedUser(
        TenantId tenantId, string email, DateTimeOffset now, string? displayName = null)
    {
        var trimmedEmail = email.Trim();
        if (trimmedEmail.Length == 0 || trimmedEmail.Length > 320 || !trimmedEmail.Contains('@'))
        {
            return Result<WorkspaceUser>.Failure($"'{email}' is not a valid email address to invite.");
        }

        return new WorkspaceUser
        {
            TenantId = tenantId,
            Email = trimmedEmail,
            DisplayName = displayName,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Assigns <paramref name="role"/> to <paramref name="user"/>. Enforces the invariant
    /// <see cref="WorkspaceMembership"/>'s own doc comment defers to this task: the user and the
    /// role must already belong to the same tenant as each other (and therefore as the membership
    /// row this creates) — a structural guarantee the FK/index alone cannot make, since both
    /// foreign keys are only ever validated against their own table, never against each other.
    /// </summary>
    public static Result<WorkspaceMembership> CreateMembership(
        WorkspaceUser user, WorkspaceRole role, DateTimeOffset now)
    {
        if (user.TenantId != role.TenantId)
        {
            return Result<WorkspaceMembership>.Failure(
                $"workspace user {user.Id} (tenant {user.TenantId}) and role {role.Name} " +
                $"(tenant {role.TenantId}) belong to different tenants; a membership cannot cross " +
                "the tenant boundary.");
        }

        return new WorkspaceMembership
        {
            TenantId = user.TenantId,
            WorkspaceUserId = user.Id,
            WorkspaceRoleId = role.Id,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// True when <paramref name="user"/> already holds <paramref name="role"/> among
    /// <paramref name="existingMemberships"/> — the same uniqueness
    /// <c>WorkspaceMembershipConfiguration</c>'s `(WorkspaceUserId, WorkspaceRoleId)` index
    /// enforces at the database, checked ahead of the write so the invite flow can return a
    /// meaningful <see cref="Result{T}"/> failure instead of surfacing a raw constraint violation.
    /// </summary>
    public static bool HasMembership(
        IEnumerable<WorkspaceMembership> existingMemberships, WorkspaceUser user, WorkspaceRole role) =>
        existingMemberships.Any(m => m.WorkspaceUserId == user.Id && m.WorkspaceRoleId == role.Id);
}
