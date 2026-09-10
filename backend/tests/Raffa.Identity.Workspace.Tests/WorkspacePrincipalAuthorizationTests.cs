using System.Security.Claims;
using Raffa.Identity.Workspace.Domain;

namespace Raffa.Identity.Workspace.Tests;

/// <summary>
/// Pure unit proof for task E01/F06/US02/T02 (story us-02-audit-baseline AC-2: "GET /api/audit
/// returns authorized, tenant-scoped events"). No database — exercises
/// <see cref="WorkspacePrincipalAuthorization.TryAuthorize"/> directly against hand-built
/// <see cref="ClaimsPrincipal"/> instances, exactly like <c>WorkspaceRoleClaimResolverTests</c>
/// proves the role-resolution half it builds on.
/// </summary>
public sealed class WorkspacePrincipalAuthorizationTests
{
    private static readonly Guid TenantGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static ClaimsPrincipal BuildPrincipal(
        bool authenticated, string? tenantClaimValue, params string[] roleClaimValues)
    {
        var identity = authenticated ? new ClaimsIdentity(authenticationType: "Test") : new ClaimsIdentity();

        if (tenantClaimValue is not null)
        {
            identity.AddClaim(new Claim(WorkspacePrincipalAuthorization.TenantIdClaimType, tenantClaimValue));
        }

        foreach (var roleClaimValue in roleClaimValues)
        {
            identity.AddClaim(new Claim(ClaimTypes.Role, roleClaimValue));
        }

        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public void Refuses_an_unauthenticated_principal()
    {
        var principal = BuildPrincipal(authenticated: false, TenantGuid.ToString(), "Admin");

        var authorized = WorkspacePrincipalAuthorization.TryAuthorize(
            principal, WorkspaceRoleName.Admin, out _, out var failure);

        Assert.False(authorized);
        Assert.Equal(WorkspaceAuthorizationFailure.Unauthenticated, failure);
    }

    [Fact]
    public void Refuses_a_principal_with_no_identity_at_all()
    {
        var principal = new ClaimsPrincipal();

        var authorized = WorkspacePrincipalAuthorization.TryAuthorize(
            principal, WorkspaceRoleName.Admin, out _, out var failure);

        Assert.False(authorized);
        Assert.Equal(WorkspaceAuthorizationFailure.Unauthenticated, failure);
    }

    [Fact]
    public void Refuses_an_authenticated_principal_with_no_tenant_claim()
    {
        var principal = BuildPrincipal(authenticated: true, tenantClaimValue: null, "Admin");

        var authorized = WorkspacePrincipalAuthorization.TryAuthorize(
            principal, WorkspaceRoleName.Admin, out _, out var failure);

        Assert.False(authorized);
        Assert.Equal(WorkspaceAuthorizationFailure.MissingOrInvalidTenantClaim, failure);
    }

    [Fact]
    public void Refuses_an_authenticated_principal_with_an_unparsable_tenant_claim()
    {
        var principal = BuildPrincipal(authenticated: true, "not-a-guid", "Admin");

        var authorized = WorkspacePrincipalAuthorization.TryAuthorize(
            principal, WorkspaceRoleName.Admin, out _, out var failure);

        Assert.False(authorized);
        Assert.Equal(WorkspaceAuthorizationFailure.MissingOrInvalidTenantClaim, failure);
    }

    [Fact]
    public void Refuses_an_authenticated_principal_with_no_role_claim()
    {
        var principal = BuildPrincipal(authenticated: true, TenantGuid.ToString());

        var authorized = WorkspacePrincipalAuthorization.TryAuthorize(
            principal, WorkspaceRoleName.Admin, out _, out var failure);

        Assert.False(authorized);
        Assert.Equal(WorkspaceAuthorizationFailure.InsufficientRole, failure);
    }

    [Fact]
    public void Refuses_an_authenticated_principal_whose_role_does_not_meet_the_requirement()
    {
        var principal = BuildPrincipal(authenticated: true, TenantGuid.ToString(), "Procurement");

        var authorized = WorkspacePrincipalAuthorization.TryAuthorize(
            principal, WorkspaceRoleName.Admin, out _, out var failure);

        Assert.False(authorized);
        Assert.Equal(WorkspaceAuthorizationFailure.InsufficientRole, failure);
    }

    [Fact]
    public void Authorizes_a_workspace_admin_and_resolves_the_tenant_claim()
    {
        var principal = BuildPrincipal(authenticated: true, TenantGuid.ToString(), "Admin");

        var authorized = WorkspacePrincipalAuthorization.TryAuthorize(
            principal, WorkspaceRoleName.Admin, out var tenantId, out var failure);

        Assert.True(authorized);
        Assert.Equal(WorkspaceAuthorizationFailure.None, failure);
        Assert.Equal(TenantGuid, tenantId.Value);
    }

    [Fact]
    public void Authorizes_via_a_recognized_role_claim_alias()
    {
        // "Workspace Admin" is the product spec §3.1 label form; WorkspaceRoleClaimResolver already
        // folds it to WorkspaceRoleName.Admin. Proves TryAuthorize reuses that resolver rather than
        // doing its own narrower string match.
        var principal = BuildPrincipal(authenticated: true, TenantGuid.ToString(), "Workspace Admin");

        var authorized = WorkspacePrincipalAuthorization.TryAuthorize(
            principal, WorkspaceRoleName.Admin, out _, out var failure);

        Assert.True(authorized);
        Assert.Equal(WorkspaceAuthorizationFailure.None, failure);
    }

    [Fact]
    public void Supports_a_required_role_other_than_admin()
    {
        var principal = BuildPrincipal(authenticated: true, TenantGuid.ToString(), "ReadOnly");

        var authorized = WorkspacePrincipalAuthorization.TryAuthorize(
            principal, WorkspaceRoleName.ReadOnly, out var tenantId, out var failure);

        Assert.True(authorized);
        Assert.Equal(WorkspaceAuthorizationFailure.None, failure);
        Assert.Equal(TenantGuid, tenantId.Value);
    }

    [Fact]
    public void Throws_on_a_null_principal()
    {
        Assert.Throws<ArgumentNullException>(
            () => WorkspacePrincipalAuthorization.TryAuthorize(null!, WorkspaceRoleName.Admin, out _, out _));
    }
}
