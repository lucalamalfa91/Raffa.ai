using Raffa.Quotes.Application.Strategy;
using Raffa.Quotes.Domain;
using Raffa.Quotes.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Quotes.Application.Outcome;

/// <summary>
/// Implements task E05/F03/US02/T01 (negotiation-outcome; parent story us-02-outcome-capture AC-1
/// "<c>POST /api/negotiations/outcomes</c> records original/target/final/saving/discount/duration/
/// levers"). Validates every field before writing anything (same "phase 1: validate, phase 2:
/// mutate" discipline <c>Raffa.Documents.Contracts.Application.ContractCorrectionService
/// .CorrectAsync</c>/<c>Raffa.Savings.Application.SavingsOpportunityService.UpdateAsync</c>
/// already follow), derives <see cref="Domain.NegotiationOutcome.RealizedSaving"/>/
/// <see cref="Domain.NegotiationOutcome.DiscountPercent"/> deterministically via
/// <see cref="NegotiationOutcomeCalculator"/> (Appendix C rule 6 — never trusts a caller-supplied
/// figure for arithmetic this module can compute exactly), then persists one new, append-only row
/// (see <c>Domain.NegotiationOutcome</c>'s own doc comment for why "versioned" — parent story AC-3 —
/// means "never updated in place", not an explicit version counter) and writes one
/// <see cref="IAuditWriter"/> entry — same "write then audit, still inside the same call's tenant
/// scope" placement as <c>Raffa.Quotes.Application.QuoteUploadService.UploadAsync</c>/
/// <c>SavingsOpportunityService.CreateAsync</c> (parent story AC-3's "audit-tracked" half; Appendix
/// C rule 9).
///
/// Owns its own tenant scope (<see cref="ITenantContext.BeginScope"/>) rather than trusting one is
/// already active — nothing upstream of this HTTP call opens one (same reasoning
/// <c>QuoteUploadService</c>'s own doc comment gives). <see cref="IAuditWriter"/> lives in
/// <c>Raffa.SharedKernel</c>, not <c>Raffa.Audit</c>, so this dependency does not cross the
/// ADR-002 module boundary (<c>Raffa.ArchitectureTests.DependencyDirectionTests</c>' allow-list
/// for <c>Raffa.Quotes</c> is exactly <c>[SharedKernel, Benchmark]</c> — the same trick
/// <c>QuoteUploadService</c> already uses).
///
/// <para>
/// Task E05/F03/US02/T02 (outcome-propagation; parent story AC-2 "Realized savings surface on the
/// savings dashboard (cross-wave)"): this method now also records
/// <see cref="Domain.NegotiationOutcome.SavingsOpportunityId"/> when the caller supplies one, but
/// does not itself act on it — this module cannot see <c>Raffa.Savings</c> (ADR-002), so it has no
/// way to confirm the id is real or to write the realized value onto that opportunity.
/// <c>Raffa.Api.NegotiationOutcomePropagationService</c> — the composition root — reads this same
/// id back after <see cref="CaptureAsync"/> returns and performs the actual cross-module
/// propagation; see that type's own doc comment.
/// </para>
/// </summary>
public sealed class NegotiationOutcomeService(
    QuotesDbContext dbContext, ITenantContext tenantContext, IClock clock, IAuditWriter auditWriter)
{
    public const string OriginalQuoteTotalMustBePositiveError =
        "'originalQuoteTotal' must be a positive amount.";

    public const string TargetPriceMustBeNonNegativeError =
        "'targetPrice', when supplied, must be zero or a positive amount.";

    public const string FinalPriceMustBePositiveError = "'finalPrice' must be a positive amount.";

    public const string NegotiationDurationDaysMustBeNonNegativeError =
        "'negotiationDurationDays' must be zero or a positive number of days.";

    public const string LeversUsedRequiredError = "At least one 'leversUsed' entry is required.";

    public static string LeversUsedInvalidError { get; } =
        $"'leversUsed' entries must each be one of: " +
        $"{string.Join(", ", Enum.GetNames<NegotiationLeverType>())}.";

    /// <summary>Returned by <see cref="CaptureAsync"/> when <c>quoteId</c> does not name a
    /// <see cref="Quote"/> for the caller's tenant. <c>Raffa.Api.NegotiationsEndpointExtensions</c>
    /// maps exactly this string to 404 — same <c>Result&lt;T&gt;.Error</c>-sentinel-to-404 convention
    /// <c>ContractCorrectionService.ContractNotFoundError</c>/<c>SavingsOpportunityService
    /// .NotFoundError</c> already establish (short and generic, not interpolated with the id, so the
    /// endpoint can compare it exactly).</summary>
    public const string QuoteNotFoundError = "Quote not found.";

    /// <summary><see cref="AuditEntry.Action"/> for a successful <see cref="CaptureAsync"/> call —
    /// past-tense <c>&lt;resource_type&gt;.&lt;verb&gt;</c>, matching this codebase's established
    /// convention (<c>SavingsOpportunityService.AuditIdentifiedAction</c>,
    /// <c>QuoteUploadService</c>'s own <c>"quote.uploaded"</c>).</summary>
    private const string AuditCapturedAction = "negotiation_outcome.captured";

    /// <summary><see cref="AuditEntry.ResourceType"/> — snake_case for a multi-word resource, same
    /// convention as <c>SavingsOpportunityService.AuditResourceType</c>'s own
    /// <c>"savings_opportunity"</c>.</summary>
    private const string AuditResourceType = "negotiation_outcome";

    /// <summary>Same interim-actor placeholder as <c>QuoteUploadService.UnattributedActor</c> — see
    /// that type's own doc comment for why: ADR-010 (Entra ID/OIDC) is not in this task's
    /// "Architecture decisions in force" list, so there is no validated caller identity yet.</summary>
    private const string UnattributedActor = "unattributed";

    /// <summary>
    /// Backs `POST /api/negotiations/outcomes` (AC-1). Validates <paramref name="request"/> fully
    /// before any query or write (an invalid request leaves the database untouched), then confirms
    /// <see cref="NegotiationOutcomeCaptureRequest.QuoteId"/> names a real, tenant-scoped
    /// <see cref="Quote"/> (the same tenant-scoped existence check <c>MarketAssessmentService
    /// .AssessAsync</c> already performs for the identical id) before computing and persisting the
    /// outcome.
    /// </summary>
    public async Task<Result<NegotiationOutcomeResult>> CaptureAsync(
        TenantId tenantId,
        NegotiationOutcomeCaptureRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.OriginalQuoteTotal <= 0m)
        {
            return Result<NegotiationOutcomeResult>.Failure(OriginalQuoteTotalMustBePositiveError);
        }

        if (request.TargetPrice is < 0m)
        {
            return Result<NegotiationOutcomeResult>.Failure(TargetPriceMustBeNonNegativeError);
        }

        if (request.FinalPrice <= 0m)
        {
            return Result<NegotiationOutcomeResult>.Failure(FinalPriceMustBePositiveError);
        }

        if (request.NegotiationDurationDays < 0)
        {
            return Result<NegotiationOutcomeResult>.Failure(NegotiationDurationDaysMustBeNonNegativeError);
        }

        if (request.LeversUsed is null || request.LeversUsed.Count == 0)
        {
            return Result<NegotiationOutcomeResult>.Failure(LeversUsedRequiredError);
        }

        var leversUsed = new List<NegotiationLeverType>(request.LeversUsed.Count);
        foreach (var leverText in request.LeversUsed)
        {
            if (!Enum.TryParse<NegotiationLeverType>(leverText, ignoreCase: true, out var parsedLever)
                || !Enum.IsDefined(parsedLever))
            {
                return Result<NegotiationOutcomeResult>.Failure(LeversUsedInvalidError);
            }

            leversUsed.Add(parsedLever);
        }

        var quoteId = new EntityId(request.QuoteId);

        using var tenantScope = tenantContext.BeginScope(tenantId);

        var quoteExists = await dbContext.Quotes
            .AnyAsync(q => q.TenantId == tenantId && q.Id == quoteId, cancellationToken)
            .ConfigureAwait(false);

        if (!quoteExists)
        {
            return Result<NegotiationOutcomeResult>.Failure(QuoteNotFoundError);
        }

        var now = clock.UtcNow;
        var calculation = NegotiationOutcomeCalculator.Compute(request.OriginalQuoteTotal, request.FinalPrice);

        var outcome = new NegotiationOutcome
        {
            TenantId = tenantId,
            QuoteId = quoteId,
            OriginalQuoteTotal = request.OriginalQuoteTotal,
            TargetPrice = request.TargetPrice,
            FinalPrice = request.FinalPrice,
            RealizedSaving = calculation.RealizedSaving,
            DiscountPercent = calculation.DiscountPercent,
            NegotiationDurationDays = request.NegotiationDurationDays,
            LeversUsed = leversUsed,
            CapturedAt = now,
            // Task E05/F03/US02/T02 (outcome-propagation) — see Domain.NegotiationOutcome
            // .SavingsOpportunityId's own doc comment: a bare, unvalidated cross-module id, honestly
            // null when the caller supplied none.
            SavingsOpportunityId = request.SavingsOpportunityId is { } savingsOpportunityGuid
                ? new EntityId(savingsOpportunityGuid)
                : null,
        };

        dbContext.NegotiationOutcomes.Add(outcome);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Recorded only once the capture itself is durable, still inside this call's own tenant
        // scope (same placement as QuoteUploadService.UploadAsync's own "upload -> audit event"
        // write). A failure here throws and fails the whole request rather than silently dropping
        // the audit record — ADR-011 treats audit as a compliance control, not a best-effort
        // side-channel (same posture ContractCorrectionService.CorrectAsync's own doc comment
        // documents for the identical placement).
        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                UnattributedActor,
                AuditCapturedAction,
                AuditResourceType,
                outcome.Id.Value.ToString(),
                now,
                $"quoteId={outcome.QuoteId} finalPrice={outcome.FinalPrice} " +
                $"realizedSaving={outcome.RealizedSaving} discountPercent={outcome.DiscountPercent} " +
                $"savingsOpportunityId={outcome.SavingsOpportunityId}"),
            cancellationToken).ConfigureAwait(false);

        return Result<NegotiationOutcomeResult>.Success(ToResult(outcome));
    }

    private static NegotiationOutcomeResult ToResult(NegotiationOutcome outcome) => new(
        outcome.Id,
        outcome.QuoteId,
        outcome.OriginalQuoteTotal,
        outcome.TargetPrice,
        outcome.FinalPrice,
        outcome.RealizedSaving,
        outcome.DiscountPercent,
        outcome.NegotiationDurationDays,
        outcome.LeversUsed,
        outcome.CapturedAt,
        outcome.SavingsOpportunityId);
}
