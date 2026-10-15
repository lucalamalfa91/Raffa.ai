using System.Text.Json;
using Raffa.Documents.Contracts.Application.Extraction;

namespace Raffa.AiFlows.DocumentExtraction.Orchestration;

/// <summary>
/// F5-T04 (reduce step): merges the typed facts one stage produced over the chunks of a long document into
/// the single payload the stage would have returned for the whole text. Scalar stages keep, per field, the
/// non-empty reading with the highest confidence (the earliest chunk wins a tie); list stages concatenate in
/// chunk order and drop exact duplicates. Page numbers and spans are untouched, so they still verify.
/// </summary>
internal static class StageFactsMerger
{
    public static StageFacts Merge(IReadOnlyList<StageFacts> parts)
    {
        if (parts.Count == 0)
        {
            throw new ArgumentException("Nothing to merge.", nameof(parts));
        }

        return parts[0] switch
        {
            StageFacts.Scalars first => new StageFacts.Scalars(first.Stage, MergeScalars(parts.Cast<StageFacts.Scalars>())),
            StageFacts.LineItems => new StageFacts.LineItems(Concat(parts.Cast<StageFacts.LineItems>().Select(p => p.Items))),
            StageFacts.Clauses => new StageFacts.Clauses(Concat(parts.Cast<StageFacts.Clauses>().Select(p => p.Items))),
            StageFacts.Obligations => new StageFacts.Obligations(Concat(parts.Cast<StageFacts.Obligations>().Select(p => p.Items))),
            StageFacts.Risks => new StageFacts.Risks(Concat(parts.Cast<StageFacts.Risks>().Select(p => p.Items))),
            _ => parts[0],
        };
    }

    /// <summary>The payload JSON <c>IExtractionStage.Parse</c> reads back to the same facts.</summary>
    public static string ToPayloadJson(StageFacts facts) => facts switch
    {
        StageFacts.Scalars s => JsonSerializer.Serialize(new ExtractedFactsPayload(s.Facts)),
        StageFacts.LineItems l => JsonSerializer.Serialize(new ExtractedLineItemsPayload(l.Items)),
        StageFacts.Clauses c => JsonSerializer.Serialize(new ExtractedClausesPayload(c.Items)),
        StageFacts.Obligations o => JsonSerializer.Serialize(new ExtractedObligationsPayload(o.Items)),
        StageFacts.Risks r => JsonSerializer.Serialize(new ExtractedRisksPayload(r.Items)),
        _ => throw new NotSupportedException(facts.GetType().Name),
    };

    private static List<ExtractedFact> MergeScalars(IEnumerable<StageFacts.Scalars> parts)
    {
        var order = new List<string>();
        var best = new Dictionary<string, ExtractedFact>(StringComparer.OrdinalIgnoreCase);
        var unnamed = new List<ExtractedFact>();

        foreach (var fact in parts.SelectMany(p => p.Facts))
        {
            if (string.IsNullOrWhiteSpace(fact.Field))
            {
                unnamed.Add(fact);
                continue;
            }

            if (!best.TryGetValue(fact.Field, out var current))
            {
                order.Add(fact.Field);
                best[fact.Field] = fact;
            }
            else if (Better(fact, current))
            {
                best[fact.Field] = fact;
            }
        }

        return order.Select(f => best[f]).Concat(unnamed).ToList();
    }

    private static bool Better(ExtractedFact candidate, ExtractedFact current)
    {
        var candidateHasValue = !string.IsNullOrWhiteSpace(candidate.Value);
        var currentHasValue = !string.IsNullOrWhiteSpace(current.Value);
        if (candidateHasValue != currentHasValue)
        {
            return candidateHasValue;
        }

        return candidateHasValue && (candidate.Confidence ?? 0) > (current.Confidence ?? 0);
    }

    private static List<T> Concat<T>(IEnumerable<IReadOnlyList<T>> parts) =>
        parts.SelectMany(p => p).Distinct().ToList();
}
