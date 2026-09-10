namespace Raffa.Chat.Domain;

/// <summary>
/// The two branches of the Ask Raffa query engine (product spec §8.3 diagram: "Intent
/// detection ├── Structured query (SQL / filters) └── Semantic retrieval"). Every incoming
/// question is assigned exactly one of these before any answer is produced.
/// </summary>
public enum QueryIntent
{
    /// <summary>
    /// Answerable by a deterministic query/filter over validated contract fields (for example
    /// renewal dates, annual spend — <c>Raffa.Documents.Contracts.Domain.Contract</c>). Must
    /// never reach an LLM (Appendix C rule 6: "Prefer deterministic arithmetic/date calculations
    /// to LLM reasoning"). The concrete handlers for this branch are task
    /// E02/F04/US01/T02's scope (deterministic-queries); this router only classifies and forks.
    /// </summary>
    Structured,

    /// <summary>
    /// Needs retrieval over contract sections/clauses and an LLM-grounded answer with citations
    /// (spec §8.3, §8.4 "no evidence, no claim"). Also the safe default when a question does not
    /// clearly match a structured field pattern (Appendix C rule 10: "If data quality is
    /// insufficient, return uncertainty instead of fabricated precision" — an unrecognized
    /// question must not be silently forced through a deterministic filter that does not apply).
    /// The retrieval pipeline for this branch is task E02/F04/US02/T01's scope (rag-citations).
    /// </summary>
    Semantic,
}
