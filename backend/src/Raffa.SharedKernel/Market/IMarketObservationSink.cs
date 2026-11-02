using Raffa.SharedKernel.Tenancy;

namespace Raffa.SharedKernel.Market;

/// <summary>
/// What a supplier's reply stated, stripped of everything that identifies the customer: only the facts a
/// benchmark needs (the supplier, the term and the commercial terms it offered). Never a name, an email, a
/// price in absolute terms or free text of the reply.
/// </summary>
public sealed record SupplierReplyObservation(
    string Supplier,
    string? Category,
    string? Geography,
    string? Currency,
    string? Product,
    string? Sku,
    string? Unit,
    decimal? UnitPrice,
    int? TermMonths,
    double? DiscountPct,
    double? UpliftCapPct,
    int? NoticeDays,
    string? PaymentTerms,
    DateTimeOffset ObservedAt)
{
    /// <summary>True when the reply stated at least one commercial term worth keeping.</summary>
    public bool HasTerms => UnitPrice is > 0 || DiscountPct is not null || UpliftCapPct is not null || NoticeDays is not null || PaymentTerms is not null;
}

/// <summary>
/// The invisible side of the renewal case: what a supplier answered feeds Raffa's own benchmark. The
/// implementation anonymises, stores in the shared market store and writes an audit row so the flow can be
/// monitored; the case never waits for it and never fails because of it.
/// </summary>
public interface IMarketObservationSink
{
    /// <param name="tenantId">The workspace the reply belongs to: used for the audit row and a one-way key, never stored.</param>
    /// <param name="correlationId">What the observation came from (the case id and reply): hashed into a dedupe key, never stored.</param>
    Task RecordAsync(
        TenantId tenantId, string actor, string correlationId, SupplierReplyObservation observation, CancellationToken cancellationToken = default);
}

/// <summary>The default: a host without the market store records nothing.</summary>
public sealed class NoMarketObservationSink : IMarketObservationSink
{
    public Task RecordAsync(
        TenantId tenantId, string actor, string correlationId, SupplierReplyObservation observation, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
