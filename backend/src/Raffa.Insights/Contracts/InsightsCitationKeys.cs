using Raffa.SharedKernel;

namespace Raffa.Insights.Contracts;

/// <summary>
/// The three citation-key shapes task E13/F07/US01/T01's own coding objective fixes verbatim for
/// every number the strategy pack narrates: <c>fact:&lt;contractId&gt;:&lt;field&gt;</c> (a tenant
/// contract fact), <c>market:&lt;recordId&gt;</c> (a market-intelligence record), <c>calc:&lt;name&gt;</c>
/// (a value this calculator itself derived, not read off any single source — for example "today's
/// date" or "how many priced lines this contract has"). Centralized here so
/// <c>Raffa.Insights.Negotiation.PricedLineNegotiationCalculator</c>,
/// <c>Raffa.Insights.Strategy.StrategyPackBuilder</c> and their tests all build the same three
/// shapes the same way — never hand-formatted ad hoc at each call site.
/// </summary>
public static class InsightsCitationKeys
{
    /// <summary>A tenant contract fact, e.g. <c>fact:11111111-.../priced-line[0].quantity</c>.</summary>
    public static string Fact(EntityId contractId, string field) => $"fact:{contractId}:{field}";

    /// <summary>A market-intelligence record (<c>Raffa.Market</c>'s own <c>MarketDeal</c>/
    /// <c>market_record</c> row id, ADR-024) — only ever built when a real record id is known;
    /// never fabricated (Appendix C rule 10).</summary>
    public static string Market(string recordId) => $"market:{recordId}";

    /// <summary>A value this calculator derived itself (a date, a count, a normalized ratio) — never
    /// a single source row, so it names the computation instead, honestly (mirrors
    /// <c>Raffa.Quotes.Application.Strategy.NegotiationLeverEvidence</c>'s own "name the
    /// computation" convention for the identical case).</summary>
    public static string Calc(string name) => $"calc:{name}";
}
