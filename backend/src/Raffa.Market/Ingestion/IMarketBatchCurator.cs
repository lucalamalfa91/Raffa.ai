using Raffa.Market.Contracts;

namespace Raffa.Market.Ingestion;

/// <summary>
/// A-11 / F7-T09: the seam the ingestion job calls on every batch of changed records, before they are
/// embedded and stored. The implementation (<c>Raffa.AiFlows.MarketKnowledge.Ingestion</c>) validates
/// the records (data quality) and completes their provenance; this module only knows the contract, so
/// it never depends on the AI flows layer. When no curator is registered the job stores the feed as it
/// is, as before.
/// </summary>
public interface IMarketBatchCurator
{
    /// <summary>
    /// Curates one batch. The returned <see cref="MarketCurationOutcome.Accepted"/> deals are the ones to
    /// store (possibly completed), in input order; every input record is either accepted or listed in
    /// <see cref="MarketCurationOutcome.Rejected"/>.
    /// </summary>
    Task<MarketCurationOutcome> CurateAsync(IReadOnlyList<MarketDeal> deals, CancellationToken cancellationToken = default);
}

/// <summary>The result of curating one batch.</summary>
/// <param name="Accepted">The records to store, in input order.</param>
/// <param name="Rejected">The records that were kept out, each with its reasons.</param>
public sealed record MarketCurationOutcome(
    IReadOnlyList<MarketDeal> Accepted,
    IReadOnlyList<MarketRejection> Rejected);

/// <summary>A record the curation kept out of the corpus.</summary>
/// <param name="RecordId">The rejected record.</param>
/// <param name="Reasons">Why (rule codes or the data-quality agent's reasons).</param>
public sealed record MarketRejection(string RecordId, IReadOnlyList<string> Reasons);

/// <summary>
/// F7-T09: the deterministic, always-on part of the data-quality check -- the rules a record must
/// satisfy whatever a model thinks of it. Pure, so the same function guards the ingest, the agent's
/// verifier and the agent's fallback.
/// </summary>
public static class MarketDealRules
{
    /// <summary>The reasons <paramref name="deal"/> violates a hard rule; empty when it satisfies all of them.</summary>
    public static IReadOnlyList<string> Violations(MarketDeal deal, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(deal);

        var reasons = new List<string>();

        if (string.IsNullOrWhiteSpace(deal.RecordId))
        {
            reasons.Add("missing-record-id");
        }

        if (string.IsNullOrWhiteSpace(deal.Supplier))
        {
            reasons.Add("missing-supplier");
        }

        if (string.IsNullOrWhiteSpace(deal.Product))
        {
            reasons.Add("missing-product");
        }

        if (string.IsNullOrWhiteSpace(deal.Category))
        {
            reasons.Add("missing-category");
        }

        if (string.IsNullOrWhiteSpace(deal.Geography))
        {
            reasons.Add("missing-geography");
        }

        if (deal.Currency is not { Length: 3 } currency || !currency.All(char.IsAsciiLetter))
        {
            reasons.Add("invalid-currency");
        }

        if (deal.UnitPriceP25 <= 0 || deal.UnitPriceP50 <= 0 || deal.UnitPriceP75 <= 0)
        {
            reasons.Add("non-positive-price");
        }

        if (deal.UnitPriceP25 > deal.UnitPriceP50 || deal.UnitPriceP50 > deal.UnitPriceP75)
        {
            reasons.Add("percentiles-out-of-order");
        }

        if (deal.SampleSize < 1)
        {
            reasons.Add("empty-sample");
        }

        if (deal.TermMonths is < 1 or > 120)
        {
            reasons.Add("term-out-of-range");
        }

        if (deal.UpdatedAt > now.AddDays(1))
        {
            reasons.Add("updated-in-the-future");
        }

        if (deal.DiscountAchievedPct is < 0 or > 100)
        {
            reasons.Add("discount-out-of-range");
        }

        if (deal.UpliftCapPct is < 0 or > 100)
        {
            reasons.Add("uplift-out-of-range");
        }

        if (deal.NoticeDays is < 0 or > 730)
        {
            reasons.Add("notice-out-of-range");
        }

        return reasons;
    }
}
