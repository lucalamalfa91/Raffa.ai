using Raffa.SharedKernel;

namespace Raffa.Documents.Contracts.Domain;

/// <summary>
/// The market price a <see cref="ContractLineItem"/> was last compared with — one row per line
/// item, written when the document is extracted and rewritten whenever it goes stale
/// (<see cref="Application.LineItemMarketPriceService"/>), so Contract 360's Market / vs market
/// columns read a stored, dated comparison instead of an em dash.
///
/// <para>
/// A row with <see cref="RecordId"/> <see langword="null"/> is a <b>checked, no-match</b> result
/// (no comparable market record for the line) — distinct from "never checked", which is the
/// absence of a row. Every figure carries the provenance ADR-001 w17 clause 4 requires: the
/// market record it came from, its sample size, its region/term and when the market data itself
/// was last refreshed.
/// </para>
/// </summary>
public sealed class ContractLineItemMarketPrice : TenantScopedEntity
{
    /// <summary>The priced line (unique per tenant).</summary>
    public required EntityId LineItemId { get; set; }

    /// <summary>The line's contract, so one contract's prices load in one query.</summary>
    public required EntityId ContractId { get; set; }

    /// <summary>The market record matched, or <see langword="null"/> when none was comparable.</summary>
    public string? RecordId { get; set; }

    /// <summary>The market product name matched, e.g. <c>"Sales Cloud Unlimited"</c>.</summary>
    public string? Product { get; set; }

    public string? Geography { get; set; }
    public string? Currency { get; set; }
    public int? TermMonths { get; set; }

    public decimal? UnitPriceP25 { get; set; }

    /// <summary>The median — the "market price" the line's own unit price is compared with.</summary>
    public decimal? UnitPriceP50 { get; set; }

    public decimal? UnitPriceP75 { get; set; }
    public int? SampleSize { get; set; }

    /// <summary>The corpus's own provenance label (e.g. "representative market data · mock feed · updated 2026-07-01").</summary>
    public string? Provenance { get; set; }

    /// <summary>When the market record itself was last refreshed.</summary>
    public DateTimeOffset? MarketUpdatedAt { get; set; }

    /// <summary>When this line was last compared with the market (match or no match).</summary>
    public required DateTimeOffset CheckedAt { get; set; }
}
