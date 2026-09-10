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

    /// <summary>Same interim-actor placeholder as <c>DocumentUploadService.UnattributedActor</c> /
    /// <c>ContractCorrectionService.UnattributedActor</c> — see either type's own doc comment for
    /// why: ADR-010 (Entra ID/OIDC) is not in this task's "Architecture decisions in force" list,
    /// so there is no validated caller identity to record, and a client-supplied "actor" on the
    /// request body would be an unverified, spoofable identity — worse than an explicit, honest
    /// placeholder. Distinct from <see cref="RenewalAction.Owner"/>, which the caller sets on
    /// purpose as free-text "who is tracking this renewal" — this constant is only ever the audit
    /// entry's own actor, never persisted onto the row itself.</summary>
    private const string UnattributedActor = "unattributed";

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
    public async Task<Result<RenewalActionResult>> SetActionAsync(
        TenantId tenantId,
        EntityId contractId,
        string? owner,
        string? status,
        string? action,
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
                UnattributedActor,
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
    /// none has ever been recorded for this (tenant, contract) pair — no HTTP route calls this yet
    /// (the spec's own Appendix A API table names only the POST), but it exists for the same
    /// "prove persistence really happened" reason <c>Raffa.Audit.Application.IAuditQueryService</c>
    /// existed before its own first route, and so a future `GET /api/renewals` can merge this in
    /// without this module needing a second write path.
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
}
