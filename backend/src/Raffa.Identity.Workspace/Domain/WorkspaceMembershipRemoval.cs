namespace Raffa.Identity.Workspace.Domain;

/// <summary>
/// The last-Admin guard (task E15/F01/US01/T01, wave w14; ADR-025 Rule D.5a, §H T9): "at least one
/// live Admin membership per tenant, always" — a tenant with zero Admins is unrecoverable without
/// operator intervention. A pure function over already-loaded facts, the same "no query, no save,
/// provable with in-memory objects" shape <see cref="WorkspaceMembershipFactory"/> already
/// establishes for the invite flow: <see cref="Infrastructure.WorkspaceMembershipService.RemoveMemberAsync"/>
/// is the one caller that loads <paramref name="targetRole"/> and counts live Admin memberships,
/// then asks this method the single yes/no question.
///
/// <para>
/// Deliberately indifferent to *who* is removing *whom* — an Admin removing themselves and an Admin
/// removing a different Admin are the same case: the invariant is about how many live Admin
/// memberships the tenant would have left, not about self- vs. other-removal. AC-9 / T9's three
/// scenarios ("sole Admin removes self", "sole Admin removes the only other Admin", "either removal
/// with two Admins") all fall out of this one rule without a special case for either.
/// </para>
/// </summary>
public static class WorkspaceMembershipRemoval
{
    /// <summary>
    /// <see langword="true"/> when removing a membership at <paramref name="targetRole"/> is safe
    /// given <paramref name="liveAdminMembershipCount"/> — the number of live
    /// <see cref="WorkspaceMembership"/> rows at <see cref="WorkspaceRoleName.Admin"/> in this
    /// tenant, <b>including</b> the one about to be removed. Removing a non-Admin membership is
    /// always safe (the invariant only constrains Admin); removing an Admin membership is safe only
    /// when at least one other live Admin membership survives it.
    /// </summary>
    public static bool CanRemove(WorkspaceRoleName targetRole, int liveAdminMembershipCount)
    {
        if (targetRole != WorkspaceRoleName.Admin)
        {
            return true;
        }

        return liveAdminMembershipCount > 1;
    }
}
