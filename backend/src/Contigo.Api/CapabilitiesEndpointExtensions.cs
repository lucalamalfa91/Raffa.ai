using Contigo.Chat.Application.Capabilities;
using Contigo.Identity.Workspace.Domain;

namespace Contigo.Api;

/// <summary>
/// Maps `GET /api/capabilities` (R-SYS-01; story us-01-capability-catalog AC-1, task
/// E13/F08/US01/T01). Deliberately **not** called from `Program.cs` by this task — this story's
/// own Tasks row names it "`CapabilitiesEndpointExtensions.cs` (mapped by F06)"; the same
/// "endpoint exists, host wiring is a later task's job" shape
/// `Contigo.Chat.Infrastructure.ServiceCollectionExtensions`'s own doc comment already documents
/// for `Application.Conversations.ConversationService`. `Contigo.Api.Tests
/// .CapabilitiesEndpointTests` proves this file's HTTP behaviour with its own standalone minimal
/// host (`WebApplication.CreateBuilder` + `Microsoft.AspNetCore.TestHost.UseTestServer`) rather
/// than `WebApplicationFactory&lt;Program&gt;`, precisely because the real `Program.cs` does not
/// map it yet.
///
/// <para>
/// <b>No tenant header</b>: unlike every other endpoint in this host, the catalog is static,
/// tenant-agnostic metadata (ten fixed <see cref="Capability"/> rows, R-SYS-01) — a capability's
/// <see cref="CapabilityAvailability"/> is a declarative *condition* like
/// `needsValidatedContract`, not a resolved per-tenant boolean (resolving it against a real
/// validated-contract count is <see cref="CapabilityRouting.ResolveActions"/>'s job, a later
/// task's concern once a real caller assembles a <see cref="RoutingContext"/>). So this endpoint
/// needs no `X-Tenant-Id` the way `ContractsEndpointExtensions`/`PortfolioEndpointExtensions`/etc.
/// do.
/// </para>
///
/// <para>
/// <b>Role-aware listing (AC-1 "role-aware: admin-only entries hidden for Procurement")</b>: the
/// only axis this endpoint does need is "is the caller a Workspace Admin". `Contigo.Chat` cannot
/// see `Contigo.Identity.Workspace.Domain.WorkspaceRoleName` at all (its own architecture
/// allow-list is `[SharedKernel, AiGateway]` — see <see cref="CapabilityRoleGate"/>'s own doc
/// comment), so this composition root — the one project allowed to reference every module, per
/// `ChatEndpointExtensions`' own doc comment — is where the mapping from a real five-value
/// <see cref="WorkspaceRoleName"/> down to the catalog's two-value <see cref="CapabilityRoleGate"/>
/// happens. No host authentication is wired yet (ADR-010 is not in this task's "architecture
/// decisions in force" list — same gap `Program.cs`'s document endpoints already carry for
/// `X-Tenant-Id`, not promoted to reports/open-questions.md by this task for the identical
/// "concurrent appends break a phase-barrier merge" reason those endpoints' own comments already
/// give), so the interim signal is an `X-Role` header, reusing
/// <see cref="WorkspaceRoleClaimResolver.TryResolve(string?, out WorkspaceRoleName)"/> verbatim
/// rather than inventing new parsing (it already accepts `"Admin"`, `"Workspace Admin"`,
/// `"Contigo.Admin"`, ...). Fails closed on the "show admin entries" axis specifically: a missing,
/// unparseable, or non-Admin header hides admin-gated entries — the safe default for a visibility
/// gate, mirroring `WorkspacePrincipalAuthorization`'s own "fail closed, never fail open"
/// convention — never a hard 401/403 the way `AuditEndpointExtensions` is, since the catalog
/// itself is not sensitive, tenant data.
/// </para>
/// </summary>
public static class CapabilitiesEndpointExtensions
{
    /// <summary>Interim role signal — see the type doc comment's "Role-aware listing" section.</summary>
    private const string RoleHeaderName = "X-Role";

    public static IEndpointRouteBuilder MapCapabilitiesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/capabilities", GetCapabilities);
        return endpoints;
    }

    private static IResult GetCapabilities(HttpRequest request)
    {
        var isAdmin = CallerIsAdmin(request);

        var visible = CapabilityCatalog.All
            .Where(capability => capability.RoleGate != CapabilityRoleGate.Admin || isAdmin)
            .Select(ToResponse);

        return Results.Ok(new
        {
            version = CapabilityCatalog.Version,
            capabilities = visible,
        });
    }

    private static bool CallerIsAdmin(HttpRequest request) =>
        request.Headers.TryGetValue(RoleHeaderName, out var values)
        && WorkspaceRoleClaimResolver.TryResolve(values.ToString(), out var role)
        && role == WorkspaceRoleName.Admin;

    private static object ToResponse(Capability capability) => new
    {
        key = capability.Key,
        title = capability.Title,
        routePattern = capability.RoutePattern,
        description = capability.Description,
        exampleQuestions = capability.ExampleQuestions,
        roleGate = capability.RoleGate.ToApiValue(),
        availability = capability.Availability.ToApiValue(),
        howTo = capability.HowTo,
    };
}
