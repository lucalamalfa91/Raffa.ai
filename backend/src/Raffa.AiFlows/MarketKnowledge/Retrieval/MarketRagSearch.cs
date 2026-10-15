using System.Globalization;
using Raffa.AiFlows.Negotiation.Options;
using Raffa.AiFlows.Negotiation.Tools;
using Raffa.AiFlows.Shared.Pack;
using Raffa.Insights.Contracts;
using Raffa.Market.Contracts;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel;

namespace Raffa.AiFlows.MarketKnowledge.Retrieval;

/// <summary>
/// The market researcher's tool (<see cref="IMarketRagSearch"/>) over the Market module's RAG
/// (<see cref="IMarketKnowledgeRetrieval"/>: the pgvector index in a deployed host, the in-memory
/// scorer otherwise). Each note becomes a <see cref="PackCorpus.Market"/> item keyed
/// <c>market:{recordId}</c> — the same key a market deal item for that record carries. With
/// <see cref="CouncilOptions.MarketNoteValues"/> (F2-T10, off by default) a note also carries its
/// structured provenance facts as pack values: the sample size and the as-of date.
/// </summary>
internal sealed class MarketRagSearch(IMarketKnowledgeRetrieval retrieval, CouncilOptions? options = null) : IMarketRagSearch
{
    internal const string SampleSizeKey = "sampleSize";
    internal const string AsOfKey = "asOf";

    public async Task<Result<IReadOnlyList<PackItem>>> SearchAsync(string query, int topK, CancellationToken cancellationToken = default)
    {
        var notes = await retrieval.SearchAsync(query, topK, null, cancellationToken).ConfigureAwait(false);
        if (notes.IsFailure)
        {
            return Result<IReadOnlyList<PackItem>>.Failure(notes.Error);
        }

        IReadOnlyList<PackItem> items = notes.Value
            .Select(note => new PackItem(
                InsightsCitationKeys.Market(note.RecordId),
                PackCorpus.Market,
                note.Title,
                note.Provenance,
                null,
                null,
                note.Snippet,
                null,
                null,
                note.RecordId,
                note.Provenance,
                ValuesOf(note)))
            .ToList();

        return Result<IReadOnlyList<PackItem>>.Success(items);
    }

    /// <summary>The note's structured facts, only when <see cref="CouncilOptions.MarketNoteValues"/> is on
    /// and the note carries its provenance: the sample the figures rest on and the date they are as of.</summary>
    private IReadOnlyList<PackValue> ValuesOf(MarketNote note)
    {
        if (options is not { MarketNoteValues: true } || note.ProvenanceInfo is not { } info)
        {
            return [];
        }

        var values = new List<PackValue>(2);
        if (info.N > 0)
        {
            values.Add(new PackValue(SampleSizeKey, info.N.ToString(CultureInfo.InvariantCulture), PackValueKind.Number));
        }

        values.Add(new PackValue(AsOfKey, info.AsOf.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PackValueKind.Date));
        return values;
    }
}
