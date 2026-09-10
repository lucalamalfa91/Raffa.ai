using System.Text.RegularExpressions;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Application;

/// <summary>
/// Structured-vs-semantic query intent router for Ask Raffa (product spec §8.3; task
/// E02/F04/US01/T01; parent story us-01-query-router).
///
/// <para>
/// Classifies a natural-language question so a caller can fork execution the way spec §8.3's
/// diagram requires: "Intent detection ├── Structured query (SQL / filters) └── Semantic
/// retrieval (contract sections / clauses)". The fork itself — <see cref="QueryRouteDecision"/> —
/// is the "structured query path" this task establishes: <see cref="QueryIntent.Structured"/>
/// questions are marked to go straight to a deterministic handler (task E02/F04/US01/T02), never
/// to an LLM; <see cref="QueryIntent.Semantic"/> questions are marked for RAG retrieval (task
/// E02/F04/US02/T01).
/// </para>
///
/// <para>
/// The classification itself is a fixed keyword/pattern match over the question text — free,
/// synchronous and 100% reproducible, not an LLM call (Appendix C rule 6: "Prefer deterministic
/// arithmetic/date calculations to LLM reasoning", generalized here to intent detection so the
/// fork decision is itself deterministic). Accordingly this class takes no dependency on
/// <c>Raffa.AiGateway</c> at all — see <c>AskRaffaQueryRouterTests
/// .Router_has_no_dependency_on_the_AI_Gateway</c> for the structural proof.
/// </para>
///
/// <para>
/// Legal/clause vocabulary is checked before structured field vocabulary: a question that names
/// clause-level content (liability, indemnification, ...) must never be misrouted to a
/// deterministic field lookup that does not carry that data. A question that matches neither
/// list defaults to <see cref="QueryIntent.Semantic"/> — see that member's doc comment for why
/// (Appendix C rule 10).
/// </para>
/// </summary>
public sealed class AskRaffaQueryRouter
{
    // Legal/clause vocabulary (Raffa.Documents.Contracts.Domain.Clause: ClauseType, RawText —
    // free-text content a deterministic field filter cannot answer). Checked first: spec §8.3's
    // own examples ("What liability do we have with AWS?", "Which contracts contain unlimited
    // liability?") both need clause retrieval, not a field filter.
    private static readonly string[] SemanticKeywords =
    [
        "liability", "liable", "indemnif", "warrant", "confidential", "clause",
        "obligation", "unlimited", "terminat", "breach", "damages",
    ];

    // Validated Contract fields (Raffa.Documents.Contracts.Domain.Contract: StartDate,
    // EndDate, CancellationDeadline, AnnualSpend, TotalContractValue, ...) that a deterministic
    // filter/aggregation can answer without any LLM (Appendix C rule 6).
    private static readonly string[] StructuredKeywords =
    [
        "renew", "expir", "cancellation deadline", "annual spend", "total contract value",
        "tcv", "spend", "how much", "start date", "end date",
    ];

    // Relative date-window phrasing (spec §8.3: "Which contracts renew in the next 120 days?")
    // that the keyword list above would otherwise miss when it is not paired with "renew".
    private static readonly Regex NextNDaysPattern = new(
        @"next\s+\d+\s+(day|days|month|months|year|years)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Classifies <paramref name="question"/> and returns the routing decision. Pure and
    /// synchronous — no I/O, no LLM call, always the same answer for the same input.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="question"/> is empty or whitespace-only.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="question"/> is null.</exception>
    public QueryRouteDecision Route(string question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var trimmed = question.Trim();

        var semanticMatch = FirstMatch(trimmed, SemanticKeywords);
        if (semanticMatch is not null)
        {
            return new QueryRouteDecision(
                trimmed,
                QueryIntent.Semantic,
                $"matched legal/clause keyword '{semanticMatch}' — needs clause retrieval, not a deterministic field filter.");
        }

        var structuredMatch = FirstMatch(trimmed, StructuredKeywords);
        if (structuredMatch is not null)
        {
            return new QueryRouteDecision(
                trimmed,
                QueryIntent.Structured,
                $"matched validated contract-field keyword '{structuredMatch}' — deterministic query, no LLM.");
        }

        if (NextNDaysPattern.IsMatch(trimmed))
        {
            return new QueryRouteDecision(
                trimmed,
                QueryIntent.Structured,
                "matched a relative date-window phrase ('next N days/months/years') — deterministic date filter, no LLM.");
        }

        return new QueryRouteDecision(
            trimmed,
            QueryIntent.Semantic,
            "no structured field pattern matched; defaulting to semantic retrieval rather than risk a false deterministic answer (Appendix C rule 10).");
    }

    private static string? FirstMatch(string question, string[] keywords) =>
        keywords.FirstOrDefault(keyword => question.Contains(keyword, StringComparison.OrdinalIgnoreCase));
}
