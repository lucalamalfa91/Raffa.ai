using Raffa.Identity.Workspace.Domain;

namespace Raffa.Identity.Workspace.Tests;

/// <summary>
/// T9 (task E15/F01/US01/T01, wave w14; ADR-025 Rule D.5a, §H): the last-Admin guard, proven
/// without a database — <see cref="WorkspaceMembershipRemoval.CanRemove"/> takes only the target
/// membership's role and a live-Admin count, both of which
/// <c>WorkspaceMembershipService.RemoveMemberAsync</c> queries before calling this pure function.
/// </summary>
public sealed class WorkspaceMembershipRemovalTests
{
    [Fact]
    public void Sole_admin_removing_self_is_refused()
    {
        // The count includes the Admin membership about to be removed -- "sole Admin" means this
        // count is 1.
        Assert.False(WorkspaceMembershipRemoval.CanRemove(WorkspaceRoleName.Admin, liveAdminMembershipCount: 1));
    }

    [Fact]
    public void Sole_admin_removing_the_only_other_admin_is_refused()
    {
        // Same fact from the guard's point of view as removing self: only one live Admin
        // membership exists, and the one about to be removed is it.
        Assert.False(WorkspaceMembershipRemoval.CanRemove(WorkspaceRoleName.Admin, liveAdminMembershipCount: 1));
    }

    [Fact]
    public void With_two_admins_either_removal_is_allowed()
    {
        Assert.True(WorkspaceMembershipRemoval.CanRemove(WorkspaceRoleName.Admin, liveAdminMembershipCount: 2));
    }

    [Theory]
    [InlineData(WorkspaceRoleName.Procurement)]
    [InlineData(WorkspaceRoleName.Legal)]
    [InlineData(WorkspaceRoleName.Finance)]
    [InlineData(WorkspaceRoleName.ReadOnly)]
    public void A_non_admin_membership_may_always_be_removed_regardless_of_admin_count(WorkspaceRoleName role)
    {
        // The invariant only constrains Admin -- removing any other role never touches it, even
        // with a live-Admin count of zero (a defensively-impossible state this function does not
        // need to police; it only answers the one question it is asked).
        Assert.True(WorkspaceMembershipRemoval.CanRemove(role, liveAdminMembershipCount: 0));
        Assert.True(WorkspaceMembershipRemoval.CanRemove(role, liveAdminMembershipCount: 1));
    }
}
