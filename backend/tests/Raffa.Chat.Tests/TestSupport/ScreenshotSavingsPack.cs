using Raffa.Chat.Application.Pack;

namespace Raffa.Chat.Tests.TestSupport;

/// <summary>
/// The pack behind the live "Where can I save the most this quarter?" turn that used to abstain
/// with "actionKey 'raffa:renewals' does not resolve…": the portfolio-target verdict, two council
/// plays and the council verdict, the #1 validated-contract candidate, one market-feed deal, the
/// playbook and the three Raffa feature items — shaped exactly as
/// <c>Raffa.Api.AskCopilotService.BuildPortfolioSavingsTargetPackAsync</c> and
/// <c>Council.NegotiationCouncil</c> build them (keys, corpora, "Grounded in:" tail included).
/// </summary>
internal static class ScreenshotSavingsPack
{
    public const string ContractId = "3f2b8c1e-0000-4000-8000-000000000001";

    public static IReadOnlyList<PackItem> Build() =>
    [
        new PackItem(
            "calc:portfolio-target",
            PackCorpus.Calc,
            "Portfolio — saving target and coverage inside the window",
            "target reachable",
            null, null,
            "Window 90 days, no amount named. 2 contracts can be acted on inside the window; their grounded levers add up to EUR 15365 to EUR 30730. The target is reachable inside the window on the grounded levers alone.",
            "/renewals", null, null,
            "deterministic calculator",
            [
                new PackValue("windowDays", "90", PackValueKind.Number),
                new PackValue("coverageLow", "15365", PackValueKind.Amount, "EUR"),
                new PackValue("coverageHigh", "30730", PackValueKind.Amount, "EUR"),
                new PackValue("candidatesInWindow", "2", PackValueKind.Number),
            ]),
        new PackItem(
            "calc:council:play[1]",
            PackCorpus.Calc,
            "Play 1 — Oracle uplift cap",
            "negotiation council · rank 1",
            null, null,
            "Ask Oracle to cap the renewal uplift at 7% before the notice date. Timing: send it by 2026-10-21. Grounded in: calc:lever[3f2b8c1e:uplift-cap], calc:candidate[1].",
            $"/contracts/{ContractId}", null, null,
            "negotiation council",
            [
                new PackValue("estimatedLow", "15365", PackValueKind.Amount, "EUR"),
                new PackValue("estimatedHigh", "30730", PackValueKind.Amount, "EUR"),
                new PackValue("percent", "7", PackValueKind.Percentage),
                new PackValue("actionDate", "2026-10-21", PackValueKind.Date),
                new PackValue("daysToAction", "29", PackValueKind.Number),
            ],
            ContractId),
        new PackItem(
            "calc:council:play[2]",
            PackCorpus.Calc,
            "Play 2 — Databricks notice reservation + uplift cap",
            "negotiation council · rank 2",
            null, null,
            "Reserve the notice right with Databricks and ask for a 5% uplift cap. Timing: before 2026-12-10. Grounded in: calc:candidate[2].",
            null, null, null,
            "negotiation council",
            [
                new PackValue("estimatedLow", "10000", PackValueKind.Amount, "EUR"),
                new PackValue("estimatedHigh", "20000", PackValueKind.Amount, "EUR"),
                new PackValue("percent", "5", PackValueKind.Percentage),
                new PackValue("actionDate", "2026-12-10", PackValueKind.Date),
            ]),
        new PackItem(
            "calc:council:verdict",
            PackCorpus.Calc,
            "Council verdict — the goal is reachable",
            "negotiation council",
            null, null,
            "The two plays inside the window cover EUR 15365 to EUR 30730 on their own.",
            null, null, null,
            "negotiation council",
            []),
        new PackItem(
            "calc:candidate[1]",
            PackCorpus.Calc,
            "Oracle — #1 inside the window",
            "inside the window",
            null, null,
            "Oracle: grounded levers worth EUR 15365 to EUR 30730 a year on an annual spend of EUR 439000. Notice falls on 2026-10-21. Best levers — Uplift cap: up to EUR 30730. Cumulative with the candidates above: EUR 30730.",
            $"/contracts/{ContractId}", null, null,
            "deterministic calculator",
            [
                new PackValue("coverageLow", "15365", PackValueKind.Amount, "EUR"),
                new PackValue("coverageHigh", "30730", PackValueKind.Amount, "EUR"),
                new PackValue("runningCumulativeHigh", "30730", PackValueKind.Amount, "EUR"),
                new PackValue("annualSpend", "439000", PackValueKind.Amount, "EUR"),
                new PackValue("actionDate", "2026-10-21", PackValueKind.Date),
                new PackValue("daysToAction", "29", PackValueKind.Number),
            ],
            ContractId),
        new PackItem(
            "market:deal-oracle-db-emea",
            PackCorpus.Market,
            "Oracle · Database Enterprise (DB-EE)",
            "36 months · EMEA · representative market data",
            null, null,
            "Companies of 1000-5000 employees closing Oracle Database Enterprise in EMEA in 2026-Q2 paid P50 EUR 132.5 (P25 110-P75 150), achieved a 12% discount.",
            null, null,
            "deal-oracle-db-emea",
            "representative market data · mock feed",
            [
                new PackValue("p25", "110", PackValueKind.Amount, "EUR"),
                new PackValue("p50", "132.5", PackValueKind.Amount, "EUR"),
                new PackValue("p75", "150", PackValueKind.Amount, "EUR"),
                new PackValue("discountAchievedPct", "12", PackValueKind.Percentage),
            ]),
        new PackItem(
            "raffa:playbook:notice-timing",
            PackCorpus.Raffa,
            "Playbook — notice timing",
            null,
            null, null,
            "Send the notice before the deadline so the renewal is negotiated, not automatic.",
            null, null, null,
            "Raffa playbook",
            []),
        Feature("renewals", "Renewals", "/renewals"),
        Feature("savings", "Savings", "/savings"),
        Feature("portfolio", "Portfolio", "/contracts"),
    ];

    private static PackItem Feature(string key, string title, string href) =>
        new(
            $"raffa:{key}",
            PackCorpus.Raffa,
            title,
            href,
            null, null,
            $"{title} is a Raffa feature.",
            href, null, null,
            "Raffa feature",
            []);
}
