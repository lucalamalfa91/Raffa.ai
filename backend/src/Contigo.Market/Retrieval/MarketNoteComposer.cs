using System.Globalization;
using System.Text;
using Contigo.Market.Contracts;

namespace Contigo.Market.Retrieval;

/// <summary>
/// Turns one <see cref="MarketDeal"/> into one <see cref="MarketNote"/> narrative (R-MKT-03
/// Projection 2: "one narrative per record"), e.g. "Companies of 500-2000 employees closing
/// Salesforce Sales Cloud Enterprise in CH in 2026-Q1 paid P50 CHF 132 (P25 118-P75 149), achieved
/// an 8% discount, obtained a 4% uplift cap and 90-day notice…" — the same flavour requirements.md
/// R-MKT-03 itself illustrates. T02's ingestion job is expected to call this once per
/// <see cref="MarketDeal"/> and embed the resulting <see cref="MarketNote.Snippet"/>
/// (ADR-004 `embed` role) into the shared `market_embedding` index; this task's own
/// <see cref="InMemoryMarketKnowledgeRetrieval"/> calls it directly, in memory, with no index at all.
///
/// A composed note has no query to score against — <see cref="MarketNote.Score"/> is always
/// <c>0</c> here. <see cref="IMarketKnowledgeRetrieval.SearchAsync"/> is what assigns a real,
/// per-query score, using this method's output as the query-independent starting point
/// (typically via <c>note with { Score = ... }</c>).
/// </summary>
public static class MarketNoteComposer
{
    /// <summary>Builds the <see cref="MarketNote"/> for <paramref name="deal"/> — every field
    /// except <see cref="MarketNote.Score"/> (see the type-level doc comment).</summary>
    public static MarketNote Compose(MarketDeal deal)
    {
        ArgumentNullException.ThrowIfNull(deal);

        return new MarketNote(
            RecordId: deal.RecordId,
            Title: BuildTitle(deal),
            Snippet: BuildNarrative(deal),
            Category: deal.Category,
            Geography: deal.Geography,
            UpdatedAt: deal.UpdatedAt,
            Provenance: MarketProvenance.Label(deal),
            Score: 0d);
    }

    private static string BuildTitle(MarketDeal deal) =>
        deal.Sku is null
            ? $"{deal.Supplier} · {deal.Product}"
            : $"{deal.Supplier} · {deal.Product} ({deal.Sku})";

    private static string BuildNarrative(MarketDeal deal)
    {
        var narrative = new StringBuilder();

        narrative.Append(CultureInfo.InvariantCulture, $"Companies of {deal.CompanySizeBand} " +
            $"employees closing {deal.Supplier} {deal.Product} in {deal.Geography} in " +
            $"{deal.ClosingPeriod} paid P50 {deal.Currency} {FormatPrice(deal.UnitPriceP50)} " +
            $"(P25 {FormatPrice(deal.UnitPriceP25)}-P75 {FormatPrice(deal.UnitPriceP75)})");

        if (deal.DiscountAchievedPct is { } discount)
        {
            narrative.Append(CultureInfo.InvariantCulture, $", achieved a {FormatPercent(discount)} discount");
        }

        if (deal.UpliftCapPct is { } uplift)
        {
            narrative.Append(CultureInfo.InvariantCulture, $", obtained a {FormatPercent(uplift)} uplift cap");
        }

        if (deal.NoticeDays is { } notice)
        {
            narrative.Append(CultureInfo.InvariantCulture, $" and {notice}-day notice");
        }

        if (deal.PaymentTerms is { } paymentTerms)
        {
            narrative.Append(CultureInfo.InvariantCulture, $" ({paymentTerms})");
        }

        narrative.Append('.');

        if (deal.NegotiatedClauses.Count > 0)
        {
            var clauses = string.Join("; ", deal.NegotiatedClauses.Select(c => $"{c.Type} — {c.Value}"));
            narrative.Append(CultureInfo.InvariantCulture, $" Negotiated clauses: {clauses}.");
        }

        return narrative.ToString();
    }

    private static string FormatPrice(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string FormatPercent(double value) => $"{value.ToString("0.#", CultureInfo.InvariantCulture)}%";
}
