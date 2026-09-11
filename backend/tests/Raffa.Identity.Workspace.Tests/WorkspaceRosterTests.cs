using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;

namespace Raffa.Identity.Workspace.Tests;

/// <summary>
/// Pure unit proof for task E14/F04/US01/T01 (story us-01-members-api; ADR-026 §D3, ADR-025 §D.4)
/// of <see cref="WorkspaceMembershipService.ComposeRoster"/> — no database, mirroring how
/// <c>WorkspaceMembershipFactoryTests</c> proves that factory's own structural invariants. The
/// EF-query half (<see cref="WorkspaceMembershipService.ListMembersAsync"/> itself — the join
/// shape, and that it never has a chance to anchor on <c>workspace_user</c> in the first place) is
/// proven over a real (in-memory) host in
/// <c>Raffa.Api.Tests.WorkspaceMembersEndpointTests</c>, including the removed-member case: a
/// stray <see cref="WorkspaceUser"/> row with no membership and no invitation is exactly what a
/// removed member leaves behind (audit continuity — <see cref="WorkspaceSignIn"/> needs the row),
/// and <see cref="WorkspaceMembershipService.ComposeRoster"/>'s own signature below is the other
/// half of that proof: it does not accept a <see cref="WorkspaceUser"/> collection at all, so a
/// removed member's leftover row has no path into its output regardless of what is in the database
/// — only <see cref="MembershipRosterRow"/> and <see cref="InvitationRosterRow"/> ever reach it.
/// </summary>
public sealed class WorkspaceRosterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_live_membership_renders_active()
    {
        var alice = MembershipRow("alice@acme.example", "Alice Admin", WorkspaceRoleName.Admin);

        var roster = WorkspaceMembershipService.ComposeRoster([alice], [], Now);

        var member = Assert.Single(roster);
        Assert.Equal(alice.UserId, member.Id);
        Assert.Equal("alice@acme.example", member.Email);
        Assert.Equal("Alice Admin", member.Name);
        Assert.Equal(WorkspaceRoleName.Admin, member.Role);
        Assert.Equal(WorkspaceMemberStatus.Active, member.Status);
    }

    [Fact]
    public void A_live_invitation_with_no_membership_renders_invited()
    {
        var invite = InvitationRow("new.hire@acme.example", WorkspaceRoleName.Procurement, expiresAt: Now.AddDays(7));

        var roster = WorkspaceMembershipService.ComposeRoster([], [invite], Now);

        var member = Assert.Single(roster);
        Assert.Equal("new.hire@acme.example", member.Email);
        Assert.Equal(WorkspaceRoleName.Procurement, member.Role);
        Assert.Equal(WorkspaceMemberStatus.Invited, member.Status);
    }

    [Fact]
    public void A_removed_member_who_has_neither_a_membership_nor_a_live_invitation_row_is_absent()
    {
        // "bob@acme.example" was once a member: their WorkspaceUser row still exists in the real
        // database (audit continuity), but the DELETE that removed them took their membership row
        // with it (ADR-025 Rule D.5a) and they were never re-invited. ComposeRoster's signature
        // takes only membership/invitation rows -- there is no WorkspaceUser collection to even
        // construct a stray "removed but still Active" row from, which is the whole point: only
        // alice's own live membership can produce output.
        var alice = MembershipRow("alice@acme.example", "Alice Admin", WorkspaceRoleName.Admin);

        var roster = WorkspaceMembershipService.ComposeRoster([alice], [], Now);

        var member = Assert.Single(roster);
        Assert.Equal("alice@acme.example", member.Email);
        Assert.DoesNotContain(roster, m => m.Email == "bob@acme.example");
    }

    [Theory]
    [InlineData(true, false, false)] // accepted
    [InlineData(false, true, false)] // revoked
    [InlineData(false, false, true)] // expired
    public void An_accepted_revoked_or_expired_invitation_does_not_render_invited(
        bool accepted, bool revoked, bool expired)
    {
        var invite = InvitationRow(
            "gone@acme.example",
            WorkspaceRoleName.Legal,
            expiresAt: expired ? Now.AddDays(-1) : Now.AddDays(7),
            acceptedAt: accepted ? Now.AddDays(-1) : null,
            revokedAt: revoked ? Now.AddDays(-1) : null);

        var roster = WorkspaceMembershipService.ComposeRoster([], [invite], Now);

        Assert.Empty(roster);
    }

    [Fact]
    public void Two_memberships_for_the_same_person_render_once_at_the_highest_role()
    {
        var userId = EntityId.New();
        var procurement = MembershipRow(userId, "buyer@acme.example", null, WorkspaceRoleName.Procurement);
        var admin = MembershipRow(userId, "buyer@acme.example", null, WorkspaceRoleName.Admin);

        var roster = WorkspaceMembershipService.ComposeRoster([procurement, admin], [], Now);

        var member = Assert.Single(roster);
        Assert.Equal(WorkspaceRoleName.Admin, member.Role);
    }

    [Fact]
    public void Precedence_is_not_simply_admin_first_it_is_the_full_ordering()
    {
        // Neither role is Admin -- proves this reuses WorkspaceRoleClaimResolver's own precedence
        // (Admin, Procurement, Legal, Finance, ReadOnly) rather than a shortcut like "is one of
        // them Admin?".
        var userId = EntityId.New();
        var finance = MembershipRow(userId, "reviewer@acme.example", null, WorkspaceRoleName.Finance);
        var legal = MembershipRow(userId, "reviewer@acme.example", null, WorkspaceRoleName.Legal);

        var roster = WorkspaceMembershipService.ComposeRoster([finance, legal], [], Now);

        var member = Assert.Single(roster);
        Assert.Equal(WorkspaceRoleName.Legal, member.Role);
    }

    [Fact]
    public void A_live_invitation_for_someone_who_already_holds_a_membership_is_not_also_listed_as_invited()
    {
        // Not the every-day shape (today's InviteAsync still writes a membership immediately,
        // ADR-025 §D.1 phase 3 note), but ListMembersAsync's own doc comment requires ComposeRoster
        // to be correct regardless of which write path produced the rows it is handed.
        var member = MembershipRow("both@acme.example", "Both", WorkspaceRoleName.Procurement);
        var invite = InvitationRow("both@acme.example", WorkspaceRoleName.Admin, expiresAt: Now.AddDays(7));

        var roster = WorkspaceMembershipService.ComposeRoster([member], [invite], Now);

        var only = Assert.Single(roster);
        Assert.Equal(WorkspaceMemberStatus.Active, only.Status);
        Assert.Equal(WorkspaceRoleName.Procurement, only.Role);
    }

    [Fact]
    public void Name_is_null_unless_a_display_name_is_stored()
    {
        var noName = MembershipRow("plain@acme.example", null, WorkspaceRoleName.ReadOnly);
        var named = MembershipRow("named@acme.example", "Nadia Named", WorkspaceRoleName.ReadOnly);

        var roster = WorkspaceMembershipService.ComposeRoster([noName, named], [], Now);

        Assert.Null(roster.Single(m => m.Email == "plain@acme.example").Name);
        Assert.Equal("Nadia Named", roster.Single(m => m.Email == "named@acme.example").Name);
    }

    [Fact]
    public void Invited_row_name_also_maps_to_the_stored_display_name_never_derived_from_the_email()
    {
        // A re-invited, previously-removed person can already carry a stored DisplayName from
        // before their WorkspaceUser row was created the first time (that row survives removal).
        var invite = InvitationRow(
            "returning@acme.example", WorkspaceRoleName.Finance, expiresAt: Now.AddDays(7), displayName: "Rita Returning");

        var roster = WorkspaceMembershipService.ComposeRoster([], [invite], Now);

        Assert.Equal("Rita Returning", Assert.Single(roster).Name);
    }

    private static MembershipRosterRow MembershipRow(
        string email, string? displayName, WorkspaceRoleName role) =>
        MembershipRow(EntityId.New(), email, displayName, role);

    private static MembershipRosterRow MembershipRow(
        EntityId userId, string email, string? displayName, WorkspaceRoleName role) =>
        new(userId, email, displayName, role);

    private static InvitationRosterRow InvitationRow(
        string email,
        WorkspaceRoleName role,
        DateTimeOffset expiresAt,
        DateTimeOffset? acceptedAt = null,
        DateTimeOffset? revokedAt = null,
        string? displayName = null) =>
        new(EntityId.New(), email, displayName, role, acceptedAt, revokedAt, expiresAt);
}
