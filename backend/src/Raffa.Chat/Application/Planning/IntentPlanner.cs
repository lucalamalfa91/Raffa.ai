using System.Text.RegularExpressions;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Application.Planning;

/// <summary>
/// Maps a <see cref="GateLabel.InDomain"/> question onto one of the nine fixed
/// <see cref="AskIntent"/> values (task E13/F06/US01/T01, ask-engine; ADR-024 "planner (fixed
/// intents)"; `inputs/requirements.md` R-ASK-03). The prototype's own branch order
/// (`inputs/design/prototypes/raffa-v2/app.jsx` → <c>ask(text, scope)</c>, echoed in
/// `raffa-v2/ia-v2.md` "Ask intents in the prototype") is the behavioural oracle this planner
/// reproduces, generalized from keyword matching over a hard-coded fixture into keyword matching
/// over the fixed intent enum — the pack composition (a later step, in the composition root) is
/// what actually supplies real data per intent; this type only decides which shape of pack to
/// build.
///
/// <para>
/// Pure and synchronous — no I/O, no LLM call (Appendix C rule 6; R-ASK-03: "a model call may only
/// pick among the fixed intents, never free-form" — here no model is spent on intent selection at
/// all, the same determinism convention <c>AskRaffaQueryRouter</c>/
/// <c>DeterministicQueryPlanner</c> already establish for the older, single-turn query router this
/// engine supersedes for Ask Raffa's own gate/planner responsibilities).
/// </para>
///
/// <para>
/// Reuses <c>AskRaffaQueryRouter</c>'s legal/clause vocabulary for <see cref="AskIntent.Clause"/>
/// detection (both routers agree a liability/termination/confidentiality question needs clause
/// retrieval, not a field filter) rather than duplicating the list — <c>DeterministicQueryPlanner</c>
/// itself is reused one level down, by the composition root, to turn a
/// <see cref="AskIntent.StructuredFact"/> plan into an actual dates/spend lookup once real
/// <c>ContractFact</c> rows are available (this project cannot fetch them itself — ADR-002 allow
/// -list <c>[SharedKernel, AiGateway]</c>).
/// </para>
/// </summary>
public sealed class IntentPlanner
{
    private readonly AskRaffaQueryRouter _legacyRouter = new();

    // "benchmark / compare / competitor / 'in linea' / market / fair price / too much" — prototype
    // verbatim, English + Italian.
    private static readonly Regex BenchmarkPattern = new(
        @"\b(benchmark|compar\w*|confront\w*|competitor|concorrent\w*|in\s+linea|allineat\w*|" +
        @"market|mercato|fair\s+price|prezzo\s+giusto|too\s+much|troppo)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "How should I approach the Salesforce renewal?" / "Come dovrei affrontare il rinnovo
    // Salesforce?" (R-STR-01 AC-1's own worked question) — a full strategy pack, not just a date.
    private static readonly Regex RenewalStrategyPattern = new(
        @"\b(approach|affrontare|renewal\s+strategy|strategia|negotiat\w*|negozia\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "top / first / why / perché / start" (priority narration) and "most critical / più critici"
    // (R-PORT-01/02's own portfolio-wide ranking) share one pattern: scoped to a named supplier
    // this becomes RenewalStrategy (that contract's own priority explanation), portfolio-wide it
    // becomes PortfolioStrategy.
    private static readonly Regex PriorityPattern = new(
        @"\b(top|first|why|perch[eé]|start|critic\w*|priorit\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "saving / risparm / largest" (prototype) plus "where can we save / dove posso risparmiare"
    // (R-PORT-02's own worked question) — same named-supplier-vs-portfolio-wide split as priority.
    private static readonly Regex SavingsPattern = new(
        @"\b(saving\w*|risparm\w*|largest)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "askable / not yet / confidence / fields / missing" (prototype) plus status/stato.
    private static readonly Regex DocumentStatusPattern = new(
        @"\b(askable|not\s+yet|confidence|fiducia|fields?|campi|missing|mancant\w*|status|stato)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A bare "take me to X" / "open X" navigation request with nothing to narrate.
    private static readonly Regex NavigatePattern = new(
        @"\b(take\s+me\s+to|go\s+to|open\s+(documents|renewals|portfolio|savings|quotes)|" +
        @"vai\s+a|apri\s+(documenti|rinnovi|portafoglio))\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Plans <paramref name="question"/>, already known to be
    /// <see cref="GateLabel.InDomain"/> (<see cref="Gate.DomainGate.Classify"/>).
    /// </summary>
    /// <param name="question">The caller's raw question text.</param>
    /// <param name="namedSupplier">The gate's own resolved supplier name
    /// (<see cref="Gate.DomainGateResult.NamedSupplier"/>), or <see langword="null"/> when the
    /// question named none. A benchmark/priority/savings question scopes to this contract when
    /// present, or to the whole portfolio when absent.</param>
    /// <exception cref="ArgumentException"><paramref name="question"/> is null/blank.</exception>
    public IntentPlanResult Plan(string question, string? namedSupplier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var trimmed = question.Trim();

        if (BenchmarkPattern.IsMatch(trimmed))
        {
            return namedSupplier is not null
                ? new IntentPlanResult(
                    AskIntent.MarketCompare,
                    $"matched the benchmark/compare lexicon with a known supplier ('{namedSupplier}') " +
                    "in scope — inline comparison (R-CMP-01).",
                    namedSupplier)
                : new IntentPlanResult(
                    AskIntent.QuoteRoute,
                    "matched the benchmark/compare lexicon with no supplier in scope — route to " +
                    "Quote check (R-SYS-02).",
                    namedSupplier);
        }

        if (RenewalStrategyPattern.IsMatch(trimmed))
        {
            return new IntentPlanResult(
                AskIntent.RenewalStrategy,
                "matched the renewal-strategy lexicon ('approach'/'affrontare'/'negotiate'...).",
                namedSupplier);
        }

        if (PriorityPattern.IsMatch(trimmed))
        {
            return namedSupplier is not null
                ? new IntentPlanResult(
                    AskIntent.RenewalStrategy,
                    $"matched the priority lexicon ('top'/'first'/'critical'...) scoped to " +
                    $"'{namedSupplier}' — that contract's own strategy pack explains its priority.",
                    namedSupplier)
                : new IntentPlanResult(
                    AskIntent.PortfolioStrategy,
                    "matched the priority lexicon ('top'/'first'/'critical'...) with no supplier " +
                    "in scope — portfolio-wide criticality ranking (R-PORT-01/02).",
                    namedSupplier);
        }

        if (SavingsPattern.IsMatch(trimmed))
        {
            return namedSupplier is not null
                ? new IntentPlanResult(
                    AskIntent.Savings,
                    $"matched the savings lexicon scoped to '{namedSupplier}'.",
                    namedSupplier)
                : new IntentPlanResult(
                    AskIntent.PortfolioStrategy,
                    "matched the savings lexicon with no supplier in scope — portfolio-wide " +
                    "'where can we save' (R-PORT-02).",
                    namedSupplier);
        }

        if (DocumentStatusPattern.IsMatch(trimmed))
        {
            return new IntentPlanResult(
                AskIntent.DocumentStatus, "matched the document/field-status lexicon.", namedSupplier);
        }

        if (NavigatePattern.IsMatch(trimmed))
        {
            return new IntentPlanResult(
                AskIntent.Navigate, "matched a bare navigation request with nothing to narrate.", namedSupplier);
        }

        var legacyDecision = _legacyRouter.Route(trimmed);
        if (legacyDecision.Intent == Domain.QueryIntent.Semantic)
        {
            return new IntentPlanResult(
                AskIntent.Clause,
                $"legacy query router classified this Semantic (clause/legal vocabulary): {legacyDecision.Reason}",
                namedSupplier);
        }

        // Structured (dates, spend, "next N days") or no pattern matched at all: StructuredFact is
        // the safe default — an empty pack for a genuinely unanswerable question still abstains
        // honestly downstream (Appendix C rule 10) rather than this planner inventing a tenth
        // intent for "unknown".
        return new IntentPlanResult(
            AskIntent.StructuredFact,
            $"legacy query router classified this Structured, or nothing else matched: {legacyDecision.Reason}",
            namedSupplier);
    }
}
