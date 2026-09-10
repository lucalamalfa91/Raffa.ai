using Raffa.Identity.Workspace.Domain;

namespace Raffa.Identity.Workspace.Tests;

/// <summary>
/// Pure unit proof for task E01/F05/US01/T02 (story us-01-workspace-roles, AC-3: "Role assignment
/// resolves from OIDC claims (Admin/Procurement/Legal/Finance/Read-only)"). No database.
/// </summary>
public sealed class WorkspaceRoleClaimResolverTests
{
    [Theory]
    [InlineData("Admin", WorkspaceRoleName.Admin)]
    [InlineData("admin", WorkspaceRoleName.Admin)]
    [InlineData("Workspace Admin", WorkspaceRoleName.Admin)]
    [InlineData("Raffa.Admin", WorkspaceRoleName.Admin)]
    [InlineData("Procurement", WorkspaceRoleName.Procurement)]
    [InlineData("Raffa.Procurement", WorkspaceRoleName.Procurement)]
    [InlineData("Legal", WorkspaceRoleName.Legal)]
    [InlineData("Finance", WorkspaceRoleName.Finance)]
    [InlineData("ReadOnly", WorkspaceRoleName.ReadOnly)]
    [InlineData("Read-only", WorkspaceRoleName.ReadOnly)]
    [InlineData("Read-only / Business", WorkspaceRoleName.ReadOnly)]
    [InlineData("  Admin  ", WorkspaceRoleName.Admin)]
    public void Resolves_every_documented_claim_shape_for_its_role(string claimValue, WorkspaceRoleName expected)
    {
        Assert.True(WorkspaceRoleClaimResolver.TryResolve(claimValue, out var resolved));
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("SuperAdmin")]
    [InlineData("Raffa.Read")] // an ADR-010 *scope*, not a workspace role.
    public void Refuses_an_unrecognized_or_blank_claim_value(string? claimValue)
    {
        Assert.False(WorkspaceRoleClaimResolver.TryResolve(claimValue, out _));
    }

    [Fact]
    public void Picks_the_highest_precedence_role_when_multiple_claims_are_present()
    {
        var claims = new[] { "Finance", "Admin", "ReadOnly" };

        Assert.True(WorkspaceRoleClaimResolver.TryResolve(claims, out var resolved));
        Assert.Equal(WorkspaceRoleName.Admin, resolved);
    }

    [Fact]
    public void Falls_back_to_the_only_recognized_claim_when_others_do_not_match()
    {
        var claims = new[] { "SuperAdmin", "Finance" };

        Assert.True(WorkspaceRoleClaimResolver.TryResolve(claims, out var resolved));
        Assert.Equal(WorkspaceRoleName.Finance, resolved);
    }

    [Fact]
    public void Fails_when_no_claim_in_the_collection_is_recognized()
    {
        Assert.False(WorkspaceRoleClaimResolver.TryResolve(["SuperAdmin", "Owner"], out _));
    }

    [Fact]
    public void Fails_on_an_empty_claim_collection()
    {
        Assert.False(WorkspaceRoleClaimResolver.TryResolve(Array.Empty<string>(), out _));
    }
}
