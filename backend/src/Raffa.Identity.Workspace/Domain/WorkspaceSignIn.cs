using Raffa.SharedKernel;

namespace Raffa.Identity.Workspace.Domain;

/// <summary>
/// Pure decision logic for linking a signed-in Entra ID identity to its
/// <see cref="WorkspaceUser"/> row (task E01/F05/US01/T02; ADR-010). <see cref="WorkspaceUser"/>'s
/// own doc comment names this task as the owner of "resolving [ExternalSubjectId] from the OIDC
/// sub/oid claim on first sign-in". Takes the two lookups the caller already had to do (by
/// external subject, by email) and returns the resulting decision — no query, no save — so the
/// state machine below is provable without a database.
/// </summary>
public static class WorkspaceSignIn
{
    /// <summary>
    /// Resolves the <see cref="WorkspaceUser"/> a sign-in belongs to.
    /// <list type="bullet">
    /// <item>Already linked (<paramref name="existingByExternalSubject"/> is not
    /// <see langword="null"/>): returns it unchanged — every later sign-in is a no-op lookup.</item>
    /// <item>Not yet linked but a same-tenant invite exists for this email
    /// (<paramref name="existingByEmail"/>): links <paramref name="externalSubjectId"/> onto that
    /// row (first sign-in after being invited).</item>
    /// <item>Neither: fails. An unrecognized email in a tenant is not provisioned by signing in —
    /// only an admin's invite (<see cref="WorkspaceMembershipFactory.CreateInvitedUser"/>) creates
    /// a <see cref="WorkspaceUser"/> row; self-service join is out of this task's scope.</item>
    /// </list>
    /// </summary>
    public static Result<WorkspaceUser> ResolveSignedInUser(
        WorkspaceUser? existingByExternalSubject, WorkspaceUser? existingByEmail, string externalSubjectId)
    {
        if (existingByExternalSubject is not null)
        {
            return existingByExternalSubject;
        }

        if (existingByEmail is null)
        {
            return Result<WorkspaceUser>.Failure(
                "no workspace invite was found for this email in this tenant; sign-in cannot " +
                "provision a new workspace user.");
        }

        return existingByEmail.LinkExternalSubject(externalSubjectId);
    }
}
