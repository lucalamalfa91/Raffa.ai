using Raffa.SharedKernel;

namespace Raffa.Savings.Domain;

/// <summary>
/// One realized-savings record for a <see cref="SavingsOpportunity"/> (task E04/F02/US02/T02,
/// realized-savings; parent story us-02-savings-opportunity AC-3 "Realized value is captured and
/// audit-tracked (App C #9)"; module-map.md "Savings | SavingsOpportunity, RealizedSavings |
/// /api/savings" — this module's second named entity). Written only by
/// <see cref="Application.SavingsOpportunityService.UpdateAsync"/> (`PATCH /api/savings/{id}`,
/// Appendix A "Status/owner/realized value"), closing the gap
/// <see cref="SavingsOpportunityStatus.Realized"/>'s own doc comment names.
///
/// <para>
/// Append-only, the same "never destructively overwrite" convention (Appendix C rule 5's spirit)
/// <c>Raffa.Documents.Contracts.Domain.ContractVersion</c>/<c>CorrectionHistory</c> already apply
/// to their own captured history: recording a realized value always inserts a new row here, never
/// updates or deletes a previous one, so a later correction to a realized figure is its own new
/// row and the original capture stays reachable. Unlike <c>Raffa.Audit</c>'s own append-only
/// enforcement, this is an application-level discipline only (no database trigger) — the same
/// lighter-weight convention <c>ContractVersion</c>/<c>CorrectionHistory</c> themselves rely on
/// (neither has a DB-level append-only trigger either), not every append-only table in this
/// codebase needs the stronger guarantee <c>Raffa.Audit</c>'s own compliance role demands.
/// </para>
/// </summary>
public sealed class RealizedSavings : TenantScopedEntity
{
    /// <summary>The <see cref="SavingsOpportunity"/> this realized value belongs to. Always an id
    /// that exists for this tenant — <see cref="Application.SavingsOpportunityService.UpdateAsync"/>
    /// is the only writer, and it loads the opportunity by (tenant, id) before ever creating this
    /// row. Unlike <see cref="SavingsOpportunity.SupplierId"/>/<see cref="SavingsOpportunity.ContractId"/>
    /// (deliberately no FK — a genuine cross-module reference this module cannot validate, ADR-002),
    /// this is a same-module reference; kept as a plain indexed id column rather than a real FK
    /// anyway, matching this module's own "no cross-aggregate FK" convention rather than
    /// introducing the one exception.</summary>
    public required EntityId SavingsOpportunityId { get; set; }

    /// <summary>The realized amount — always <c>&gt;= 0</c>
    /// (<see cref="Application.SavingsOpportunityService.RealizedAmountMustBeNonNegativeError"/>),
    /// in <see cref="Currency"/>. Never its own independently-supplied currency: a realized value is
    /// always denominated in the same currency as the opportunity it realizes (this codebase has no
    /// currency-conversion service anywhere — same reasoning <see cref="SavingsOpportunity.Currency"/>'s
    /// own doc comment gives), so accepting a separate caller-supplied currency here would invite a
    /// mismatch this module cannot reconcile.</summary>
    public required decimal Amount { get; set; }

    /// <summary>Copied from the parent <see cref="SavingsOpportunity.Currency"/> at the moment this
    /// row is written — denormalized, not a join, so an already-realized historical figure is never
    /// reinterpreted by a later, unrelated change on the parent. ISO 4217, same convention as
    /// <see cref="SavingsOpportunity.Currency"/>.</summary>
    public required string Currency { get; set; }

    /// <summary>When this realized value was recorded (caller-supplied via <c>IClock</c>, not a
    /// database default) — same "no hidden clock" convention every other timestamped write in this
    /// codebase follows. Deliberately independent of <see cref="SavingsOpportunity.UpdatedAt"/>,
    /// which can move again later for an unrelated owner-only patch.</summary>
    public required DateTimeOffset RealizedAt { get; set; }
}
