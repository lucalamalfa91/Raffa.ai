using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Implements task E19/F03/US01/T01 (us-01-step-ticks-api): the server-side home for Contract
/// 360's 4-step negotiation checklist (ADR-028 §D3). <see cref="GetAsync"/> reads the ticked step
/// keys for one contract; <see cref="SetAsync"/> writes the whole set in one call.
///
/// <para>
/// <b>The row's presence is the tick</b> (ADR-003 w16 clause 1) — there is no <c>Ticked</c>
/// boolean and no third, nullable state to interpret. <see cref="SetAsync"/> therefore never
/// "updates" a row's own tick flag: a step present in the caller's set that has no row yet is
/// inserted, a row whose step is absent from the caller's set is deleted, and a step present in
/// both is left completely untouched (its original <see cref="ContractNegotiationStep.TickedAt"/>
/// survives) — which is exactly what makes the whole-set <c>PUT</c> idempotent (AC-2: "sending
/// the same set twice leaves the same rows").
/// </para>
///
/// <para>
/// Same shape as <see cref="ContractCorrectionService"/>/<see cref="DocumentValidationService"/>:
/// owns its own tenant scope (<see cref="ITenantContext.BeginScope"/>), validates everything
/// before writing anything (AC-3: an unknown step name must leave the database untouched), and
/// writes one append-only <see cref="IAuditWriter"/> entry per successful <see cref="SetAsync"/>
/// call. <paramref name="actor"/> below is the validated caller identity
/// (<c>ICallerContext</c>'s <c>oid</c>) the same way <see cref="DocumentValidationService
/// .ValidateAsync"/>'s own <c>actor</c> parameter already is — this task lands after NW-05, so,
/// unlike the older services in this module, there is no "no validated identity yet" placeholder
/// to fall back to.
/// </para>
///
/// <para>
/// Owns its own tenant scope on both members (ADR-009 belt-and-suspenders: the explicit
/// <c>tenant_id</c> predicate below plus Postgres RLS are two independent reasons a cross-tenant
/// contract id reads back as "not found", not one — AC-5).
/// </para>
/// </summary>
public sealed class NegotiationStepService(
    DocumentsContractsDbContext dbContext,
    ITenantContext tenantContext,
    IClock clock,
    IAuditWriter auditWriter)
{
    /// <summary>Returned by <see cref="SetAsync"/> when no contract with the given id exists for
    /// the caller's tenant. <c>Raffa.Api.ContractsEndpointExtensions</c> maps exactly this string
    /// to 404, every other failure to 400 — the same sentinel-string convention
    /// <see cref="ContractCorrectionService.ContractNotFoundError"/> already establishes.</summary>
    public const string ContractNotFoundError = "Contract not found.";

    /// <summary>Returned by <see cref="SetAsync"/> when the caller's set names a step outside the
    /// four canonical <see cref="NegotiationStep"/> members (AC-3/AC-6). Deliberately does not
    /// echo the offending value back — same "list what is valid, not what was wrong" shape as
    /// <c>NegotiationOutcomeService.LeversUsedInvalidError</c>.</summary>
    public static string UnknownStepError { get; } =
        $"Each negotiation step must be one of: {string.Join(", ", Enum.GetNames<NegotiationStep>())}.";

    private const string AuditSetAction = "contract.negotiation_steps_set";
    private const string AuditResourceType = "contract";

    /// <summary>
    /// The ticked step keys for <paramref name="contractId"/>, in canonical checklist order
    /// (<see cref="NegotiationStep"/>'s declaration order) — never null for a contract that
    /// exists, even when nothing has been ticked yet (AC-1: "an untouched contract returns an
    /// empty set, not 404"). <see langword="null"/> only when the contract itself does not exist
    /// for this tenant (mirrors <see cref="ContractCorrectionHistoryQueryService.GetHistoryAsync"/>'s
    /// own null-vs-empty split for "no such contract" vs. "contract exists, nothing recorded yet").
    /// </summary>
    public async Task<IReadOnlyList<string>?> GetAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default)
    {
        // Entry point: open this call's own tenant scope rather than trusting one is already
        // active (see the type doc comment) -- must happen before either query below, since the
        // RLS connection interceptor reads ITenantContext.Current only when the connection opens.
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var contractExists = await dbContext.Contracts
            .AsNoTracking()
            .AnyAsync(c => c.TenantId == tenantId && c.Id == contractId, cancellationToken)
            .ConfigureAwait(false);

        if (!contractExists)
        {
            return null;
        }

        var ticked = await dbContext.ContractNegotiationSteps
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.ContractId == contractId)
            .Select(s => s.Step)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return OrderedStepNames(ticked);
    }

    /// <summary>
    /// Writes the whole ticked-step set for <paramref name="contractId"/> — AC-2's idempotent
    /// whole-set semantics, inserting any step in <paramref name="steps"/> with no row yet and
    /// deleting any row whose step is absent from <paramref name="steps"/>, in the one
    /// transaction <see cref="DbContext.SaveChangesAsync(CancellationToken)"/> already gives a
    /// single call (task text: "in one transaction"). An unknown step name fails the whole call
    /// before anything is queried or written (AC-3) — same "validate everything before mutating"
    /// discipline <see cref="ContractCorrectionService.CorrectAsync"/> and
    /// <c>Raffa.Renewals.Application.RenewalActionService.SetActionAsync</c> already use, and the
    /// same case-insensitive <c>Enum.TryParse</c> + <c>Enum.IsDefined</c> check
    /// <c>NegotiationOutcomeService.CaptureAsync</c> already establishes for
    /// <c>NegotiationOutcome.LeversUsed</c> (task text: "follow it; invent nothing").
    /// </summary>
    /// <param name="steps">The whole set the caller wants ticked, as step-name strings. A step
    /// already ticked that is also present here is left completely untouched (see the type doc
    /// comment) — this is what makes a repeated, identical call a true no-op.</param>
    /// <param name="actor">The validated caller identity, recorded on the audit entry — never a
    /// client-supplied value (see the type doc comment).</param>
    public async Task<Result<IReadOnlyList<string>>> SetAsync(
        TenantId tenantId,
        EntityId contractId,
        IReadOnlyCollection<string> steps,
        string actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        // Phase 1 (AC-3): validate every incoming name against the closed enum before this call
        // queries or mutates anything -- an unknown name must leave the database untouched.
        var parsedSteps = new HashSet<NegotiationStep>();
        foreach (var stepName in steps)
        {
            if (!Enum.TryParse<NegotiationStep>(stepName, ignoreCase: true, out var parsed)
                || !Enum.IsDefined(parsed))
            {
                return Result<IReadOnlyList<string>>.Failure(UnknownStepError);
            }

            parsedSteps.Add(parsed);
        }

        // Entry point: open this call's own tenant scope (see the type doc comment) before any
        // query below, same placement as GetAsync.
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var contractExists = await dbContext.Contracts
            .AsNoTracking()
            .AnyAsync(c => c.TenantId == tenantId && c.Id == contractId, cancellationToken)
            .ConfigureAwait(false);

        if (!contractExists)
        {
            return Result<IReadOnlyList<string>>.Failure(ContractNotFoundError);
        }

        // Tracked (no AsNoTracking): rows removed below must be attached to this DbContext's
        // change tracker for Remove to stage a DELETE.
        var existingRows = await dbContext.ContractNegotiationSteps
            .Where(s => s.TenantId == tenantId && s.ContractId == contractId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var now = clock.UtcNow;
        var alreadyTicked = new HashSet<NegotiationStep>();
        foreach (var row in existingRows)
        {
            if (parsedSteps.Contains(row.Step))
            {
                // Still wanted -- left untouched, including TickedAt (see the type doc comment on
                // why a repeated identical PUT must not perturb the original tick instant).
                alreadyTicked.Add(row.Step);
            }
            else
            {
                dbContext.ContractNegotiationSteps.Remove(row);
            }
        }

        foreach (var step in parsedSteps)
        {
            if (alreadyTicked.Contains(step))
            {
                continue;
            }

            dbContext.ContractNegotiationSteps.Add(new ContractNegotiationStep
            {
                TenantId = tenantId,
                ContractId = contractId,
                Step = step,
                TickedAt = now,
            });
        }

        // One call -- every staged insert/delete above commits as the one transaction the task
        // text asks for; EF Core wraps a single SaveChangesAsync in one implicit transaction, the
        // same guarantee ContractCorrectionService.CorrectAsync's own multi-row write already
        // relies on.
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var resultSteps = OrderedStepNames(parsedSteps);

        // Recorded only once the write is durable, still inside this call's own tenant scope --
        // same placement as every other write in this module (ContractCorrectionService,
        // DocumentValidationService).
        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                AuditSetAction,
                AuditResourceType,
                contractId.Value.ToString(),
                now,
                $"steps={string.Join(",", resultSteps)}"),
            cancellationToken).ConfigureAwait(false);

        return Result<IReadOnlyList<string>>.Success(resultSteps);
    }

    /// <summary>Renders a set of ticked steps in canonical checklist order
    /// (<see cref="NegotiationStep"/>'s declaration order, matching the design oracle's own
    /// ordered "4-step checklist") rather than whatever order the caller's set or the database
    /// happened to enumerate in -- deterministic for both <see cref="GetAsync"/> and the echo-back
    /// on <see cref="SetAsync"/>.</summary>
    private static IReadOnlyList<string> OrderedStepNames(IReadOnlyCollection<NegotiationStep> steps)
    {
        var set = steps as HashSet<NegotiationStep> ?? new HashSet<NegotiationStep>(steps);
        return Enum.GetValues<NegotiationStep>()
            .Where(set.Contains)
            .Select(step => step.ToString())
            .ToList();
    }
}

/// <summary>
/// The <c>PUT /api/contracts/{id}/negotiation-steps</c> request body: the whole desired set of
/// ticked step keys (parent story AC-2). A key this request omits is unticked — there is no
/// partial-update shape (see <see cref="NegotiationStepService.SetAsync"/>'s own doc comment).
/// <see cref="Steps"/> is a plain <see cref="IReadOnlyCollection{T}"/> of strings, not a set:
/// System.Text.Json binds a JSON array into one with no ambiguity, and
/// <see cref="NegotiationStepService.SetAsync"/> builds its own internal <see cref="HashSet{T}"/>
/// regardless of what the caller sent, including a request that repeats the same key twice. This
/// record is co-located with <see cref="NegotiationStepService"/> rather than given its own file,
/// the same way <see cref="ContractCorrectionHistoryQueryService"/> co-locates its own
/// <c>CorrectionHistoryRecord</c> read model.
/// </summary>
public sealed record NegotiationStepsRequest(IReadOnlyCollection<string> Steps);
