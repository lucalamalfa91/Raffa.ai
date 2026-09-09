using Contigo.Documents.Contracts.Domain;

namespace Contigo.Documents.Contracts.Application;

/// <summary>
/// One row of the portfolio list (task E02/F03/US01/T01, us-01-portfolio-list-filters AC-1;
/// product spec §8.1 "Columns": Supplier, Contract, Annual Spend, Start, End, Renewal,
/// Cancellation Deadline, Auto-renewal, Risk, Status).
///
/// Two columns are deliberately proxies for concepts the current schema does not yet carry:
///  - "Supplier" is <see cref="SupplierId"/> only (a bare id, nullable). Suppliers/Products
///    (ADR-002 module map) is still an empty scaffold with no domain types, and
///    <see cref="Contract.SupplierId"/> itself is already a cross-module reference by id only,
///    never a physical FK (ADR-002) — resolving it to a display name is that module's job once
///    it exists.
///  - "Contract" is <see cref="Type"/> (Msa/OrderForm/Amendment/Sow/RenewalLetter/Other) —
///    <see cref="Contract"/> has no title/name field yet; <see cref="Type"/> is the closest
///    identifying information the schema currently records for this column.
///
/// "Renewal" is derived, not stored: <see cref="RenewalDate"/> equals <see cref="EndDate"/> only
/// when <see cref="AutoRenewal"/> is true, otherwise it is null — a contract that does not
/// auto-renew has no next renewal date, only an end date. This mirrors spec §8.3's own example
/// ("Which contracts renew in the next 120 days?" -&gt; structured SQL on validated renewal
/// fields): only auto-renewing contracts answer that question at all.
///
/// "Risk" is <see cref="Risk"/>, the highest <see cref="RiskSeverity"/> across the contract's
/// <c>Risk</c> rows (null when the contract has none) — computed by
/// <see cref="PortfolioQueryService"/>, not stored on <see cref="Contract"/> itself.
///
/// Uses plain <see cref="Guid"/> for ids (not <c>EntityId</c>) for the same reason
/// <c>Contigo.Audit.Infrastructure.AuditEventRecord</c> does: <c>EntityId</c> has no custom JSON
/// converter registered anywhere in this solution, so serializing the wrapper directly would leak
/// it as a nested <c>{"value":"..."}</c> object instead of a plain GUID string.
/// </summary>
/// <param name="Currency">The contract's own ISO currency code (<see cref="Contract.Currency"/>) —
/// the V2 Portfolio screen sums <see cref="AnnualSpend"/> across rows for its summary line
/// ("N validated contracts · CHF 4.2M annual"), which is only meaningful per currency.</param>
public sealed record PortfolioListItem(
    Guid ContractId,
    Guid? SupplierId,
    ContractDocumentType Type,
    decimal? AnnualSpend,
    string Currency,
    DateOnly? StartDate,
    DateOnly? EndDate,
    DateOnly? RenewalDate,
    DateOnly? CancellationDeadline,
    bool AutoRenewal,
    string Status,
    RiskSeverity? Risk);
