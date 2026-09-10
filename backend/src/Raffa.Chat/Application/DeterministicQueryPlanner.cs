using System.Globalization;
using System.Text.RegularExpressions;
using Raffa.Chat.Domain;

namespace Raffa.Chat.Application;

/// <summary>
/// Turns a <see cref="QueryRouteDecision"/> already routed to <see cref="QueryIntent.Structured"/>
/// (task E02/F04/US01/T01, <see cref="AskRaffaQueryRouter"/>) into a concrete, typed
/// <see cref="DeterministicQuery"/> that <see cref="DeterministicQueryHandler"/> can execute —
/// task E02/F04/US01/T02's own share of spec §8.3's "Intent detection ├── Structured query
/// (SQL / filters)" box.
///
/// <para>
/// Classification here is the same free, synchronous, 100% reproducible keyword/pattern match the
/// router itself uses (Appendix C rule 6, generalized the same way <see cref="AskRaffaQueryRouter"/>'s
/// own doc comment explains) — never an LLM call. See
/// <c>DeterministicQueryPlannerTests.Planner_has_no_dependency_on_the_AI_Gateway</c> for the
/// structural proof.
/// </para>
/// </summary>
public sealed class DeterministicQueryPlanner
{
    // Same relative date-window phrase AskRaffaQueryRouter.NextNDaysPattern matches, with the
    // amount/unit captured so the window size can actually be computed — the router only needs to
    // know the phrase is present, this planner needs to know what it says.
    private static readonly Regex NextNDaysPattern = new(
        @"next\s+(\d+)\s+(day|days|month|months|year|years)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // A capitalized word (or run of capitalized words) that is not the question's first word is,
    // for every phrasing this planner sees ("... Microsoft annual spend", "... with Acme Corp"),
    // a proper noun: English only capitalizes an ordinary word when it opens the sentence, and
    // AskRaffaQueryRouter.Route trims the question before this planner ever sees it, so
    // position 0 really is the first word. Used only to flag "this question names a specific
    // supplier" — see the "spend" branch of Plan below for why that must not be silently ignored
    // (Appendix C rule 10). Not full NLP: it can miss a lower-cased name and, given more than one
    // capitalized run, only reports the first — good enough to stop a silent company-wide
    // fallback from masquerading as a supplier-scoped answer, not a name extractor.
    private static readonly Regex CapitalizedSupplierNamePattern = new(
        @"(?<!^)[A-Z][A-Za-z0-9&'-]*(?:\s+[A-Z][A-Za-z0-9&'-]*)*",
        RegexOptions.Compiled);

    private const int DaysPerMonth = 30;
    private const int DaysPerYear = 365;

    /// <summary>
    /// Plans <paramref name="decision"/>. Pure and synchronous — no I/O, no LLM call, always the
    /// same plan for the same input.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="decision"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="decision"/>'s <see cref="QueryRouteDecision.Intent"/> is not
    /// <see cref="QueryIntent.Structured"/> — only the router's structured branch has a
    /// deterministic query to plan; handing this method a semantic decision is a caller bug, not
    /// a legitimate input.
    /// </exception>
    public DeterministicQuery Plan(QueryRouteDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.Intent != QueryIntent.Structured)
        {
            throw new ArgumentException(
                $"Only a '{QueryIntent.Structured}' decision can be planned into a deterministic " +
                $"query; '{decision.Question}' was routed '{decision.Intent}'.",
                nameof(decision));
        }

        var question = decision.Question;

        var windowMatch = NextNDaysPattern.Match(question);
        if (windowMatch.Success)
        {
            var days = ToDays(
                int.Parse(windowMatch.Groups[1].Value, CultureInfo.InvariantCulture),
                windowMatch.Groups[2].Value);
            return new DeterministicQuery.RenewalWindow(question, days);
        }

        if (question.Contains("spend", StringComparison.OrdinalIgnoreCase))
        {
            // No supplier-name -> SupplierId resolution exists yet: Suppliers/Products is still
            // an empty scaffold (see ContractFact's doc comment and
            // Raffa.Documents.Contracts.Application.PortfolioFilter's identical gap for
            // "category"). A caller that already has a resolved SupplierId can still scope the
            // aggregation — see DeterministicQueryHandler — this planner just cannot derive one
            // from free text yet.
            //
            // What this planner CAN do from text alone is tell "no supplier named" apart from "a
            // supplier is named but unresolved" (CapitalizedSupplierNamePattern above) — the
            // second case is passed through as RequestedSupplierName so DeterministicQueryHandler
            // can set DeterministicQueryResult.SupplierScopeUnresolved instead of silently
            // returning a company-wide total under a question that names one supplier (Appendix C
            // rule 10: uncertainty, not fabricated precision).
            var supplierNameMatch = CapitalizedSupplierNamePattern.Match(question);
            var requestedSupplierName = supplierNameMatch.Success ? supplierNameMatch.Value : null;

            return new DeterministicQuery.AnnualSpend(question, SupplierId: null, requestedSupplierName);
        }

        return new DeterministicQuery.Unsupported(
            question,
            "the router classified this as a structured contract-field question, but task " +
            "E02/F04/US01/T02 only covers renewal-date-window and annual-spend queries (task " +
            "title: \"Deterministic query handlers for dates/spend\"); no deterministic handler " +
            "exists yet for this phrasing.");
    }

    private static int ToDays(int amount, string unit) => unit.ToLowerInvariant() switch
    {
        "month" or "months" => amount * DaysPerMonth,
        "year" or "years" => amount * DaysPerYear,
        _ => amount,
    };
}
