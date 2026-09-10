namespace Raffa.Identity.Workspace.Domain;

/// <summary>
/// One row per <see cref="WorkspaceRoleName"/> per <see cref="Workspace"/> (ADR-009: every
/// business table — including the role catalog itself — carries `tenant_id` and is RLS-guarded;
/// story us-01-workspace-roles AC-1). Seeded by
/// <see cref="WorkspaceFactory.CreateWorkspaceWithDefaultRoles"/> when a workspace is created, so
/// a <see cref="WorkspaceMembership"/> always has a same-tenant role row to point at.
/// </summary>
public sealed class WorkspaceRole : TenantScopedEntity
{
    public required WorkspaceRoleName Name { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
