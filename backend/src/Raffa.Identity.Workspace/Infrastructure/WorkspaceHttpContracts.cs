namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>
/// Request body for `POST /api/workspaces` (task E01/F09/US01/T01, r0-integration AC-1 "create
/// workspace" step). A public HTTP JSON contract — deliberately living next to
/// <see cref="WorkspaceProvisioningService"/> rather than in <c>Raffa.Api</c>, the same reason
/// <c>Raffa.Audit.Infrastructure.AuditQueryService</c>'s own <c>AuditEventRecord</c> lives next
/// to its service instead of in the host: <c>Raffa.ArchitectureTests.DependencyDirectionTests
/// .Host_must_not_contain_domain_types</c> only inspects the <c>Raffa.Api</c>/<c>Raffa.Worker</c>
/// assemblies, so a request/response shape that lived there as a public type would be flagged as
/// business logic leaking into a host that must stay a thin composition root.
/// </summary>
public sealed record CreateWorkspaceRequest(string Name);

/// <summary>
/// Request body for `POST /api/workspaces/{tenantId}/invites` (r0-integration AC-1 "invite"
/// step). <see cref="Role"/> is the raw claim-shaped string
/// <see cref="Domain.WorkspaceRoleClaimResolver"/> already knows how to resolve (bare enum name,
/// product-spec label, or a `Raffa.`-namespaced app-role value) — reusing that resolver here
/// keeps exactly one place that knows the accepted spellings, the same one ADR-010's eventual JWT
/// role claim will also go through. See <see cref="CreateWorkspaceRequest"/>'s sibling doc comment
/// for why this type lives here and not in Raffa.Api.
/// </summary>
public sealed record InviteRequest(string Email, string Role);
