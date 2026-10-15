using Raffa.AiGateway.Agents;
using Raffa.Market.Contracts;
using Raffa.Market.Ingestion;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.MarketKnowledge.Ingestion;

/// <summary>Options of the ingest curation (<c>Market:Ingestion:Curation</c>).</summary>
public sealed class MarketCurationOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "Market:Ingestion:Curation";

    /// <summary>Run the data-quality agent and the provenance curator on every ingest batch. Off by
    /// default: the feed is then stored as it is, with no AI call, as before.</summary>
    public bool Enabled { get; set; }

    /// <summary>Complete the billing unit of records that carry none (the provenance curator). On when
    /// curation is enabled; switch off to run the data-quality review alone.</summary>
    public bool CurateProvenance { get; set; } = true;
}

/// <summary>
/// A-11 / F7-T09: the ingest batch's curator. For each batch of changed records it (1) applies the
/// mechanical rules (<see cref="MarketDealRules"/>), (2) runs the data-quality agent over the
/// survivors and drops what it rejects, and (3) runs the provenance curator over the accepted records
/// that lack a billing unit and fills it in from the closed vocabulary. One agent call per step per
/// batch, so the gateway audits one row each. Disabled (the default) it is a pass-through.
/// </summary>
internal sealed class MarketBatchCurator(
    StepRunner runner, MarketCurationOptions options, IClock clock) : IMarketBatchCurator
{
    private readonly MarketDataQualityStep _quality = new();
    private readonly MarketProvenanceCuratorStep _provenance = new();

    public async Task<MarketCurationOutcome> CurateAsync(
        IReadOnlyList<MarketDeal> deals, CancellationToken cancellationToken = default)
    {
        if (!options.Enabled || deals.Count == 0)
        {
            return new MarketCurationOutcome(deals, []);
        }

        var rejected = new List<MarketRejection>();
        var now = clock.UtcNow;

        // 1. Mechanical rules: never negotiable, never sent to a model.
        var candidates = new List<MarketDeal>(deals.Count);
        foreach (var deal in deals)
        {
            var violations = MarketDealRules.Violations(deal, now);
            if (violations.Count > 0)
            {
                rejected.Add(new MarketRejection(deal.RecordId, violations));
            }
            else
            {
                candidates.Add(deal);
            }
        }

        // 2. Data-quality agent over the survivors.
        var accepted = candidates;
        if (candidates.Count > 0)
        {
            var review = await runner
                .RunAsync(_quality, new QualityInput(candidates.Select(MarketIngestWire.ToQuality).ToList()), cancellationToken)
                .ConfigureAwait(false);

            var decisions = review.Value?.Decisions.ToDictionary(d => d.RecordId, StringComparer.Ordinal)
                ?? new Dictionary<string, QualityDecision>(StringComparer.Ordinal);
            accepted = [];
            foreach (var deal in candidates)
            {
                if (decisions.TryGetValue(deal.RecordId, out var decision) && decision.Verdict == QualityVerdict.Reject)
                {
                    rejected.Add(new MarketRejection(deal.RecordId, decision.Reasons));
                }
                else
                {
                    accepted.Add(deal);
                }
            }
        }

        // 3. Provenance curator: only for records with no billing unit.
        if (options.CurateProvenance)
        {
            var lacking = accepted.Where(d => string.IsNullOrWhiteSpace(d.UnitMetric)).ToList();
            if (lacking.Count > 0)
            {
                var curated = await runner
                    .RunAsync(
                        _provenance,
                        new ProvenanceInput(lacking.Select(MarketIngestWire.ToProvenance).ToList(), MarketIngestAgents.AllowedUnits),
                        cancellationToken)
                    .ConfigureAwait(false);

                var units = curated.Value?.Decisions
                    .Where(d => !string.IsNullOrWhiteSpace(d.Unit))
                    .ToDictionary(d => d.RecordId, d => d.Unit!, StringComparer.Ordinal)
                    ?? new Dictionary<string, string>(StringComparer.Ordinal);

                // Never overwrites a unit the feed carries; the verifier already limited units to the list.
                accepted = accepted
                    .Select(d => units.TryGetValue(d.RecordId, out var unit) && string.IsNullOrWhiteSpace(d.UnitMetric)
                        ? d with { UnitMetric = unit }
                        : d)
                    .ToList();
            }
        }

        return new MarketCurationOutcome(accepted, rejected);
    }
}
