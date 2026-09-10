using Raffa.SharedKernel;

namespace Raffa.Savings.Application;

/// <summary>
/// Input to <see cref="SavingsOpportunityService.CreateAsync"/> — parent story
/// us-02-savings-opportunity AC-2's captured-field list, minus <c>status</c>/<c>owner</c> (every
/// newly identified opportunity starts <see cref="Domain.SavingsOpportunityStatus.Identified"/> with
/// no owner — see <see cref="Domain.SavingsOpportunity.Owner"/>'s own doc comment) and minus
/// <c>tenantId</c> (an explicit, separate parameter on
/// <see cref="SavingsOpportunityService.CreateAsync"/> itself, never embedded in a request body —
/// the same convention <c>Raffa.Renewals.Application.RenewalActionService.SetActionAsync</c>
/// already follows, so a caller cannot smuggle a different tenant's claim through the request
/// payload).
///
/// Not yet wired to an HTTP route (this task's own AC-1 names only `GET`/`PATCH`
/// `/api/savings`) — <see cref="SavingsOpportunityService.CreateAsync"/> exists so a test (or a
/// future real caller, e.g. a worker job that runs
/// <c>Raffa.Savings.Application.PriceNormalizationCalculator</c> against a real contract) can
/// identify an opportunity; wiring a real caller is deliberately out of this task's own scope, the
/// same "wiring lands with the first real caller" convention `backend/README.md` documents
/// repeatedly for this solution's other modules.
/// </summary>
public sealed record CreateSavingsOpportunityRequest(
    EntityId? SupplierId,
    EntityId? ContractId,
    string Type,
    decimal CurrentSpend,
    string Currency,
    decimal EstimatedSavingsLow,
    decimal EstimatedSavingsHigh,
    double Confidence);
