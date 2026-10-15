using Raffa.AiGateway.Agents;
using Raffa.Market.Contracts;

namespace Raffa.AiFlows.MarketKnowledge.Ingestion;

// The ingest batch's two agents as runner steps (plan A-11, F7-T09), mirroring Negotiation/Agents/
// CouncilSteps.cs: wire records, a definition per agent (prompt, version, strict schema, registered
// hash so a prompt edited without a version bump fails Validate() in a test) and one step class each.
// Both fall back to the deterministic behaviour when the model is unreachable, so an ingest never
// stalls on, and never silently stores around, a failed review.

internal enum QualityVerdict
{
    Accept,
    Reject,
}

internal sealed record QualityRecord(
    string RecordId,
    string Supplier,
    string Product,
    string Category,
    string Geography,
    string Currency,
    int TermMonths,
    decimal UnitPriceP25,
    decimal UnitPriceP50,
    decimal UnitPriceP75,
    int SampleSize,
    double? DiscountPct,
    double? UpliftCapPct,
    int? NoticeDays);

internal sealed record QualityInput(IReadOnlyList<QualityRecord> Records);

internal sealed record QualityDecision(string RecordId, QualityVerdict Verdict, IReadOnlyList<string> Reasons);

internal sealed record QualityPayload(IReadOnlyList<QualityDecision> Decisions);

internal sealed record ProvenanceRecord(
    string RecordId,
    string Supplier,
    string Product,
    string Category,
    string Source,
    int SampleSize);

internal sealed record ProvenanceInput(IReadOnlyList<ProvenanceRecord> Records, IReadOnlyList<string> AllowedUnits);

internal sealed record ProvenanceDecision(string RecordId, string? Unit);

internal sealed record ProvenancePayload(IReadOnlyList<ProvenanceDecision> Decisions);

internal static class MarketIngestStepDefinitions
{
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(90);

    // SHA-256 of each prompt (AgentDefinition.HashPrompt), registered next to the version.
    internal const string DataQualityHash = "43eec4d6f13edc454eab6798cce2abedf12fbb3fddf2bf971074589559025253";
    internal const string ProvenanceCuratorHash = "5d06d3e00bb94d70c5c02ebadff60fc400e2a9d966b8db7b228fcc2cee5ad7f6";

    public static AgentDefinition DataQuality(TimeSpan? deadline = null) =>
        AgentDefinition.ForOutput<QualityPayload>(
            MarketIngestAgents.DataQualityName,
            MarketIngestAgents.Version,
            MarketIngestAgents.DataQualityPrompt,
            deadline ?? Deadline,
            AgentFailurePolicy.Fallback,
            registeredPromptHash: DataQualityHash);

    public static AgentDefinition ProvenanceCurator(TimeSpan? deadline = null) =>
        AgentDefinition.ForOutput<ProvenancePayload>(
            MarketIngestAgents.ProvenanceCuratorName,
            MarketIngestAgents.Version,
            MarketIngestAgents.ProvenanceCuratorPrompt,
            deadline ?? Deadline,
            AgentFailurePolicy.Fallback,
            registeredPromptHash: ProvenanceCuratorHash);
}

/// <summary>Data-quality review of one batch (F7-T09). Verifier: exactly one decision per input record,
/// none unknown, and every rejection carries a reason. Fallback: accept all (the mechanical rules ran first).</summary>
internal sealed class MarketDataQualityStep(TimeSpan? deadline = null) : IAgentStep<QualityInput, QualityPayload>
{
    public AgentDefinition Definition { get; } = MarketIngestStepDefinitions.DataQuality(deadline);

    public IStepVerifier<QualityInput, QualityPayload>? Verifier { get; } = new QualityVerifier();

    public QualityPayload? Fallback(QualityInput input) =>
        new(input.Records.Select(r => new QualityDecision(r.RecordId, QualityVerdict.Accept, [])).ToList());

    private sealed class QualityVerifier : IStepVerifier<QualityInput, QualityPayload>
    {
        public string Name => "market-quality-coverage";

        public StepVerdict Verify(QualityInput input, QualityPayload output)
        {
            var violations = new List<string>();
            var expected = input.Records.Select(r => r.RecordId).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var decision in output.Decisions)
            {
                if (!expected.Contains(decision.RecordId))
                {
                    violations.Add($"'{decision.RecordId}' is not an input record.");
                }
                else if (!seen.Add(decision.RecordId))
                {
                    violations.Add($"'{decision.RecordId}' has more than one decision.");
                }

                if (decision.Verdict == QualityVerdict.Reject && decision.Reasons.Count == 0)
                {
                    violations.Add($"The rejection of '{decision.RecordId}' has no reason.");
                }
            }

            foreach (var missing in expected.Except(seen))
            {
                violations.Add($"'{missing}' has no decision.");
            }

            return violations.Count == 0 ? StepVerdict.Pass : StepVerdict.Reject(violations.ToArray());
        }
    }
}

/// <summary>Billing-unit curation for the records of one batch that carry none. Verifier: only input
/// ids, at most once, and a unit only from the closed list. Fallback: no unit for anyone.</summary>
internal sealed class MarketProvenanceCuratorStep(TimeSpan? deadline = null) : IAgentStep<ProvenanceInput, ProvenancePayload>
{
    public AgentDefinition Definition { get; } = MarketIngestStepDefinitions.ProvenanceCurator(deadline);

    public IStepVerifier<ProvenanceInput, ProvenancePayload>? Verifier { get; } = new UnitVerifier();

    public ProvenancePayload? Fallback(ProvenanceInput input) =>
        new(input.Records.Select(r => new ProvenanceDecision(r.RecordId, null)).ToList());

    private sealed class UnitVerifier : IStepVerifier<ProvenanceInput, ProvenancePayload>
    {
        public string Name => "market-provenance-unit";

        public StepVerdict Verify(ProvenanceInput input, ProvenancePayload output)
        {
            var violations = new List<string>();
            var expected = input.Records.Select(r => r.RecordId).ToHashSet(StringComparer.Ordinal);
            var allowed = input.AllowedUnits.ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var decision in output.Decisions)
            {
                if (!expected.Contains(decision.RecordId))
                {
                    violations.Add($"'{decision.RecordId}' is not an input record.");
                }
                else if (!seen.Add(decision.RecordId))
                {
                    violations.Add($"'{decision.RecordId}' has more than one decision.");
                }

                if (decision.Unit is { } unit && !allowed.Contains(unit))
                {
                    violations.Add($"'{unit}' is not an allowed unit.");
                }
            }

            return violations.Count == 0 ? StepVerdict.Pass : StepVerdict.Reject(violations.ToArray());
        }
    }
}

/// <summary>Maps the deals of a batch onto the wire records the agents read.</summary>
internal static class MarketIngestWire
{
    public static QualityRecord ToQuality(MarketDeal d) => new(
        d.RecordId, d.Supplier, d.Product, d.Category, d.Geography, d.Currency, d.TermMonths,
        d.UnitPriceP25, d.UnitPriceP50, d.UnitPriceP75, d.SampleSize,
        d.DiscountAchievedPct, d.UpliftCapPct, d.NoticeDays);

    public static ProvenanceRecord ToProvenance(MarketDeal d) => new(
        d.RecordId, d.Supplier, d.Product, d.Category, d.Source, d.SampleSize);
}
