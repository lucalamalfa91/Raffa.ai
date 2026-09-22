using Raffa.Chat.Application.Pack;
using Raffa.SharedKernel;

namespace Raffa.Chat.Application.Council;

/// <summary>
/// The market RAG as the market researcher's one tool: a short keyword query in, the closest
/// market notes out, already shaped as <see cref="PackCorpus.Market"/> pack items keyed
/// <c>market:{recordId}</c> (the same key a market deal item carries, so a note and a deal for the
/// same record never appear twice). Implemented by the composition root over the Market module's
/// retrieval — <c>Raffa.Chat</c> depends on neither the Market module nor its index (ADR-002).
/// </summary>
public interface IMarketRagSearch
{
    Task<Result<IReadOnlyList<PackItem>>> SearchAsync(string query, int topK, CancellationToken cancellationToken = default);
}
