namespace Raffa.Chat.Domain;

/// <summary>
/// The fixed planner intents for a <see cref="GateLabel.InDomain"/> turn (task E13/F06/US01/T01,
/// ask-engine; ADR-024 "planner (fixed intents)"; `inputs/requirements.md` R-ASK-03). A model call
/// (the `answer` role) may only narrate the pack assembled for one of these — it never picks the
/// intent itself and never answers free-form (R-ASK-03: "a model call may only pick among the
/// fixed intents, never free-form" — in this implementation the intent is decided entirely
/// deterministically by <see cref="Application.Planning.IntentPlanner"/>, so no model call is
/// spent on intent selection at all).
/// </summary>
public enum AskIntent
{
    /// <summary>Dates, spend, notice periods, uplift — a validated contract field (spec §8.3
    /// "structured query").</summary>
    StructuredFact,

    /// <summary>Liability, termination, confidentiality... — clause-level content (spec §8.3
    /// "semantic retrieval").</summary>
    Clause,

    /// <summary>"Is my X contract in line with market?" — a named, validated contract compared
    /// against the benchmark (R-CMP-01).</summary>
    MarketCompare,

    /// <summary>"How should I approach the X renewal?" — the full strategy pack for one contract
    /// (R-STR-01).</summary>
    RenewalStrategy,

    /// <summary>"Which contracts are most critical / where can we save?" — the portfolio-wide
    /// criticality ranking (R-PORT-01/02), typically asked in a new chat.</summary>
    PortfolioStrategy,

    /// <summary>"Which of my contracts are poorly positioned on the market / where can I save in
    /// 2026?" — a portfolio-wide market-position ranking (task E27/F01/US01/T01, NW-79/NW-86;
    /// ADR-024 w19 cl. 13; product-owner lock 6: R-SYS-02 narrowed — this is answered in Ask,
    /// never routed to Quote check, which stays the handler for a new market proposal). Always the
    /// workspace portfolio, even when the chat was opened from one contract's 360 (lock 4).</summary>
    PortfolioMarketPosition,

    /// <summary>"Where is the largest saving?" / "quali leve per risparmiare 20k sul rinnovo" for
    /// one contract — that contract's savings levers: grounded lever calculations, negotiation
    /// points, market deals, clause evidence and the negotiation council's plays.</summary>
    Savings,

    /// <summary>"Come posso salvare 40K sul prossimo quarterly basandomi sui contratti attivi?" —
    /// a quantified saving goal (amount and/or window, see
    /// <see cref="Application.Planning.SavingsGoal"/>) with no supplier in scope: the
    /// portfolio-wide candidate list that adds up to the target inside the window.</summary>
    PortfolioSavingsTarget,

    /// <summary>"Which documents are not askable yet?" — document/field status, not a contract
    /// fact.</summary>
    DocumentStatus,

    /// <summary>A benchmark/compare question with no validated contract to compare inline —
    /// routes to Quote check instead of narrating (R-SYS-02).</summary>
    QuoteRoute,

    /// <summary>A bare "take me to X" request with nothing to narrate — deterministic routing
    /// only, the same shape as <see cref="QuoteRoute"/>.</summary>
    Navigate,

    /// <summary>"Cerca sul web le pratiche di mercato sui rinnovi SaaS" / "search the web for
    /// supplier news on X" — an explicit request to leave the tenant's data and read the public
    /// web (ADR-030). Never answered directly: the composition root first asks the user's consent
    /// (an interview question with the <c>consent</c> presentation) and only an authorised turn
    /// runs <c>Application.WebResearch.WebResearchComposer</c>, behind the kill switch, the
    /// workspace opt-in and the daily budget. With any gate closed it becomes a redirect that says
    /// why. An interview option can also force this intent (the "search the public web" choice on
    /// the interpretation menu).</summary>
    WebResearch,
}
