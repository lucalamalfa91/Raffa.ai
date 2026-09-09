using Contigo.SharedKernel;

namespace Contigo.Chat.Application.Capabilities;

/// <summary>
/// The caller's own role, for <see cref="CapabilityRouting.ResolveActions"/>'s role-gate check
/// (an admin-gated capability, e.g. <see cref="CapabilityCatalog.WorkspaceMembersKey"/>, never
/// produces a <see cref="CopilotAction"/> for a <see cref="Standard"/> caller). Two-value, like
/// <see cref="CapabilityRoleGate"/> — not the five-value
/// <c>Contigo.Identity.Workspace.Domain.WorkspaceRoleName</c> catalog, for the identical
/// architecture-allow-list reason <see cref="CapabilityRoleGate"/>'s own doc comment gives.
/// </summary>
public enum CapabilityCallerRole
{
    /// <summary>Any non-admin workspace role (Procurement, Legal, Finance, Read-only).</summary>
    Standard,

    /// <summary>Workspace Admin.</summary>
    Admin,
}

/// <summary>
/// Everything <see cref="CapabilityRouting.ResolveActions"/> needs to turn an intent into real
/// <see cref="CopilotAction"/>s (task E13/F08/US01/T01 coding objective: "ResolveActions(intents,
/// RoutingContext { validatedContractCount, role, contractId?, quoteId?, documentId? })"). Built by
/// a later task's caller (the Ask engine) from whatever it already resolved for this turn — this
/// module never queries a database itself (see <see cref="CapabilityRouting"/>'s own doc comment).
/// </summary>
/// <param name="ValidatedContractCount">Drives R-SYS-04's availability replacement — 0 replaces
/// every <see cref="CapabilityAvailability.NeedsValidatedContract"/> action with the Documents
/// upload action.</param>
/// <param name="Role">See <see cref="CapabilityCallerRole"/>.</param>
/// <param name="ContractId">Known when this turn is already scoped to one contract (e.g. a named,
/// validated supplier, or an Ask conversation opened from Contract 360's "Ask about it") — enables
/// the `/contracts/{contractId}` and `/renewals?select={contractId}` href shapes.</param>
/// <param name="QuoteId">Known when this turn is already scoped to one quote — enables the
/// `/quotes/{quoteId}` href shape.</param>
/// <param name="DocumentId">Known when this turn is already scoped to one document — enables the
/// `/documents?review={documentId}` href shape.</param>
public sealed record RoutingContext(
    int ValidatedContractCount,
    CapabilityCallerRole Role,
    EntityId? ContractId = null,
    EntityId? QuoteId = null,
    EntityId? DocumentId = null);
