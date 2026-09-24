using Raffa.SharedKernel;
using Raffa.SharedKernel.Market;

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

    /// <summary>The market product name matched, e.g. <c>"Sales Cloud Unlimited"</c> — for a
    /// <see cref="MarketMatchKind.Similar"/> match, the similar product, never the line's own.</summary>
    public string? Product { get; set; }

    /// <summary>Whether <see cref="Product"/> is the line's own product, a bundle of the products it
    /// names, or only a similar product; <see langword="null"/> when nothing was matched (and on
    /// rows written before the kind was recorded, which were all exact matches).</summary>
    public MarketMatchKind? MatchKind { get; set; }

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

    /// <summary>The market corpus fingerprint this row was compared against
    /// (<see cref="IMarketPriceMatcher.GetCorpusVersionAsync"/>): a re-ingested corpus re-prices the
    /// line on its next read.</summary>
    public string? CorpusVersion { get; set; }

    // ---- Estimate: only for a line with no match, never mixed with the matched band above ----

    /// <summary>How the estimate was obtained; <see langword="null"/> when the line has none (it was
    /// matched, or not even an estimate could be made).</summary>
    public MarketEstimateKind? EstimateKind { get; set; }

    public string? EstimateCurrency { get; set; }
    public decimal? EstimateUnitPriceP25 { get; set; }
    public decimal? EstimateUnitPriceP50 { get; set; }
    public decimal? EstimateUnitPriceP75 { get; set; }

    /// <summary>What the estimate rests on, in plain words.</summary>
    public string? EstimateBasis { get; set; }

    /// <summary>The product the estimate is for.</summary>
    public string? EstimateProduct { get; set; }

    /// <summary>When an estimate was last attempted for this line (with or without a result), so a
    /// read does not ask for one again until the corpus or the line changes.</summary>
    public DateTimeOffset? EstimatedAt { get; set; }
}
