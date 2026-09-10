namespace Raffa.Quotes.Application.Outcome;

/// <summary>
/// Input to <see cref="NegotiationOutcomeService.CaptureAsync"/> — `POST
/// /api/negotiations/outcomes`'s JSON body (task E05/F03/US02/T01, negotiation-outcome; parent
/// story us-02-outcome-capture AC-1's captured-field list), minus <c>tenantId</c> (an explicit,
/// separate parameter on <see cref="NegotiationOutcomeService.CaptureAsync"/> itself, never
/// embedded in the request payload — the same convention <c>Raffa.Renewals.Application
/// .RenewalActionService.SetActionAsync</c>/<c>Raffa.Savings.Application
/// .SavingsOpportunityService.CreateAsync</c> already follow, so a caller cannot smuggle a
/// different tenant's claim through the body).
///
/// <para>
/// <see cref="QuoteId"/> is a plain <see cref="Guid"/>, not <c>Raffa.SharedKernel.EntityId</c> —
/// deliberately: <c>Raffa.SharedKernel.EntityId</c>/<c>TenantId</c> have no
/// <c>System.Text.Json</c> converter registered anywhere in this solution, so binding one directly
/// from a minimal-API JSON body (as <c>Raffa.Savings.Application
/// .CreateSavingsOpportunityRequest.SupplierId</c>/<c>ContractId</c> do) would deserialize a bare
/// GUID string incorrectly — that type is honest about the gap ("Not yet wired to an HTTP route")
/// and has never actually been bound from JSON. Every request DTO in this codebase that *is* wired
/// to a real route (<c>Raffa.Savings.Application.SavingsOpportunityPatchRequest</c>) instead
/// exposes only natively JSON-safe primitive types and lets the endpoint/service parse
/// ids/enums explicitly — this type follows that proven pattern instead.
/// </para>
///
/// <para>
/// <see cref="LeversUsed"/> is <see cref="IReadOnlyList{T}"/> of <see cref="string"/>, not
/// <c>Raffa.Quotes.Application.Strategy.NegotiationLeverType</c>, for the identical reason: this
/// codebase has no global <c>JsonStringEnumConverter</c> registered (every existing endpoint
/// converts an enum to/from a wire string explicitly, e.g. <c>SavingsOpportunityPatchRequest
/// .Status</c>), so <see cref="NegotiationOutcomeService.CaptureAsync"/> parses/validates each
/// entry the same way <c>SavingsOpportunityService.UpdateAsync</c> parses its own <c>status</c>
/// string.
/// </para>
///
/// <para>
/// <see cref="SavingsOpportunityId"/> — task E05/F03/US02/T02 (outcome-propagation; parent story
/// AC-2 "Realized savings surface on the savings dashboard (cross-wave)") — is optional and,
/// like <see cref="QuoteId"/>, a plain <see cref="Guid"/> rather than
/// <c>Raffa.SharedKernel.EntityId</c>, for the identical JSON-binding reason given above. Trailing,
/// with a default, so every existing 6-argument construction of this record (every test and any
/// other caller written against task E05/F03/US02/T01's own shape) keeps compiling unchanged. See
/// <c>Domain.NegotiationOutcome.SavingsOpportunityId</c>'s own doc comment for why this is a bare,
/// unvalidated id (Raffa.Quotes cannot see Raffa.Savings, ADR-002) and why it is honestly
/// optional (not every outcome traces back to a pre-tracked opportunity).
/// </para>
/// </summary>
public sealed record NegotiationOutcomeCaptureRequest(
    Guid QuoteId,
    decimal OriginalQuoteTotal,
    decimal? TargetPrice,
    decimal FinalPrice,
    int NegotiationDurationDays,
    IReadOnlyList<string> LeversUsed,
    Guid? SavingsOpportunityId = null);
