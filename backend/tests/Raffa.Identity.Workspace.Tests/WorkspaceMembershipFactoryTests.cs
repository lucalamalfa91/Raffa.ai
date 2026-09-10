using Raffa.Identity.Workspace.Domain;
using Raffa.SharedKernel;

namespace Raffa.Identity.Workspace.Tests;

/// <summary>
/// Pure unit proof for task E01/F05/US01/T02 (story us-01-workspace-roles, AC-3) of the invite
/// flow's structural invariants, with no database — mirrors how <c>WorkspaceFactoryTests</c>
/// proves task E01/F05/US01/T01's own factory.
/// </summary>
public sealed class WorkspaceMembershipFactoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Creates_an_invited_user_with_no_external_subject_yet()
    {
        var tenantId = TenantId.New();

        var result = WorkspaceMembershipFactory.CreateInvitedUser(tenantId, "new.hire@acme.example", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(tenantId, result.Value.TenantId);
        Assert.Equal("new.hire@acme.example", result.Value.Email);
        Assert.Null(result.Value.ExternalSubjectId);
        Assert.Equal(Now, result.Value.CreatedAt);
    }

    [Fact]
    public void Trims_surrounding_whitespace_from_the_invited_email()
    {
        var result = WorkspaceMembershipFactory.CreateInvitedUser(TenantId.New(), "  a@acme.example  ", Now);

        Assert.True(result.IsSuccess);
        Assert.Equal("a@acme.example", result.Value.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-email")]
    public void Refuses_to_invite_an_invalid_email(string email)
    {
        var result = WorkspaceMembershipFactory.CreateInvitedUser(TenantId.New(), email, Now);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Refuses_an_email_longer_than_the_column_limit()
    {
        var tooLong = "a@" + new string('b', 320); // 322 chars, > 320 (RFC 5321 mailbox limit).

        var result = WorkspaceMembershipFactory.CreateInvitedUser(TenantId.New(), tooLong, Now);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Creates_a_membership_for_a_same_tenant_user_and_role()
    {
        var tenantId = TenantId.New();
        var user = new WorkspaceUser { TenantId = tenantId, Email = "a@acme.example", CreatedAt = Now };
        var role = new WorkspaceRole { TenantId = tenantId, Name = WorkspaceRoleName.Procurement, CreatedAt = Now };

        var result = WorkspaceMembershipFactory.CreateMembership(user, role, Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(user.Id, result.Value.WorkspaceUserId);
        Assert.Equal(role.Id, result.Value.WorkspaceRoleId);
        Assert.Equal(tenantId, result.Value.TenantId);
        Assert.Equal(Now, result.Value.CreatedAt);
    }

    [Fact]
    public void Refuses_a_membership_across_two_different_tenants()
    {
        var user = new WorkspaceUser { TenantId = TenantId.New(), Email = "a@acme.example", CreatedAt = Now };
        var role = new WorkspaceRole { TenantId = TenantId.New(), Name = WorkspaceRoleName.Admin, CreatedAt = Now };

        var result = WorkspaceMembershipFactory.CreateMembership(user, role, Now);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void Detects_an_existing_membership_for_the_same_user_and_role()
    {
        var tenantId = TenantId.New();
        var user = new WorkspaceUser { TenantId = tenantId, Email = "a@acme.example", CreatedAt = Now };
        var role = new WorkspaceRole { TenantId = tenantId, Name = WorkspaceRoleName.Legal, CreatedAt = Now };
        var membership = WorkspaceMembershipFactory.CreateMembership(user, role, Now).Value;

        Assert.True(WorkspaceMembershipFactory.HasMembership([membership], user, role));
    }

    [Fact]
    public void Does_not_detect_a_membership_for_a_different_role()
    {
        var tenantId = TenantId.New();
        var user = new WorkspaceUser { TenantId = tenantId, Email = "a@acme.example", CreatedAt = Now };
        var legalRole = new WorkspaceRole { TenantId = tenantId, Name = WorkspaceRoleName.Legal, CreatedAt = Now };
        var financeRole = new WorkspaceRole { TenantId = tenantId, Name = WorkspaceRoleName.Finance, CreatedAt = Now };
        var membership = WorkspaceMembershipFactory.CreateMembership(user, legalRole, Now).Value;

        Assert.False(WorkspaceMembershipFactory.HasMembership([membership], user, financeRole));
    }
}
