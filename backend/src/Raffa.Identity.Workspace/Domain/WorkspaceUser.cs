using Raffa.SharedKernel;

namespace Raffa.Identity.Workspace.Domain;

/// <summary>
/// A person's profile within one <see cref="Workspace"/> (product spec §3.2's "User"; story
/// us-01-workspace-roles AC-1: "Workspace, User, Role, Membership carry tenant_id and are
/// RLS-guarded"). Named <c>WorkspaceUser</c> rather than the spec's bare "User" for two reasons:
/// `user` is a reserved word in the PostgreSQL grammar — an unquoted `CREATE TABLE user (...)`
/// fails to parse — and this codebase's migrations rely on plain, unquoted, hand-readable
/// lowercase identifiers (see e.g. `Raffa.Documents.Contracts`'s checked-in SQL script), so
/// there is no reason to make table naming depend on whether the EF/Npgsql SQL generator happens
/// to auto-quote a reserved word; every row here is also already scoped to exactly one
/// workspace/tenant, so the qualified name documents that directly too.
///
/// <see cref="ExternalSubjectId"/> is nullable: a row exists once a workspace admin invites
/// someone by email, before that person has ever signed in. Resolving it from the OIDC
/// `sub`/`oid` claim on first sign-in, and the invite flow itself, are task E01/F05/US01/T02's
/// job (ADR-010) — now <see cref="LinkExternalSubject"/> (first-sign-in linking, called by
/// <c>Infrastructure.WorkspaceMembershipService.LinkSignInAsync</c> via
/// <see cref="WorkspaceSignIn.ResolveSignedInUser"/>) and
/// <see cref="WorkspaceMembershipFactory.CreateInvitedUser"/> (the invite itself).
/// </summary>
public sealed class WorkspaceUser : TenantScopedEntity
{
    public required string Email { get; set; }

    public string? DisplayName { get; set; }

    public string? ExternalSubjectId { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Links this row to the OIDC <c>sub</c>/<c>oid</c> claim of the identity that just signed in
    /// (ADR-010; story us-01-workspace-roles AC-3). Idempotent when it is the same subject that is
    /// already linked (a repeat sign-in is a no-op success); fails instead of overwriting when
    /// this row is already linked to a <em>different</em> subject, so one invited email can never
    /// be silently reassigned to another Entra identity.
    /// </summary>
    public Result<WorkspaceUser> LinkExternalSubject(string externalSubjectId)
    {
        if (string.IsNullOrWhiteSpace(externalSubjectId))
        {
            return Result<WorkspaceUser>.Failure("external subject id is required to link a sign-in.");
        }

        if (ExternalSubjectId is not null &&
            !string.Equals(ExternalSubjectId, externalSubjectId, StringComparison.Ordinal))
        {
            return Result<WorkspaceUser>.Failure(
                $"workspace user {Id} is already linked to a different external subject.");
        }

        ExternalSubjectId = externalSubjectId;
        return this;
    }
}
