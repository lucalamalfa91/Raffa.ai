using Raffa.Renewals.Domain;
using Raffa.SharedKernel;

namespace Raffa.Renewals.Application;

/// <summary>Outcome of a successful <see cref="RenewalActionService.SetActionAsync"/> or
/// <see cref="RenewalActionService.GetActionAsync"/> call — the current, persisted owner/status/
/// action state for one renewal (task E03/F03/US01/T02, renewal-action).</summary>
public sealed record RenewalActionResult(
    EntityId ContractId,
    string Owner,
    RenewalActionStatus Status,
    string Action,
    DateTimeOffset UpdatedAt);
