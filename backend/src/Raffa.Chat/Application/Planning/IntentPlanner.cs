using System.Text.RegularExpressions;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Application.Planning;

/// <summary>
/// Maps a <see cref="GateLabel.InDomain"/> question onto one of the ten fixed
/// <see cref="AskIntent"/> values (task E13/F06/US01/T01, ask-engine; ADR-024 "planner (fixed
/// intents)"; `inputs/requirements.md` R-ASK-03; <see cref="AskIntent.PortfolioMarketPosition"/>
/// added by task E27/F01/US01/T01, NW-79, ADR-024 w19 cl. 13). The prototype's own branch order
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

    // "quali contratti" / "which contracts" (PortfolioQuestionPattern below) combined with "mal
    // posizionat*" / "poorly positioned" / "above market" / "too expensive" (optionally
    // "risparm*" / "2026" too, not required for the match) — screenshot Q1 (task
    // E27/F01/US01/T01, NW-79/NW-86; ADR-024 w19 cl. 13; product-owner lock 6: this is a
    // portfolio question answered in Ask, never Quote check's new-market-proposal handler).
    // Checked before BenchmarkPattern and SavingsPattern so a "mercato"/"risparmiare" inside the
    // same sentence never steals it into QuoteRoute/PortfolioStrategy — this is its own intent.
    private static readonly Regex PortfolioMarketPositionPattern = new(
        @"\b(mal\s+posizionat\w*|poorly\s+positioned|above\s+market|too\s+expensive)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PortfolioQuestionPattern = new(
        @"\b(quali\s+contratti|which\s+contracts)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "saving / risparm / largest" (prototype) plus "where can we save / dove posso risparmiare"
    // (R-PORT-02's own worked question) — same named-supplier-vs-portfolio-wide split as
    // priority. Checked ahead of BenchmarkPattern (task E27/F01/US01/T01, NW-79; ADR-024 w19 cl.
    // 13) so a sentence that also names "mercato" — but does not match
    // PortfolioMarketPositionPattern above — still reaches Savings/PortfolioStrategy instead of
    // being stolen by the market/benchmark lexicon into QuoteRoute.
    private static readonly Regex SavingsPattern = new(
        @"\b(saving\w*|risparm\w*|largest|salv\w*|tagli\w*|ridurr\w*|riduzione|ridotto|costi|budget|" +
        @"lev[ae]|cut\s+(the\s+)?cost\w*|spend\s+less|abbassare|abbattere|efficient\w*)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A short follow-up that carries no intent of its own ("non mi hai risposto", "e quindi?",
    // "spiegami meglio", "how?") — planned on the previous user question plus this one, so the
    // turn advances the same topic instead of falling through to the StructuredFact default.
    // Two tiers: an explicit follow-up phrase anywhere in a short turn, or a bare interrogative
    // ("come?", "why?") when the whole turn is three words or fewer — never a full question that
    // merely opens with "come"/"how".
    private static readonly Regex ExplicitFollowUpPattern = new(
        @"\b(non\s+(mi\s+)?hai\s+risposto|e\s+quindi|e\s+allora|spiegami|approfondisci|dettaglia\w*|pi[uù]\s+dettagli|" +
        @"di\s+pi[uù]|in\s+concreto|concretamente|nello\s+specifico|ok\s+e|s[iì]\s+ma|ma\s+come|ma\s+quindi|" +
        @"tell\s+me\s+more|more\s+detail\w*|elaborate|be\s+(more\s+)?specific|you\s+didn'?t\s+answer|" +
        @"that'?s\s+not\s+an\s+answer|and\s+then|so\s+what|concretely|specifically)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BareInterrogativePattern = new(
        @"^(?:e\s+)?(?:come|quindi|perch[eé]|allora|how|why|and)\W*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const int FollowUpMaxWords = 12;
    private const int BareInterrogativeMaxWords = 3;

    // "benchmark / compare / competitor / 'in linea' / market / fair price / too much" — prototype
    // verbatim, English + Italian.
    private static readonly Regex BenchmarkPattern = new(
        @"\b(benchmark|compar\w*|confront\w*|competitor|concorrent\w*|in\s+linea|allineat\w*|" +
        @"market|mercato|fair\s+price|prezzo\s+giusto|too\s+much|troppo)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "How should I approach the Salesforce renewal?" / "Come dovrei affrontare il rinnovo
    // Salesforce?" (R-STR-01 AC-1's own worked question) — a full strategy pack, not just a date.
    // Widened (task E27/F01/US01/T01, NW-79/NW-95; ADR-024 w19 cl. 13) with "contrattare"/
    // "punti" and the bare noun "rinnovo" (screenshot Q3: "quali punti su cui contrattare nel
    // prossimo rinnovo") — two deliberate narrowings, each guarding a real collision:
    // "contratta\w*", never "contratt\w*", so it matches only the verb family (to negotiate), not
    // the noun "contratto"/"contratti" (contract) that shows up in almost every Ask question; and
    // bare "rinnovo", never "rinnov\w*", because that wildcard also matches the plain verb
    // conjugation "rinnovano" ("[contracts] renew") in a structured date question with no
    // supplier in scope — golden case seeded-structured_fact-rinnovo-120-giorni-it
    // (GAP-ASK-ITALIAN-STRUCTURED-BLIND) — which would otherwise be stolen into an unscoped
    // portfolio-strategy criticality ranking instead of its documented abstain.
    private static readonly Regex RenewalStrategyPattern = new(
        @"\b(approach|affrontare|renewal\s+strategy|strategia|negotiat\w*|negozia\w*|" +
        @"contrattare|contratta\w*|rinnovo|punti)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // "top / first / why / perché / start" (priority narration) and "most critical / più critici"
    // (R-PORT-01/02's own portfolio-wide ranking) share one pattern: scoped to a named supplier
    // this becomes RenewalStrategy (that contract's own priority explanation), portfolio-wide it
    // becomes PortfolioStrategy.
    private static readonly Regex PriorityPattern = new(
        @"\b(top|first|why|perch[eé]|start|critic\w*|priorit\w*)\b",
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

    // "notice" / "preavviso" / "disdetta" / "disdetta period" / "cancellation deadline" (task
    // E27/F01/US01/T01, NW-79/NW-91; ADR-024 w19 cl. 13) — a validated-contract structured fact
    // (EndDate/CancellationDeadline/AutoRenewal), never generic clause RAG. Checked last, right
    // before the legacy router's own fallback: <c>AskRaffaQueryRouter</c> already has a
    // "cancellation deadline" Structured keyword, but no "notice"/"preavviso"/"disdetta" keyword
    // of its own, so without this pattern those phrasings default to its Semantic fallback and
    // become AskIntent.Clause (unfiltered tenant RAG) instead of the structured notice fact.
    private static readonly Regex NoticePattern = new(
        @"\b(notice|preavviso|disdetta(\s+period)?|cancellation\s+deadline)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Plans <paramref name="question"/>, already known to be
    /// <see cref="GateLabel.InDomain"/> (<see cref="Gate.DomainGate.Classify"/>).
    /// </summary>
    /// <param name="question">The caller's raw question text.</param>
    /// <param name="namedSupplier">The gate's own resolved supplier name
    /// (<see cref="Gate.DomainGateResult.NamedSupplier"/>), or <see langword="null"/> when the
    /// question named none. A benchmark/priority/savings question scopes to this contract when
    /// present, or to the whole portfolio when absent; a <see cref="AskIntent.PortfolioMarketPosition"/>
    /// question is always portfolio-wide regardless of this value (lock 4) — it is echoed, never
    /// used to narrow that intent to one contract.</param>
    /// <exception cref="ArgumentException"><paramref name="question"/> is null/blank.</exception>
    public IntentPlanResult Plan(string question, string? namedSupplier) => Plan(question, namedSupplier, previousUserQuestion: null);

    /// <summary>
    /// Plans <paramref name="question"/>; when it is a bare follow-up (see <c>FollowUpPattern</c>)
    /// and <paramref name="previousUserQuestion"/> is known, the two are planned together so the
    /// follow-up inherits the previous turn's intent and goal ("quali leve per risparmiare 20k" →
    /// "non mi hai risposto" stays a scoped savings turn with the same 20k target).
    /// </summary>
    public IntentPlanResult Plan(string question, string? namedSupplier, string? previousUserQuestion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);

        var trimmed = question.Trim();

        if (!string.IsNullOrWhiteSpace(previousUserQuestion) && IsBareFollowUp(trimmed))
        {
            var inherited = Plan(previousUserQuestion.Trim() + " " + trimmed, namedSupplier, previousUserQuestion: null);
            return inherited with { Reason = "bare follow-up planned on the previous user question: " + inherited.Reason };
        }

        var goal = SavingsGoalParser.Parse(trimmed);

        if (PortfolioMarketPositionPattern.IsMatch(trimmed) && PortfolioQuestionPattern.IsMatch(trimmed))
        {
            // Lock 4: always the workspace portfolio, even when a supplier is already in scope
            // (e.g. the chat was opened from one contract's 360) — this branch never reads
            // namedSupplier to decide anything, it only echoes it through unchanged.
            return new IntentPlanResult(
                AskIntent.PortfolioMarketPosition,
                "matched the portfolio mal-position lexicon ('quali contratti'/'which contracts' + " +
                "'mal posizionat*'/'poorly positioned'/'above market'/'too expensive') — always the " +
                "workspace portfolio, even when a supplier is in scope (lock 4; R-SYS-02 narrowed, " +
                "lock 6).",
                namedSupplier, goal);
        }

        if (SavingsPattern.IsMatch(trimmed))
        {
            if (namedSupplier is not null)
            {
                return new IntentPlanResult(
                    AskIntent.Savings,
                    $"matched the savings lexicon scoped to '{namedSupplier}'.",
                    namedSupplier,
                    goal);
            }

            return goal.HasTarget
                ? new IntentPlanResult(
                    AskIntent.PortfolioSavingsTarget,
                    "matched the savings lexicon with a quantified goal (amount/percent/window) and no " +
                    "supplier in scope — portfolio-wide savings target.",
                    namedSupplier,
                    goal)
                : new IntentPlanResult(
                    AskIntent.PortfolioStrategy,
                    "matched the savings lexicon with no supplier in scope — portfolio-wide " +
                    "'where can we save' (R-PORT-02).",
                    namedSupplier,
                    goal);
        }

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
                    namedSupplier, goal);
        }

        if (RenewalStrategyPattern.IsMatch(trimmed))
        {
            return new IntentPlanResult(
                AskIntent.RenewalStrategy,
                "matched the renewal-strategy lexicon ('approach'/'affrontare'/'negotiate'/" +
                "'contrattare'/'rinnovo'/'punti'...).",
                namedSupplier,
                goal);
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
                    namedSupplier, goal);
        }

        if (DocumentStatusPattern.IsMatch(trimmed))
        {
            return new IntentPlanResult(
                AskIntent.DocumentStatus, "matched the document/field-status lexicon.", namedSupplier, goal);
        }

        if (NavigatePattern.IsMatch(trimmed))
        {
            return new IntentPlanResult(
                AskIntent.Navigate, "matched a bare navigation request with nothing to narrate.", namedSupplier, goal);
        }

        if (NoticePattern.IsMatch(trimmed))
        {
            return new IntentPlanResult(
                AskIntent.StructuredFact,
                "matched the notice lexicon ('notice'/'preavviso'/'disdetta'/'cancellation " +
                "deadline') — a validated-contract structured fact, not clause RAG (NW-91).",
                namedSupplier, goal);
        }

        var legacyDecision = _legacyRouter.Route(trimmed);
        if (legacyDecision.Intent == Domain.QueryIntent.Semantic)
        {
            return new IntentPlanResult(
                AskIntent.Clause,
                $"legacy query router classified this Semantic (clause/legal vocabulary): {legacyDecision.Reason}",
                namedSupplier, goal);
        }

        // Structured (dates, spend, "next N days") or no pattern matched at all: StructuredFact is
        // the safe default — an empty pack for a genuinely unanswerable question still abstains
        // honestly downstream (Appendix C rule 10) rather than this planner inventing yet another
        // intent for "unknown".
        return new IntentPlanResult(
            AskIntent.StructuredFact,
            $"legacy query router classified this Structured, or nothing else matched: {legacyDecision.Reason}",
            namedSupplier, goal);
    }

    private static bool IsBareFollowUp(string question)
    {
        var wordCount = question.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        if (wordCount <= BareInterrogativeMaxWords && BareInterrogativePattern.IsMatch(question))
        {
            return true;
        }

        return wordCount <= FollowUpMaxWords && ExplicitFollowUpPattern.IsMatch(question);
    }
}
