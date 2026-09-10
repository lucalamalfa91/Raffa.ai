namespace Raffa.Identity.Workspace.Domain;

/// <summary>
/// The tenant boundary itself (product spec §3.2 "Every business object must carry tenant_id";
/// ADR-009; story us-01-workspace-roles AC-1's "Workspace"). Named <c>WorkspaceTenant</c> rather
/// than the bare "Workspace" because this project's own root namespace is
/// <c>Raffa.Identity.Workspace</c> — a type named exactly <c>Workspace</c> inside it collides
/// with that namespace segment (CS0118 "is a namespace but is used like a type") the moment any
/// other file in this assembly references it unqualified, so every sibling type here
/// (<see cref="WorkspaceUser"/>, <see cref="WorkspaceRole"/>, <see cref="WorkspaceMembership"/>)
/// already uses a compound name for the same structural reason; this one just spells out the
/// concept it stands for instead of leaving it implicit.
///
/// In V1 a workspace *is* a tenant — there is no separate "Tenant" table — so a
/// <see cref="WorkspaceTenant"/> row's own <see cref="TenantScopedEntity.TenantId"/> is always
/// equal to that same row's <see cref="TenantScopedEntity.Id"/>: this table carries `tenant_id`
/// (and is RLS-guarded, AC-1) exactly like every other table here, and every one of those other
/// tables' `tenant_id` values is a logical reference back to one specific workspace row's
/// <see cref="TenantScopedEntity.Id"/> (ADR-009: "FK to workspace/tenant").
///
/// Always construct via <see cref="WorkspaceFactory.CreateWorkspaceWithDefaultRoles"/> rather
/// than the object initializer directly, so the `Id == TenantId` invariant cannot drift.
/// </summary>
public sealed class WorkspaceTenant : TenantScopedEntity
{
    public required string Name { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
