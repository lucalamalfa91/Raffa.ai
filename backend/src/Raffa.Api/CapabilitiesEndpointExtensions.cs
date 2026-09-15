using Raffa.Chat.Application.Capabilities;

namespace Raffa.Api;

/// <summary>
/// Maps `GET /api/capabilities` (R-SYS-01; story us-01-capability-catalog AC-1, task
/// E13/F08/US01/T01; wave w16 NW-31, task E18/F03/US01/T01). The catalog is static,
/// tenant-agnostic metadata (ten fixed <see cref="Capability"/> rows) — a capability's
/// <see cref="CapabilityAvailability"/> is a declarative *condition* like
/// `needsValidatedContract`, not a resolved per-tenant boolean. So this endpoint needs
/// no tenant header the way <c>ContractsEndpointExtensions</c>/<c>PortfolioEndpointExtensions</c>
/// do, and it is served whole to an unauthenticated caller.
///
/// <para>
/// Wave w16 (ADR-024 w16 clause 2; ADR-022 w16 clause 2; S16-6a) deleted the server-side
/// role filter that used to hide <see cref="CapabilityRoleGate.Admin"/> rows. The catalog
/// discloses *which admin features exist and nothing else* — no tenant data, no count, no
/// workspace name, nothing caller-derived. <see cref="Capability.RoleGate"/> stays on the
/// wire as a presentation label, never as authorization: every action behind an admin-gated
/// entry is enforced server-side by membership on its own endpoint.
/// </para>
/// </summary>
public static class CapabilitiesEndpointExtensions
{
    public static IEndpointRouteBuilder MapCapabilitiesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/capabilities", GetCapabilities);
        return endpoints;
    }

    private static IResult GetCapabilities()
    {
        var visible = CapabilityCatalog.All.Select(ToResponse);

        return Results.Ok(new
        {
            version = CapabilityCatalog.Version,
            capabilities = visible,
        });
    }

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
