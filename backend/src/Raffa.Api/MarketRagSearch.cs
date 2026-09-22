using Raffa.Chat.Application.Council;
using Raffa.Chat.Application.Pack;
using Raffa.Insights.Contracts;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary>
/// The market researcher's tool (<see cref="IMarketRagSearch"/>) over the Market module's RAG
/// (<see cref="IMarketKnowledgeRetrieval"/>: the pgvector index in a deployed host, the in-memory
/// scorer otherwise). Each note becomes a <see cref="PackCorpus.Market"/> item keyed
/// <c>market:{recordId}</c> — the same key a market deal item for that record carries.
/// </summary>
internal sealed class MarketRagSearch(IMarketKnowledgeRetrieval retrieval) : IMarketRagSearch
{
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
                []))
            .ToList();

        return Result<IReadOnlyList<PackItem>>.Success(items);
    }
}
