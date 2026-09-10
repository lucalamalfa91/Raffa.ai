using Raffa.Audit.Domain;
using Raffa.SharedKernel;

namespace Raffa.Audit.Infrastructure;

/// <summary>
/// EF Core-backed implementation of <see cref="IAuditWriter"/> (ADR-003/ADR-009, story
/// us-02-audit-baseline AC-1 — "Every module writes audit events via a shared audit
/// abstraction"). Every module depends only on the <see cref="IAuditWriter"/> port in
/// <c>Raffa.SharedKernel</c>; this type is the one place that turns an
/// <see cref="AuditEntry"/> into a durable, append-only <see cref="AuditEvent"/> row.
///
/// Deliberately does not read <see cref="Raffa.SharedKernel.Tenancy.ITenantContext"/> itself —
/// <paramref name="entry"/>'s own <see cref="AuditEntry.TenantId"/> is what gets written (the
/// caller already knows its tenant), while the *connection's* ambient tenant claim (set by
/// <see cref="Raffa.SharedKernel.Tenancy.TenantRlsConnectionInterceptor"/> from
/// <see cref="Raffa.SharedKernel.Tenancy.ITenantContext.Current"/>) is what Postgres Row-Level
/// Security checks the write against. ADR-009's "belt-and-suspenders" model: if the two ever
/// disagree — a caller passing a different tenant than the request/job's own active scope — the
/// database's `WITH CHECK` clause rejects the write rather than silently trusting the caller.
/// </summary>
public sealed class AuditWriter(AuditDbContext dbContext) : IAuditWriter
{
    public async Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var auditEvent = new AuditEvent
        {
            TenantId = entry.TenantId,
            Actor = entry.Actor,
            Action = entry.Action,
            ResourceType = entry.ResourceType,
            ResourceId = entry.ResourceId,
            OccurredAt = entry.Timestamp,
            Detail = entry.Detail,
        };

        // No update/delete path exists anywhere in this module — this Add + SaveChanges is the
        // entire write surface, and the `AddAppendOnlyEnforcement` migration backstops that at
        // the database level too (Appendix C rule 9 / this module's own append-only contract).
        dbContext.AuditEvents.Add(auditEvent);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
