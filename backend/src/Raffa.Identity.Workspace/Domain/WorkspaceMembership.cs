using Raffa.SharedKernel;

namespace Raffa.Identity.Workspace.Domain;

/// <summary>
/// Assigns one <see cref="WorkspaceRole"/> to one <see cref="WorkspaceUser"/> (product spec
/// §3.2's "User → Tenant → Role" chain; story us-01-workspace-roles AC-1/AC-3). Both the user and
/// the role referenced are expected to belong to the same tenant as this row itself; that
/// cross-reference invariant is an application-layer concern for the service that creates
/// memberships (task E01/F05/US01/T02's invite flow) to enforce — this task's guarantee is
/// structural (the FK columns exist and are indexed) and RLS-backstopped like every other table
/// here, not a cross-tenant-checking database trigger.
/// </summary>
public sealed class WorkspaceMembership : TenantScopedEntity
{
    public required EntityId WorkspaceUserId { get; set; }

    public required EntityId WorkspaceRoleId { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
