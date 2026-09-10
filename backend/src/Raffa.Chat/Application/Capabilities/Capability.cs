namespace Raffa.Chat.Application.Capabilities;

/// <summary>
/// Who may see this capability at all (R-SYS-01's "role gate"; ADR-018's "two roles only for V1
/// nav variation" — Workspace Admin vs everyone else). Deliberately not the five-value
/// <c>Raffa.Identity.Workspace.Domain.WorkspaceRoleName</c> catalog: <c>Raffa.Chat</c>'s own
/// architecture allow-list (<c>Raffa.ArchitectureTests.DependencyDirectionTests</c>) is exactly
/// <c>[SharedKernel, AiGateway]</c>, so this module cannot reference
/// <c>Raffa.Identity.Workspace</c> at all — <c>Raffa.Api</c> (the one project allowed to
/// reference every module, per <c>ChatEndpointExtensions</c>' own doc comment) is the composition
/// root that maps a real <c>WorkspaceRoleName</c> down to this two-value gate
/// (<c>CapabilitiesEndpointExtensions</c>) or to <see cref="RoutingContext"/>'s own
/// <c>CapabilityCallerRole</c> (<c>CapabilityRouting</c>).
/// </summary>
public enum CapabilityRoleGate
{
    /// <summary>Every workspace role can see and use this capability.</summary>
    Any,

    /// <summary>Workspace Admin only (spec §3.1; ADR-018 "Workspace Admin — ... owns
    /// `/workspace/members` and invites").</summary>
    Admin,
}

/// <summary>
/// R-SYS-01's "availability condition" — a declarative label a caller (the web client, or
/// <see cref="CapabilityRouting.ResolveActions"/>) uses to decide whether this capability is
/// usable *right now*. Not a per-tenant resolved boolean: <c>GET /api/capabilities</c> is static,
/// tenant-agnostic metadata (see <c>CapabilitiesEndpointExtensions</c>'s own doc comment for why it
/// takes no <c>X-Tenant-Id</c>) — resolving this condition against a real validated-contract count
/// is <see cref="CapabilityRouting"/>'s job, once a caller assembles a <see cref="RoutingContext"/>.
/// </summary>
public enum CapabilityAvailability
{
    /// <summary>Usable with zero validated contracts (Ask itself, Documents, Contract 360 once a
    /// specific contract id is already known).</summary>
    Always,

    /// <summary>Greyed until the first validated contract exists (`raffa-v2/ia-v2.md`
    /// "Secondary — 'From your contracts' ... greyed until the first validated contract":
    /// Portfolio, Renewals, Quote check). R-SYS-04's replacement rule fires for this value.</summary>
    NeedsValidatedContract,

    /// <summary>Workspace Admin only (Workspace &amp; members) — availability and
    /// <see cref="Capability.RoleGate"/> agree for this one entry.</summary>
    Admin,
}

/// <summary>
/// `GET /api/capabilities` wire-format strings for <see cref="CapabilityRoleGate"/>/
/// <see cref="CapabilityAvailability"/> — R-SYS-01/story us-01-capability-catalog AC-1's own
/// literal tokens (<c>any</c>/<c>admin</c>, <c>always</c>/<c>needsValidatedContract</c>/
/// <c>admin</c>), not this codebase's usual bare <c>.ToString()</c> PascalCase convention (see
/// e.g. <c>ContractsEndpointExtensions.ToContract360Response</c>'s <c>header.Type.ToString()</c>):
/// AC-1 spells these two fields out in lower/camel case explicitly, unlike every enum this
/// codebase has serialized before, so a bare <c>.ToString()</c> would not match the accepted text.
/// </summary>
public static class CapabilityWireFormat
{
    public static string ToApiValue(this CapabilityRoleGate gate) => gate switch
    {
        CapabilityRoleGate.Any => "any",
        CapabilityRoleGate.Admin => "admin",
        _ => throw new ArgumentOutOfRangeException(nameof(gate), gate, "Unknown capability role gate."),
    };

    public static string ToApiValue(this CapabilityAvailability availability) => availability switch
    {
        CapabilityAvailability.Always => "always",
        CapabilityAvailability.NeedsValidatedContract => "needsValidatedContract",
        CapabilityAvailability.Admin => "admin",
        _ => throw new ArgumentOutOfRangeException(
            nameof(availability), availability, "Unknown capability availability."),
    };
}

/// <summary>
/// One entry of the versioned V2 capability catalog (R-SYS-01; ADR-024 "Capability catalog
/// (R-SYS)"; task E13/F08/US01/T01; story us-01-capability-catalog AC-1). Every field is
/// `GET /api/capabilities` wire content — see <c>CapabilitiesEndpointExtensions</c> for the JSON
/// projection (lower-camel-case field names, <see cref="RoleGate"/>/<see cref="Availability"/>
/// serialized via <see cref="CapabilityWireFormat.ToApiValue(CapabilityRoleGate)"/>/
/// <see cref="CapabilityWireFormat.ToApiValue(CapabilityAvailability)"/>).
///
/// <para>
/// <see cref="RoutePattern"/> is the one canonical route `GET /api/capabilities` lists and
/// <see cref="FeatureCitation.For"/> cites — a literal path for a list/base screen (`/renewals`,
/// `/quotes`), or a path that already carries its own placeholder for a capability that only ever
/// means "one specific object" (`/contracts/{contractId}`, `/documents?review={documentId}`).
/// Building an *id-scoped* href for a capability whose canonical pattern is the base route
/// (`/renewals?select={id}`, `/quotes/{id}`) is <see cref="CapabilityRouting.ResolveActions"/>'s
/// own job, from the known id in a <see cref="RoutingContext"/> — not a second field here (AC-1's
/// own field list names exactly one `routePattern` per entry, never a plural).
/// </para>
/// </summary>
/// <param name="Key">Stable identifier (`ask`, `documents`, ... `workspace-members`) — never
/// shown to a user, only used for routing/lookup (<see cref="CapabilityCatalog.Find"/>).</param>
/// <param name="Title">Human-readable name; also the feature-citation card title (R-SYS-03 "title
/// = capability").</param>
/// <param name="RoutePattern">See the type doc comment above.</param>
/// <param name="Description">One paragraph, "what it does" — also the feature-citation snippet
/// (R-SYS-03).</param>
/// <param name="ExampleQuestions">Illustrative questions that route here.</param>
/// <param name="RoleGate">See <see cref="CapabilityRoleGate"/>.</param>
/// <param name="Availability">See <see cref="CapabilityAvailability"/>.</param>
/// <param name="HowTo">Ordered how-to steps (R-SYS-01 "how-to steps").</param>
public sealed record Capability(
    string Key,
    string Title,
    string RoutePattern,
    string Description,
    IReadOnlyList<string> ExampleQuestions,
    CapabilityRoleGate RoleGate,
    CapabilityAvailability Availability,
    IReadOnlyList<string> HowTo);
