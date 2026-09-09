namespace Contigo.Chat.Domain;

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

    /// <summary>"Where is the largest saving?" for one contract — that contract's savings
    /// opportunities/targets.</summary>
    Savings,

    /// <summary>"Which documents are not askable yet?" — document/field status, not a contract
    /// fact.</summary>
    DocumentStatus,

    /// <summary>A benchmark/compare question with no validated contract to compare inline —
    /// routes to Quote check instead of narrating (R-SYS-02).</summary>
    QuoteRoute,

    /// <summary>A bare "take me to X" request with nothing to narrate — deterministic routing
    /// only, the same shape as <see cref="QuoteRoute"/>.</summary>
    Navigate,
}
