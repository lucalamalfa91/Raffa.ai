namespace Raffa.Api;

/// <summary>
/// The workspace members route group (ADR-026 implication 6's file split; task E14/F02/US01/T01,
/// wave w14). Deliberately empty of routes in phase 1: `GET /api/workspaces/{tenantId}/members`
/// (the roster, ADR-026 §D3) lands in phase 2 (`E14/F03/US01/T01`) and
/// `DELETE /api/workspaces/{tenantId}/members/{membershipId}` (remove, ADR-025 §D.5) lands in
/// phase 3 (`E15/F01/US01/T01`). This file — and the one call site inside
/// <see cref="WorkspaceEndpointExtensions.MapWorkspaceEndpoints"/> — exist now so neither later
/// task has to add a `Program.cs` line or contend <see cref="WorkspaceEndpointExtensions"/> for a
/// second route group in the same phase (the story's own "council decisions": "split it by route
/// group up front, in this task, keeping `MapWorkspaceEndpoints()` as the single entry point").
/// </summary>
public static class WorkspaceMembersEndpointExtensions
{
    public static IEndpointRouteBuilder MapWorkspaceMemberEndpoints(this IEndpointRouteBuilder endpoints) =>
        endpoints;
}
