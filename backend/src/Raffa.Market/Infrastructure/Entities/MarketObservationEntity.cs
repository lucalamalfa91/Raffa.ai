namespace Raffa.Market.Infrastructure.Entities;

/// <summary>
/// One anonymised observation from a customer's supplier reply: the supplier and the terms it offered. The
/// table carries no <c>tenant_id</c> on purpose (like <c>market_record</c>: the shared market store is never
/// a tenant row), so nothing here names a customer, a case or a person. <see cref="WorkspaceKey"/> is a
/// one-way key, used only to count how many different workspaces an observation group rests on.
/// </summary>
public sealed class MarketObservationEntity
{
    public required Guid Id { get; set; }

    /// <summary>One-way key of the contributing workspace (HMAC with a server secret): counts distinct contributors.</summary>
    public required string WorkspaceKey { get; set; }

    /// <summary>One-way dedupe key of what it came from: the same reply recorded twice is one observation.</summary>
    public required string ObservationKey { get; set; }

    /// <summary>The supplier, lower-cased and trimmed: the grouping key.</summary>
    public required string SupplierKey { get; set; }

    public required string Supplier { get; set; }

    public string? Category { get; set; }

    public string? Geography { get; set; }

    public string? Currency { get; set; }

    /// <summary>The product the offered price is for (the contract's main line): the deal's grouping key with the supplier and term.</summary>
    public string? Product { get; set; }

    public string? Sku { get; set; }

    /// <summary>What the unit price is per ("user / month").</summary>
    public string? Unit { get; set; }

    /// <summary>The unit price the supplier offered (a benchmark only exists with a price).</summary>
    public decimal? UnitPrice { get; set; }

    public int? TermMonths { get; set; }

    public double? DiscountPct { get; set; }

    public double? UpliftCapPct { get; set; }

    public int? NoticeDays { get; set; }

    public string? PaymentTerms { get; set; }

    public required string Source { get; set; }

    public required DateTimeOffset ObservedAt { get; set; }
}
