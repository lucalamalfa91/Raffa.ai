using Raffa.Renewals.Domain;
using Raffa.Renewals.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Renewals.Application;

/// <summary>
/// Implements task E03/F03/US01/T02 (renewal-action): `POST /api/renewals/{id}/action` — parent
/// story us-01-renewal-dashboard-api AC-3 ("updates owner/status/action"), scoped to the caller's
/// tenant (ADR-009). Upserts a <see cref="RenewalAction"/> row keyed by (tenant, contract) — the
/// persistence <c>RenewalOpportunity</c>'s own doc comment named as a follow-up ("no task has given
/// `Raffa.Renewals` a `DbContext` yet"); this task is that follow-up.
///
/// Same shape as <c>Raffa.Documents.Contracts.Application.ContractCorrectionService</c>: owns its
/// own tenant scope (<see cref="ITenantContext.BeginScope"/>) rather than trusting one is already
/// active (nothing upstream opens one — see <c>Raffa.Api.Program</c>), validates every field
/// before writing anything, and writes one append-only <see cref="IAuditWriter"/> entry per
/// successful call (spec §14.1 "Comprehensive audit logging for access and data changes";
/// Appendix C rule 9). <see cref="IAuditWriter"/> lives in <c>Raffa.SharedKernel</c>, not
/// <c>Raffa.Audit</c>, so depending on it does not cross the ADR-002 module boundary
/// (`Raffa.Renewals`'s allow-list is `[SharedKernel, Benchmark]`) — the same trick
/// <c>RenewalThresholdScheduler</c> already uses for its own `renewal.approaching` audit entries.
///
/// Deliberately does not verify that <see cref="EntityId"/> contractId actually names an existing,
/// tenant-owned contract: ADR-002 forbids this module from referencing
/// <c>Raffa.Documents.Contracts</c> at all, so it structurally cannot ask. See
/// <see cref="RenewalAction"/>'s own doc comment for why that check, if ever added, belongs in
/// `Raffa.Api` instead, and why tenant scoping does not depend on it regardless.
/// </summary>
public sealed class RenewalActionService(
    RenewalsDbContext dbContext, ITenantContext tenantContext, IClock clock, IAuditWriter auditWriter)
{
    public const string OwnerRequiredError = "'owner' is required.";
    public const string ActionRequiredError = "'action' is required.";

    /// <summary><see cref="AuditEntry.Action"/> for every successful upsert — past-tense, matching
    /// this codebase's established convention (<c>DocumentUploadService</c>'s
    /// <c>"document.uploaded"</c>, <c>ContractCorrectionService</c>'s <c>"contract.corrected"</c>,
    /// <c>RenewalThresholdScheduler</c>'s <c>"renewal.approaching"</c>).</summary>
    private const string AuditUpdatedAction = "renewal.action_updated";

    /// <summary><see cref="AuditEntry.ResourceType"/> — lowercase, matching
    /// <c>DocumentUploadService</c>'s own <c>"document"</c>/<c>ContractCorrectionService</c>'s
    /// <c>"contract"</c> convention.</summary>
    private const string AuditResourceType = "renewal";

    public static string StatusRequiredError { get; } =
        $"'status' is required and must be one of: {string.Join(", ", Enum.GetNames<RenewalActionStatus>())}.";

    /// <summary>
    /// Validates <paramref name="owner"/>/<paramref name="status"/>/<paramref name="action"/>,
    /// then creates or updates the one <see cref="RenewalAction"/> row for
    /// (<paramref name="tenantId"/>, <paramref name="contractId"/>) — never a second row for the
    /// same renewal (see <see cref="Raffa.Renewals.Infrastructure.Configurations
    /// .RenewalActionConfiguration"/>'s own doc comment on the unique index this relies on).
    /// Validation runs before any query or write, so an invalid request leaves the database
    /// untouched (same "phase 1: validate everything, phase 2: mutate" discipline
    /// <c>ContractCorrectionService.CorrectAsync</c> already follows).
    /// </summary>
    /// <param name="actor">The caller's resolved token subject (ADR-011 w16 clause 15) — required,
    /// no default, so a placeholder can never return by omission. Recorded only on the
    /// <c>renewal.action_updated</c> audit entry, distinct from <paramref name="owner"/> (the
    /// caller-supplied "who is tracking this renewal" free text).</param>
    public async Task<Result<RenewalActionResult>> SetActionAsync(
        TenantId tenantId,
        EntityId contractId,
        string? owner,
        string? status,
        string? action,
        string actor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            return Result<RenewalActionResult>.Failure(OwnerRequiredError);
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            return Result<RenewalActionResult>.Failure(ActionRequiredError);
        }

        if (!Enum.TryParse<RenewalActionStatus>(status, ignoreCase: true, out var parsedStatus)
            || !Enum.IsDefined(parsedStatus))
        {
            return Result<RenewalActionResult>.Failure(StatusRequiredError);
        }

        // Entry point: open this call's own tenant scope (see the type doc comment) before any
        // query below, since the RLS connection interceptor reads ITenantContext.Current only
        // when the connection opens, which EF Core does lazily on first use.
        using var _ = tenantContext.BeginScope(tenantId);

        var existing = await dbContext.RenewalActions
            .SingleOrDefaultAsync(a => a.TenantId == tenantId && a.ContractId == contractId, cancellationToken)
            .ConfigureAwait(false);

        var now = clock.UtcNow;

        if (existing is null)
        {
            existing = new RenewalAction
            {
                TenantId = tenantId,
                ContractId = contractId,
                Owner = owner,
                Status = parsedStatus,
                Action = action,
                UpdatedAt = now,
            };
            dbContext.RenewalActions.Add(existing);
        }
        else
        {
            existing.Owner = owner;
            existing.Status = parsedStatus;
            existing.Action = action;
            existing.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Recorded only once the upsert itself is durable, still inside this call's own tenant
        // scope (same placement as ContractCorrectionService.CorrectAsync's own "correction then
        // audit entry" write). A failure here throws and fails the whole request rather than
        // silently dropping the audit record — ADR-011 treats audit as a compliance control, not a
        // best-effort side-channel.
        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                AuditUpdatedAction,
                AuditResourceType,
                contractId.Value.ToString(),
                now,
                $"owner={owner} status={parsedStatus} action={action}"),
            cancellationToken).ConfigureAwait(false);

        return Result<RenewalActionResult>.Success(
            new RenewalActionResult(contractId, owner, parsedStatus, action, now));
    }

    /// <summary>
    /// Reads back the current owner/status/action for one renewal, or <see langword="null"/> when
    /// none has ever been recorded for this (tenant, contract) pair. Task E19/F01/US01/T01
    /// (renewal-action-api; ADR-028 §D1) gave this its first HTTP caller:
    /// <c>Raffa.Api.RenewalsEndpointExtensions.GetRenewalActionAsync</c> calls this directly for
    /// `GET /api/renewals/{id}/action`'s single-row read; <see cref="GetActionsAsync"/> below is the
    /// batch counterpart the same task added so the sibling `GET /api/renewals` list can embed this
    /// fact under `savedAction` on every row without paying an N+1 for it.
    /// </summary>
    public async Task<RenewalActionResult?> GetActionAsync(
        TenantId tenantId, EntityId contractId, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);

        var existing = await dbContext.RenewalActions
            .AsNoTracking()
            .SingleOrDefaultAsync(a => a.TenantId == tenantId && a.ContractId == contractId, cancellationToken)
            .ConfigureAwait(false);

        return existing is null
            ? null
            : new RenewalActionResult(existing.ContractId, existing.Owner, existing.Status, existing.Action, existing.UpdatedAt);
    }

    /// <summary>
    /// Batch counterpart to <see cref="GetActionAsync"/> (task E19/F01/US01/T01; ADR-028 §D1):
    /// `GET /api/renewals` embeds this fact under `savedAction` in every row, and Renewals/Savings
    /// are list surfaces, so a per-row <see cref="GetActionAsync"/> call would be an N+1 across the
    /// portfolio (§D1's own words) — one query for the whole page's worth of contract ids instead,
    /// the same batch-by-page shape
    /// <c>Raffa.Api.PortfolioEndpointExtensions.ResolveSupplierNamesAsync</c> already uses for
    /// supplier names. Opens its own tenant scope exactly like <see cref="GetActionAsync"/> (nothing
    /// upstream in `Raffa.Api` has one open when this composition root calls in).
    /// </summary>
    /// <returns>
    /// A map keyed by contract id, containing an entry only for a contract id that actually has a
    /// persisted row. A requested id with nothing recorded simply has no entry — never a
    /// default/placeholder <see cref="RenewalActionResult"/> — so the caller renders the absent case
    /// as the status `NotStarted` (ADR-028 §D1) instead of a fabricated one.
    /// </returns>
    public async Task<IReadOnlyDictionary<EntityId, RenewalActionResult>> GetActionsAsync(
        TenantId tenantId,
        IReadOnlyCollection<EntityId> contractIds,
        CancellationToken cancellationToken = default)
    {
        // Skips the round trip entirely for a page with no candidates at all — the same
        // short-circuit ResolveSupplierNamesAsync uses for the identical reason.
        if (contractIds.Count == 0)
        {
            return new Dictionary<EntityId, RenewalActionResult>();
        }

        using var _ = tenantContext.BeginScope(tenantId);

        var rows = await dbContext.RenewalActions
            .AsNoTracking()
            .Where(a => a.TenantId == tenantId && contractIds.Contains(a.ContractId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows.ToDictionary(
            a => a.ContractId,
            a => new RenewalActionResult(a.ContractId, a.Owner, a.Status, a.Action, a.UpdatedAt));
    }

    /// <summary>
    /// Removes every persisted action row for the given contracts in this tenant. Used by the
    /// host's bulk document purge so a wiped contract does not leave a dangling renewal tracker.
    /// </summary>
    public async Task<int> DeleteForContractsAsync(
        TenantId tenantId,
        IReadOnlyCollection<EntityId> contractIds,
        CancellationToken cancellationToken = default)
    {
        if (contractIds.Count == 0)
        {
            return 0;
        }

        using var _ = tenantContext.BeginScope(tenantId);

        var idList = contractIds.ToList();
        var rows = await dbContext.RenewalActions
            .Where(a => a.TenantId == tenantId && idList.Contains(a.ContractId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (rows.Count == 0)
        {
            return 0;
        }

        dbContext.RenewalActions.RemoveRange(rows);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return rows.Count;
    }
}
