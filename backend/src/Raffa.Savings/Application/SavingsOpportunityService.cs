using Raffa.Savings.Domain;
using Raffa.Savings.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Savings.Application;

/// <summary>
/// Implements task E04/F02/US02/T01 (savings-opportunity): `GET /api/savings` (list) and `PATCH
/// /api/savings/{id}` (update status/owner) — parent story us-02-savings-opportunity AC-1/AC-2. Task
/// E04/F02/US02/T02 (realized-savings) extends the same `PATCH` with a third field,
/// `realizedAmount` (AC-3, "Realized value is captured and audit-tracked") — see
/// <see cref="UpdateAsync"/>'s own doc comment.
/// Also exposes <see cref="CreateAsync"/> ("identify"), not yet wired to an HTTP route — see
/// <see cref="CreateSavingsOpportunityRequest"/>'s own doc comment for why.
///
/// Same shape as <c>Raffa.Renewals.Application.RenewalActionService</c>: owns its own tenant scope
/// (<see cref="ITenantContext.BeginScope"/>) rather than trusting one is already active (nothing
/// upstream opens one — see <c>Raffa.Api.Program</c>), validates every field before writing
/// anything, and writes one append-only <see cref="IAuditWriter"/> entry per successful mutation
/// (spec §14.1 "Comprehensive audit logging for access and data changes"; Appendix C rule 9).
/// <see cref="IAuditWriter"/> lives in <c>Raffa.SharedKernel</c>, not <c>Raffa.Audit</c>, so
/// depending on it does not cross the ADR-002 module boundary (`Raffa.Savings`'s allow-list is
/// `[SharedKernel, Benchmark]`) — the same trick <c>RenewalActionService</c> already uses.
/// </summary>
public sealed class SavingsOpportunityService(
    SavingsDbContext dbContext, ITenantContext tenantContext, IClock clock, IAuditWriter auditWriter)
{
    public const string TypeRequiredError = "'type' is required.";
    public const string CurrentSpendMustBePositiveError = "'currentSpend' must be a positive amount.";
    public const string CurrencyRequiredError = "'currency' is required.";
    public const string EstimatedSavingsRangeInvalidError =
        "'estimatedSavingsLow' and 'estimatedSavingsHigh' must both be >= 0, with low <= high.";
    public const string ConfidenceOutOfRangeError = "'confidence' must be between 0 and 1 inclusive.";
    public const string OwnerCannotBeBlankError = "'owner' cannot be blank when provided.";
    public const string NoFieldsToUpdateError =
        "At least one of 'owner', 'status' or 'realizedAmount' must be provided.";

    /// <summary>Task E04/F02/US02/T02 (realized-savings). A realized value is always a savings
    /// amount, never negative — same <c>&gt;= 0</c> convention
    /// <see cref="EstimatedSavingsRangeInvalidError"/> already applies to
    /// <see cref="Domain.SavingsOpportunity.EstimatedSavingsLow"/>/<c>High</c> (unlike
    /// <see cref="CurrentSpendMustBePositiveError"/>'s strictly-positive spend amount): a
    /// negotiation can genuinely realize zero savings and Procurement may still want that on
    /// record.</summary>
    public const string RealizedAmountMustBeNonNegativeError =
        "'realizedAmount' must be zero or a positive amount.";

    /// <summary>Task E04/F02/US02/T02 (realized-savings). Recording a realized value always means
    /// the opportunity is <see cref="SavingsOpportunityStatus.Realized"/> (see that member's own doc
    /// comment) — so an explicit <c>status</c> of anything else supplied in the very same call is a
    /// contradictory request, not something <see cref="UpdateAsync"/> silently resolves one way or
    /// the other. Omitting <c>status</c> entirely is not a conflict: <see cref="UpdateAsync"/> then
    /// finalizes the opportunity as <see cref="SavingsOpportunityStatus.Realized"/> itself.</summary>
    public const string RealizedAmountConflictsWithStatusError =
        "'realizedAmount' cannot be combined with a 'status' other than 'Realized'.";

    /// <summary>Returned by <see cref="UpdateAsync"/> when no opportunity with the given id exists
    /// for the caller's tenant. <c>Raffa.Api.SavingsEndpointExtensions</c> maps exactly this string
    /// to 404; every other failure maps to 400 — same convention
    /// <c>Raffa.Documents.Contracts.Application.ContractCorrectionService.ContractNotFoundError</c>
    /// already establishes.</summary>
    public const string NotFoundError = "Savings opportunity not found.";

    public static string StatusInvalidError { get; } =
        $"'status' must be one of: {string.Join(", ", Enum.GetNames<SavingsOpportunityStatus>())}.";

    /// <summary><see cref="AuditEntry.Action"/> for a successful <see cref="CreateAsync"/> call —
    /// past-tense, matching this codebase's established convention (see
    /// <c>Raffa.Renewals.Application.RenewalActionService</c>'s own doc comment for the full
    /// list).</summary>
    private const string AuditIdentifiedAction = "savings_opportunity.identified";

    /// <summary><see cref="AuditEntry.Action"/> for a successful <see cref="UpdateAsync"/> call that
    /// does not record a realized value — see <see cref="AuditRealizedAction"/> for the one that
    /// does.</summary>
    private const string AuditUpdatedAction = "savings_opportunity.updated";

    /// <summary><see cref="AuditEntry.Action"/> for a successful <see cref="UpdateAsync"/> call that
    /// records a realized value (task E04/F02/US02/T02, realized-savings; parent story AC-3
    /// "audit-tracked"). Takes the place of <see cref="AuditUpdatedAction"/> for that call — still
    /// exactly one <see cref="IAuditWriter"/> entry per successful mutation (see this class's own
    /// doc comment), its action name just reflecting the more specific, consequential thing that
    /// happened, the same way <see cref="AuditIdentifiedAction"/> is its own distinct action rather
    /// than folding into this one.</summary>
    private const string AuditRealizedAction = "savings_opportunity.realized";

    private const string AuditResourceType = "savings_opportunity";

    /// <summary>
    /// The reserved, documented non-human principal for <see cref="CreateAsync"/> (ADR-011 w16
    /// clause 16, S-T29): "identify" has no HTTP route and no caller today (see
    /// <see cref="CreateSavingsOpportunityRequest"/>'s own doc comment), so there is no human actor
    /// to thread through — the convention already live at
    /// <c>Raffa.Api.NegotiationOutcomePropagationService.SystemActor</c>
    /// (<c>"system:negotiation-outcome-propagation"</c>), reused rather than a new one invented.
    /// The <c>:</c> makes this string provably disjoint from any Entra <c>oid</c> token subject
    /// (ADR-011 w16 clause 16a) — a reserved, documented principal is a fact, not a placeholder.
    /// </summary>
    public const string SystemActor = "system:savings-opportunity-identification";

    /// <summary>
    /// "Identify" a new opportunity — validates every field, then persists it with
    /// <see cref="SavingsOpportunityStatus.Identified"/> and no <see cref="SavingsOpportunity.Owner"/>.
    /// See <see cref="CreateSavingsOpportunityRequest"/>'s own doc comment for why no HTTP route
    /// calls this yet.
    /// </summary>
    /// <param name="actor">The resolved actor for the <c>savings_opportunity.identified</c> audit
    /// row (ADR-011 w16 clause 15) — required, no default. No HTTP route calls this method today
    /// (see the type doc comment), so a future caller supplies either a caller's resolved token
    /// subject or, for a system-originated identification, <see cref="SystemActor"/>.</param>
    public async Task<Result<SavingsOpportunityResult>> CreateAsync(
        TenantId tenantId,
        CreateSavingsOpportunityRequest request,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Type))
        {
            return Result<SavingsOpportunityResult>.Failure(TypeRequiredError);
        }

        if (request.CurrentSpend <= 0m)
        {
            return Result<SavingsOpportunityResult>.Failure(CurrentSpendMustBePositiveError);
        }

        if (string.IsNullOrWhiteSpace(request.Currency))
        {
            return Result<SavingsOpportunityResult>.Failure(CurrencyRequiredError);
        }

        if (request.EstimatedSavingsLow < 0m
            || request.EstimatedSavingsHigh < 0m
            || request.EstimatedSavingsHigh < request.EstimatedSavingsLow)
        {
            return Result<SavingsOpportunityResult>.Failure(EstimatedSavingsRangeInvalidError);
        }

        if (request.Confidence is < 0d or > 1d)
        {
            return Result<SavingsOpportunityResult>.Failure(ConfidenceOutOfRangeError);
        }

        using var _ = tenantContext.BeginScope(tenantId);

        var now = clock.UtcNow;
        var opportunity = new SavingsOpportunity
        {
            TenantId = tenantId,
            SupplierId = request.SupplierId,
            ContractId = request.ContractId,
            Type = request.Type,
            CurrentSpend = request.CurrentSpend,
            Currency = request.Currency,
            EstimatedSavingsLow = request.EstimatedSavingsLow,
            EstimatedSavingsHigh = request.EstimatedSavingsHigh,
            Confidence = request.Confidence,
            Status = SavingsOpportunityStatus.Identified,
            Owner = null,
            CreatedAt = now,
            UpdatedAt = now,
        };

        dbContext.SavingsOpportunities.Add(opportunity);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Recorded only once the write itself is durable, still inside this call's own tenant scope
        // (same placement as ContractCorrectionService.CorrectAsync's own "write then audit entry").
        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                AuditIdentifiedAction,
                AuditResourceType,
                opportunity.Id.Value.ToString(),
                now,
                $"type={opportunity.Type} currentSpend={opportunity.CurrentSpend} {opportunity.Currency}"),
            cancellationToken).ConfigureAwait(false);

        return Result<SavingsOpportunityResult>.Success(ToResult(opportunity));
    }

    /// <summary>Backs `GET /api/savings` — every opportunity for the caller's tenant, newest
    /// identified first. No filters yet (status/supplier/etc.) — a follow-up, the same
    /// minimal-first shape several other list endpoints in this codebase started with.</summary>
    public async Task<IReadOnlyList<SavingsOpportunityResult>> ListAsync(
        TenantId tenantId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);

        var rows = await dbContext.SavingsOpportunities
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // Explicit lambda, not a bare `ToResult` method-group: now that ToResult takes an optional
        // second (realizedAmount) parameter, the method group is ambiguous against Select's
        // Func<T,int,TResult> (indexed) overload as well as its Func<T,TResult> one.
        return rows.Select(o => ToResult(o)).ToList();
    }

    /// <summary>
    /// Backs `PATCH /api/savings/{id}` — updates whichever of <paramref name="owner"/>/
    /// <paramref name="status"/>/<paramref name="realizedAmount"/> the caller supplied (non-null) on
    /// the one opportunity matching (<paramref name="tenantId"/>, <paramref name="id"/>). Validation
    /// runs before any query or write, so an invalid request leaves the database untouched (same
    /// "phase 1: validate everything, phase 2: mutate" discipline
    /// <c>ContractCorrectionService.CorrectAsync</c> already follows).
    ///
    /// <para>
    /// Task E04/F02/US02/T02 (realized-savings, parent story AC-3 "Realized value is captured and
    /// audit-tracked"): supplying <paramref name="realizedAmount"/> (validated
    /// <c>&gt;= 0</c> — <see cref="RealizedAmountMustBeNonNegativeError"/>) inserts a new, append-only
    /// <see cref="Domain.RealizedSavings"/> row for this opportunity, in the opportunity's own
    /// <see cref="Domain.SavingsOpportunity.Currency"/>, and always finalizes
    /// <see cref="Domain.SavingsOpportunity.Status"/> as <see cref="SavingsOpportunityStatus.Realized"/>
    /// — either because <paramref name="status"/> already parsed to exactly that (the one value
    /// compatible with a realized amount; anything else fails validation up front with
    /// <see cref="RealizedAmountConflictsWithStatusError"/>, before any query or write), or, when
    /// <paramref name="status"/> was not supplied at all in this same call, because
    /// <see cref="SavingsOpportunityStatus.Realized"/>'s own doc comment ties "a realized value was
    /// captured" directly to "the saving was actually achieved" — the two are not independent facts a
    /// caller can set out of step with each other. The resulting single <see cref="IAuditWriter"/>
    /// entry's action is <see cref="AuditRealizedAction"/> instead of <see cref="AuditUpdatedAction"/>
    /// for that call — still exactly one entry per successful mutation, never two.
    /// </para>
    /// </summary>
    /// <param name="actor">The caller's resolved token subject (ADR-011 w16 clause 15) — required,
    /// no default. Called both from `PATCH /api/savings/{id}` (a human's resolved identity) and
    /// from <c>Raffa.Api.NegotiationOutcomePropagationService.PropagateAsync</c> (its own
    /// <c>SystemActor</c>) — recorded on the <c>savings_opportunity.updated</c>/
    /// <c>savings_opportunity.realized</c> audit row either way.</param>
    public async Task<Result<SavingsOpportunityResult>> UpdateAsync(
        TenantId tenantId,
        EntityId id,
        string? owner,
        string? status,
        decimal? realizedAmount,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (owner is null && status is null && realizedAmount is null)
        {
            return Result<SavingsOpportunityResult>.Failure(NoFieldsToUpdateError);
        }

        if (owner is not null && string.IsNullOrWhiteSpace(owner))
        {
            return Result<SavingsOpportunityResult>.Failure(OwnerCannotBeBlankError);
        }

        SavingsOpportunityStatus? parsedStatus = null;
        if (status is not null)
        {
            if (!Enum.TryParse<SavingsOpportunityStatus>(status, ignoreCase: true, out var candidate)
                || !Enum.IsDefined(candidate))
            {
                return Result<SavingsOpportunityResult>.Failure(StatusInvalidError);
            }

            parsedStatus = candidate;
        }

        if (realizedAmount is < 0m)
        {
            return Result<SavingsOpportunityResult>.Failure(RealizedAmountMustBeNonNegativeError);
        }

        if (realizedAmount is not null
            && parsedStatus is { } explicitStatus
            && explicitStatus != SavingsOpportunityStatus.Realized)
        {
            return Result<SavingsOpportunityResult>.Failure(RealizedAmountConflictsWithStatusError);
        }

        using var _ = tenantContext.BeginScope(tenantId);

        var existing = await dbContext.SavingsOpportunities
            .SingleOrDefaultAsync(o => o.TenantId == tenantId && o.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (existing is null)
        {
            return Result<SavingsOpportunityResult>.Failure(NotFoundError);
        }

        var now = clock.UtcNow;

        if (owner is not null)
        {
            existing.Owner = owner;
        }

        if (parsedStatus is { } newStatus)
        {
            existing.Status = newStatus;
        }
        else if (realizedAmount is not null)
        {
            // No explicit status in this call, but a realized value was supplied: finalize the
            // opportunity as Realized rather than leave it at whatever it was before (see this
            // method's own doc comment / SavingsOpportunityStatus.Realized's).
            existing.Status = SavingsOpportunityStatus.Realized;
        }

        existing.UpdatedAt = now;

        RealizedSavings? realized = null;
        if (realizedAmount is not null)
        {
            realized = new RealizedSavings
            {
                TenantId = tenantId,
                SavingsOpportunityId = existing.Id,
                Amount = realizedAmount.Value,
                Currency = existing.Currency,
                RealizedAt = now,
            };
            dbContext.RealizedSavingsRecords.Add(realized);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                realized is null ? AuditUpdatedAction : AuditRealizedAction,
                AuditResourceType,
                existing.Id.Value.ToString(),
                now,
                realized is null
                    ? $"owner={existing.Owner} status={existing.Status}"
                    : $"owner={existing.Owner} status={existing.Status} " +
                      $"realizedAmount={realized.Amount} {realized.Currency}"),
            cancellationToken).ConfigureAwait(false);

        return Result<SavingsOpportunityResult>.Success(ToResult(existing, realized?.Amount));
    }

    private static SavingsOpportunityResult ToResult(
        SavingsOpportunity opportunity, decimal? realizedAmount = null) => new(
        opportunity.Id,
        opportunity.SupplierId,
        opportunity.ContractId,
        opportunity.Type,
        opportunity.CurrentSpend,
        opportunity.Currency,
        opportunity.EstimatedSavingsLow,
        opportunity.EstimatedSavingsHigh,
        opportunity.Confidence,
        opportunity.Status,
        opportunity.Owner,
        opportunity.CreatedAt,
        opportunity.UpdatedAt,
        realizedAmount,
        opportunity.OpportunityKey);

    public const string GeneratedKeyRequiredError = "Every generated opportunity needs a non-blank key.";
    public const string GeneratedKeyDuplicateError = "Generated opportunity keys must be unique within one call.";

    private const string AuditGeneratedAction = "savings_opportunity.generated";

    /// <summary>
    /// Upserts the opportunities Ask Raffa's savings lever calculator produced for one contract
    /// (the same "persist-all, reconcile by key" shape <c>RenewalNegotiationTodoService.UpsertAsync</c>
    /// uses for negotiation points): a row per <see cref="GeneratedSavingsOpportunity.Key"/> is
    /// created or refreshed; a row a person already moved past <see cref="SavingsOpportunityStatus.Identified"/>
    /// (owned, in progress, realized, dismissed) is never touched; a generated row whose key is absent
    /// from this call is left as it is (the levers depend on the question's goal, so absence is not
    /// evidence the saving is gone). Validation runs before any tenant-scoped write; one audit entry
    /// per call.
    /// </summary>
    public async Task<Result<IReadOnlyList<SavingsOpportunityResult>>> UpsertGeneratedAsync(
        TenantId tenantId,
        EntityId contractId,
        IReadOnlyCollection<GeneratedSavingsOpportunity> generated,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(generated);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        if (generated.Any(g => string.IsNullOrWhiteSpace(g.Key)))
        {
            return Result<IReadOnlyList<SavingsOpportunityResult>>.Failure(GeneratedKeyRequiredError);
        }

        if (generated.Select(g => g.Key).Distinct(StringComparer.Ordinal).Count() != generated.Count)
        {
            return Result<IReadOnlyList<SavingsOpportunityResult>>.Failure(GeneratedKeyDuplicateError);
        }

        foreach (var g in generated)
        {
            if (string.IsNullOrWhiteSpace(g.Type))
            {
                return Result<IReadOnlyList<SavingsOpportunityResult>>.Failure(TypeRequiredError);
            }

            if (g.CurrentSpend <= 0m)
            {
                return Result<IReadOnlyList<SavingsOpportunityResult>>.Failure(CurrentSpendMustBePositiveError);
            }

            if (string.IsNullOrWhiteSpace(g.Currency))
            {
                return Result<IReadOnlyList<SavingsOpportunityResult>>.Failure(CurrencyRequiredError);
            }

            if (g.EstimatedSavingsLow < 0m || g.EstimatedSavingsHigh < 0m || g.EstimatedSavingsHigh < g.EstimatedSavingsLow)
            {
                return Result<IReadOnlyList<SavingsOpportunityResult>>.Failure(EstimatedSavingsRangeInvalidError);
            }

            if (g.Confidence is < 0d or > 1d)
            {
                return Result<IReadOnlyList<SavingsOpportunityResult>>.Failure(ConfidenceOutOfRangeError);
            }
        }

        using var _ = tenantContext.BeginScope(tenantId);

        var existing = await dbContext.SavingsOpportunities
            .Where(o => o.TenantId == tenantId && o.ContractId == contractId && o.OpportunityKey != null)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var byKey = existing.ToDictionary(o => o.OpportunityKey!, StringComparer.Ordinal);
        var now = clock.UtcNow;
        var touched = new List<SavingsOpportunity>();
        var created = 0;
        var refreshed = 0;
        var frozen = 0;

        foreach (var g in generated)
        {
            var type = g.Type.Length <= 100 ? g.Type : g.Type[..100];

            if (byKey.TryGetValue(g.Key, out var row))
            {
                if (row.Status != SavingsOpportunityStatus.Identified)
                {
                    frozen++;
                    touched.Add(row);
                    continue;
                }

                row.SupplierId = g.SupplierId ?? row.SupplierId;
                row.Type = type;
                row.CurrentSpend = g.CurrentSpend;
                row.Currency = g.Currency;
                row.EstimatedSavingsLow = g.EstimatedSavingsLow;
                row.EstimatedSavingsHigh = g.EstimatedSavingsHigh;
                row.Confidence = g.Confidence;
                row.UpdatedAt = now;
                refreshed++;
                touched.Add(row);
                continue;
            }

            var opportunity = new SavingsOpportunity
            {
                TenantId = tenantId,
                SupplierId = g.SupplierId,
                ContractId = contractId,
                Type = type,
                CurrentSpend = g.CurrentSpend,
                Currency = g.Currency,
                EstimatedSavingsLow = g.EstimatedSavingsLow,
                EstimatedSavingsHigh = g.EstimatedSavingsHigh,
                Confidence = g.Confidence,
                Status = SavingsOpportunityStatus.Identified,
                Owner = null,
                OpportunityKey = g.Key,
                CreatedAt = now,
                UpdatedAt = now,
            };
            dbContext.SavingsOpportunities.Add(opportunity);
            created++;
            touched.Add(opportunity);
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                AuditGeneratedAction,
                AuditResourceType,
                contractId.Value.ToString(),
                now,
                $"created={created} refreshed={refreshed} frozen={frozen}"),
            cancellationToken).ConfigureAwait(false);

        return Result<IReadOnlyList<SavingsOpportunityResult>>.Success(touched.Select(o => ToResult(o)).ToList());
    }
}

/// <summary>One lever-derived opportunity to upsert — see
/// <see cref="SavingsOpportunityService.UpsertGeneratedAsync"/>.</summary>
public sealed record GeneratedSavingsOpportunity(
    string Key,
    string Type,
    EntityId? SupplierId,
    decimal CurrentSpend,
    string Currency,
    decimal EstimatedSavingsLow,
    decimal EstimatedSavingsHigh,
    double Confidence);
