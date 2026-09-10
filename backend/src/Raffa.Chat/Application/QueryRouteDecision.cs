using Raffa.Chat.Domain;

namespace Raffa.Chat.Application;

/// <summary>
/// The outcome of <see cref="AskRaffaQueryRouter.Route"/>: which of the two spec §8.3 branches
/// a question was assigned to, and why — so a caller (or a test) never has to re-derive the
/// classification reason from the raw question text.
/// </summary>
/// <param name="Question">The question as routed (trimmed).</param>
/// <param name="Intent">The assigned branch.</param>
/// <param name="Reason">Human-readable explanation of which signal drove the classification —
/// useful for debugging a misroute and for asserting *why* in tests, not just the outcome.</param>
public sealed record QueryRouteDecision(string Question, QueryIntent Intent, string Reason)
{
    /// <summary>
    /// True when this question must be answered by a deterministic query/filter and must never
    /// reach an LLM (parent story us-01-query-router AC-2). The concrete handlers are task
    /// E02/F04/US01/T02's scope.
    /// </summary>
    public bool RequiresDeterministicQuery => Intent == QueryIntent.Structured;

    /// <summary>
    /// True when this question must be answered by RAG retrieval over contract sections/clauses
    /// (parent story us-01-query-router AC-3). The retrieval pipeline is task
    /// E02/F04/US02/T01's scope.
    /// </summary>
    public bool RequiresRagRetrieval => Intent == QueryIntent.Semantic;
}
