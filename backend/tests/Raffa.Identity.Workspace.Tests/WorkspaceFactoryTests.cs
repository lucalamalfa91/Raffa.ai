using Raffa.Identity.Workspace.Domain;
using Raffa.SharedKernel;

namespace Raffa.Identity.Workspace.Tests;

/// <summary>
/// Pure domain-logic proof for task E01/F05/US01/T01 (story us-01-workspace-roles, AC-1) that
/// needs no database: <see cref="WorkspaceFactory.CreateWorkspaceWithDefaultRoles"/> is what
/// guarantees a <see cref="WorkspaceTenant"/> row's `Id == TenantId` invariant and that every one
/// of the five <see cref="WorkspaceRoleName"/> values exists, in the same tenant, from the moment
/// a workspace is created.
/// </summary>
public sealed class WorkspaceFactoryTests
{
    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }

    [Fact]
    public void Creates_a_workspace_whose_tenant_id_equals_its_own_id()
    {
        var (workspace, _) = WorkspaceFactory.CreateWorkspaceWithDefaultRoles(
            "Acme Procurement", new FixedClock(DateTimeOffset.UtcNow));

        // ADR-009 / WorkspaceTenant's own invariant: the workspace IS the tenant boundary.
        Assert.Equal(workspace.Id.Value, workspace.TenantId.Value);
    }

    [Fact]
    public void Seeds_exactly_one_role_per_role_name_in_the_workspaces_own_tenant()
    {
        var (workspace, roles) = WorkspaceFactory.CreateWorkspaceWithDefaultRoles(
            "Acme Procurement", new FixedClock(DateTimeOffset.UtcNow));

        var expectedNames = Enum.GetValues<WorkspaceRoleName>();

        Assert.Equal(expectedNames.Length, roles.Count);
        Assert.Equal(expectedNames.OrderBy(n => n), roles.Select(r => r.Name).OrderBy(n => n));
        Assert.All(roles, role => Assert.Equal(workspace.TenantId, role.TenantId));
    }

    [Fact]
    public void Stamps_the_workspace_and_every_role_with_the_same_created_at_from_the_clock()
    {
        var now = new DateTimeOffset(2026, 9, 3, 12, 0, 0, TimeSpan.Zero);

        var (workspace, roles) = WorkspaceFactory.CreateWorkspaceWithDefaultRoles(
            "Acme Procurement", new FixedClock(now));

        Assert.Equal(now, workspace.CreatedAt);
        Assert.All(roles, role => Assert.Equal(now, role.CreatedAt));
    }
}
