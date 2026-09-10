using Raffa.SharedKernel;

namespace Raffa.Identity.Workspace.Domain;

/// <summary>
/// Creates a new <see cref="WorkspaceTenant"/> together with its full
/// <see cref="WorkspaceRoleName"/> catalog (product spec §3.1: Admin/Procurement/Legal/Finance/
/// Read-only exist in every workspace from day one). Centralising this here is what keeps
/// <see cref="WorkspaceTenant"/>'s `Id == TenantId` invariant (see that type's own doc comment)
/// and the seeded role rows sharing that same tenant id both true no matter which caller creates
/// a workspace.
/// </summary>
public static class WorkspaceFactory
{
    public static (WorkspaceTenant Workspace, IReadOnlyList<WorkspaceRole> Roles) CreateWorkspaceWithDefaultRoles(
        string name, IClock clock)
    {
        var id = EntityId.New();
        var tenantId = new TenantId(id.Value);
        var createdAt = clock.UtcNow;

        var workspace = new WorkspaceTenant
        {
            Id = id,
            TenantId = tenantId,
            Name = name,
            CreatedAt = createdAt,
        };

        var roles = Enum.GetValues<WorkspaceRoleName>()
            .Select(roleName => new WorkspaceRole
            {
                TenantId = tenantId,
                Name = roleName,
                CreatedAt = createdAt,
            })
            .ToArray();

        return (workspace, roles);
    }
}
