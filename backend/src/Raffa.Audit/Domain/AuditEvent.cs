namespace Raffa.Audit.Domain;

/// <summary>
/// The persisted, append-only record of one <see cref="Raffa.SharedKernel.AuditEntry"/> write
/// (product spec §14.1 "Comprehensive audit logging for access and data changes"; Appendix C rule
/// 9 "Capture negotiation outcomes and corrections from day one"; story us-02-audit-baseline AC-1
/// "Every module writes audit events via a shared audit abstraction").
/// <see cref="Raffa.Audit.Infrastructure.AuditWriter"/> (this module's
/// <see cref="Raffa.SharedKernel.IAuditWriter"/> implementation) is the only writer; there is no
/// update/delete path anywhere in this module's application surface, and the
/// `AddAppendOnlyEnforcement` migration makes that a database-enforced guarantee — not just an
/// API-shape convention — by rejecting any `UPDATE`/`DELETE` against this row's table regardless
/// of which role or connection issues it.
///
/// Deliberately independent of <c>Raffa.SharedKernel.AuditEntry</c>'s own shape even though the
/// fields line up 1:1 today: <c>Raffa.SharedKernel.AuditEntry</c> is the cross-module contract
/// every caller depends on, while this type is this module's own persistence model — the two are
/// allowed to diverge later (for example if the query side in task E01/F06/US02/T02 needs a
/// column the shared contract has no reason to carry) without forcing a breaking change on every
/// module that calls <see cref="Raffa.SharedKernel.IAuditWriter.WriteAsync"/>.
/// </summary>
public sealed class AuditEvent : TenantScopedEntity
{
    /// <summary>Who performed the action — an identity subject/email/system-job name, not an FK
    /// (the actor may belong to a different bounded context, or be a background job, not a
    /// queryable row in this module).</summary>
    public required string Actor { get; set; }

    /// <summary>What happened, e.g. "document.upload", "contract.correction" — a free-form,
    /// module-defined verb rather than an enum, so a new module can start writing audit events
    /// without a migration to this module.</summary>
    public required string Action { get; set; }

    /// <summary>The kind of resource acted on, e.g. "Document", "Contract" — paired with
    /// <see cref="ResourceId"/> as a loose (non-FK) pointer, exactly like
    /// <c>Raffa.Documents.Contracts.Domain.CorrectionHistory</c>'s own
    /// `TargetEntityType`/`TargetEntityId` pair, because an audit event can reference any entity
    /// in any module — no single foreign key could express that.</summary>
    public required string ResourceType { get; set; }

    /// <summary>The specific resource instance acted on. A plain string (not
    /// <c>Raffa.SharedKernel.EntityId</c>) because the resource may be identified outside this
    /// solution's own id scheme (for example an external subject id).</summary>
    public required string ResourceId { get; set; }

    /// <summary>When the audited action happened (caller-supplied, e.g. from
    /// <c>Raffa.SharedKernel.IClock</c>) — not when the row was written, though in practice the
    /// two are the same instant for a synchronous append-only writer.</summary>
    public required DateTimeOffset OccurredAt { get; set; }

    /// <summary>Optional free-form context (no <c>HasMaxLength</c> in
    /// <see cref="Raffa.Audit.Infrastructure.Configurations.AuditEventConfiguration"/>, so this
    /// maps to Postgres `text` — unbounded, matching how this codebase already stores other
    /// unbounded-by-nature audit/correction fields).</summary>
    public string? Detail { get; set; }
}
