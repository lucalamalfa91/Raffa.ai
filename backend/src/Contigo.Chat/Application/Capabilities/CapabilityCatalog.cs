namespace Contigo.Chat.Application.Capabilities;

/// <summary>
/// The versioned, static V2 capability catalog (R-SYS-01; ADR-024 "Capability catalog (R-SYS)";
/// task E13/F08/US01/T01; story us-01-capability-catalog, council decision "Catalog lives in
/// `backend/src/Contigo.Chat/Application/Capabilities/` (`CapabilityCatalog`, `CapabilityRouting`),
/// version string `capabilities-v2.0`"). Ten entries, one per V2 route in
/// `inputs/design/prototypes/contigo-v2/ia-v2.md`'s route map plus the two Documents sub-states
/// R-SYS-01/R-SYS-03 name (`documents-attention`, `documents-review`) — `/signin` is excluded, it
/// is authentication, not a Contigo capability a user asks Ask about.
///
/// <para>
/// Deliberately tenant-agnostic: no entry names a fixture supplier, a tenant id, or a specific
/// contract — that is exactly what keeps this catalog a single, static, versioned constant instead
/// of something a caller must rebuild per tenant (see <see cref="CapabilityAvailability"/>'s own
/// doc comment). Where the design source (`app.jsx`'s own demo chips/copy) hard-codes a fixture
/// supplier name (e.g. "Salesforce"), this catalog generalizes it away.
/// </para>
///
/// <para>
/// Sources: `contigo-v2/app.jsx`'s `ask()` capabilities branch ("I answer from your validated
/// contracts and route you to the right part of Contigo: • Documents — upload contracts, review
/// weak facts. • Portfolio — every contract, spend, liability and risk in one table. • Renewals —
/// deadlines and the action for each. • Quote check — benchmark a new quote against the market and
/// your history."), `chipsFor`/`c360Chips`, `contigo-v2/markup.html` (the "Documents › Review"
/// feature-card title R-SYS-03 AC-1 names, and the "The portfolio lights up from validated
/// contracts. Upload one to start." empty-state copy — see
/// <see cref="CapabilityRouting.ValidatedContractsEmptyStateCopy"/>), `contigo-v2/screens-v2.md`.
/// </para>
/// </summary>
public static class CapabilityCatalog
{
    /// <summary>Council-decided version string (story us-01-capability-catalog).</summary>
    public const string Version = "capabilities-v2.0";

    public const string AskKey = "ask";
    public const string DocumentsKey = "documents";
    public const string DocumentsAttentionKey = "documents-attention";
    public const string DocumentsReviewKey = "documents-review";
    public const string PortfolioKey = "portfolio";
    public const string ContractDetailKey = "contract-360";
    public const string RenewalsKey = "renewals";
    public const string SavingsKey = "savings";
    public const string QuoteCheckKey = "quote-check";
    public const string WorkspaceMembersKey = "workspace-members";

    /// <summary>Every catalog entry, in the order R-SYS-01's own list names them.</summary>
    public static IReadOnlyList<Capability> All { get; } = BuildAll();

    /// <summary><see langword="null"/> for an unrecognized key — never throws, since a caller
    /// (e.g. a not-yet-landed planner) may pass an id sourced from outside this catalog.</summary>
    public static Capability? Find(string key) =>
        All.FirstOrDefault(capability => string.Equals(capability.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// Two quiet per-screen suggestion chips (`app.jsx` `chipsFor`; task text "Per-screen
    /// suggestion chips (chipsFor in app.jsx) become SuggestionsFor(screenKey, supplierName?)").
    /// Falls back to the `ask` screen's own chips for an unrecognized <paramref name="screenKey"/>
    /// — the same fallback `app.jsx` itself uses (`chipsFor[s.screen]||chipsFor.ask`). The two
    /// Documents sub-states (<see cref="DocumentsAttentionKey"/>/<see cref="DocumentsReviewKey"/>)
    /// reuse the base <see cref="DocumentsKey"/> chips — `app.jsx`'s own `chipsFor` map has no
    /// separate entry for either, only for the Documents screen as a whole.
    /// </summary>
    /// <param name="screenKey">A <see cref="Capability.Key"/>, or any other screen identifier —
    /// unrecognized values fall back to <see cref="AskKey"/>'s own chips.</param>
    /// <param name="supplierName">Contract 360 (<see cref="ContractDetailKey"/>) is the one screen
    /// whose chips name the current supplier (`app.jsx` `c360Chips`); every other screen's chips
    /// are supplier-agnostic catalog copy (see the type doc comment's "tenant-agnostic" note).
    /// Ignored for every other <paramref name="screenKey"/>.</param>
    public static IReadOnlyList<string> SuggestionsFor(string screenKey, string? supplierName = null)
    {
        var normalizedKey = screenKey is DocumentsAttentionKey or DocumentsReviewKey
            ? DocumentsKey
            : screenKey;

        if (normalizedKey == ContractDetailKey)
        {
            var supplier = string.IsNullOrWhiteSpace(supplierName) ? "this supplier" : supplierName;
            return
            [
                $"When must we give notice to {supplier}?",
                $"What is our liability cap with {supplier}?",
            ];
        }

        return ChipsByScreenKey.TryGetValue(normalizedKey, out var chips) ? chips : ChipsByScreenKey[AskKey];
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> ChipsByScreenKey =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            [AskKey] = ["When does a contract expire?", "What liabilities do we have?"],
            [DocumentsKey] =
                ["Which documents are not askable yet?", "Which fields still lack confidence?"],
            [PortfolioKey] =
                ["Which of these have uncapped liability?", "Which contracts renew in the next 120 days?"],
            [RenewalsKey] = ["Why is this at the top?", "Which should we start first?"],
            [SavingsKey] =
                ["Where is the largest saving still in review?", "Which contracts renew in the next 120 days?"],
            [QuoteCheckKey] =
                ["How does this compare with our other contracts?", "Which contracts renew in the next 120 days?"],
            [WorkspaceMembersKey] = ["When does a contract expire?", "What liabilities do we have?"],
        };

    private static IReadOnlyList<Capability> BuildAll() =>
    [
        new Capability(
            AskKey,
            "Ask Contigo",
            "/ask",
            "Ask about dates, spend, notice periods and clauses in plain language — structured " +
            "questions run on validated fields, legal questions retrieve clauses, and every answer " +
            "cites its page or says it cannot answer.",
            ["What can Contigo do?", "When does this contract expire?", "What liabilities do we have?"],
            CapabilityRoleGate.Any,
            CapabilityAvailability.Always,
            [
                "Type a question in the Ask bar on any screen, or press ⌘K / Ctrl+K to focus it.",
                "Contigo authorises the scope, detects the intent and retrieves the evidence before it answers.",
                "Every reply cites the page that proves it, or says it cannot determine an answer.",
            ]),
        new Capability(
            DocumentsKey,
            "Documents",
            "/documents",
            "Upload contracts and quotes, watch them move through parsing and extraction, and " +
            "review the fields Contigo is not yet confident about before they count as validated.",
            ["How do I upload a contract?", "Which documents are not askable yet?"],
            CapabilityRoleGate.Any,
            CapabilityAvailability.Always,
            [
                "Drop your contracts — PDF, DOCX, XLSX, or PNG/JPG for a scanned copy.",
                "Contigo parses, classifies and extracts the contract automatically; anything that " +
                "is not a contract is rejected with a reason.",
                "Review the weak facts, then ask — answers only use validated facts, with the page " +
                "that proves them.",
            ]),
        new Capability(
            DocumentsAttentionKey,
            "Documents › Review",
            "/documents?filter=attention",
            "The documents still waiting for your review — everything already validated is tucked " +
            "away, since it is already askable.",
            ["How do I review weak facts?", "Which documents need my review?"],
            CapabilityRoleGate.Any,
            CapabilityAvailability.Always,
            [
                "Open Documents — Needs your attention is the default filter.",
                "Pick a row and select Review N fields.",
                "Accept or correct each weak fact, then Mark as validated.",
            ]),
        new Capability(
            DocumentsReviewKey,
            "Documents › Field review",
            "/documents?review={documentId}",
            "One document's weak facts, side by side with the original evidence — accept each " +
            "extracted value or correct it, then mark the contract validated.",
            ["Why is this field flagged?", "How do I correct a weak fact?"],
            CapabilityRoleGate.Any,
            CapabilityAvailability.Always,
            [
                "Select a flagged field to see its evidence — the document, page, section and " +
                "surrounding text.",
                "Accept the extracted value or correct it.",
                "Repeat until every weak fact is decided, then Mark as validated.",
            ]),
        new Capability(
            PortfolioKey,
            "Portfolio",
            "/contracts",
            "Every validated contract, its spend, liability and risk, in one table — open any row " +
            "for its full Contract 360 view.",
            ["Which contracts are most critical?", "Which contracts have uncapped liability?"],
            CapabilityRoleGate.Any,
            CapabilityAvailability.NeedsValidatedContract,
            [
                "Open Portfolio once at least one contract is validated.",
                "Use More columns or the filters to compare spend, risk and renewal dates.",
                "Open a row for its full Contract 360 view.",
            ]),
        new Capability(
            ContractDetailKey,
            "Contract 360",
            "/contracts/{contractId}",
            "One contract's answers band (where you can save, when you must move, what to do), the " +
            "clauses behind them, and a negotiation tracker.",
            ["What should we do about this contract?", "Why is this contract flagged?"],
            CapabilityRoleGate.Any,
            CapabilityAvailability.Always,
            [
                "Open a contract from Portfolio, Renewals, Savings or an Ask citation.",
                "Read the answers band and open Why for the clauses behind each figure.",
                "Start negotiation now to track the next steps, or open All terms for the full detail.",
            ]),
        new Capability(
            RenewalsKey,
            "Renewals",
            "/renewals",
            "Deadlines and the recommended action for every contract, sorted by priority, with a " +
            "tracker shared with Contract 360.",
            ["Which contracts renew in the next 120 days?", "Why is this contract first?"],
            CapabilityRoleGate.Any,
            CapabilityAvailability.NeedsValidatedContract,
            [
                "Open Renewals once at least one contract is validated.",
                "Pick a row to see its insight card — the facts and the recommended action.",
                "Start negotiation or assign it to a colleague; status stays in sync with Contract 360.",
            ]),
        new Capability(
            SavingsKey,
            "Savings",
            "/savings",
            "Savings KPIs and the opportunities behind them — contracts analyzed, upcoming " +
            "renewals, savings identified — reached from an action, not the rail.",
            ["Where can we save?", "What is the largest saving right now?"],
            CapabilityRoleGate.Any,
            CapabilityAvailability.NeedsValidatedContract,
            [
                "Open Savings from an Ask action, a Renewals row or Contract 360.",
                "Review the KPIs and the opportunities table.",
                "Open a row for the contract behind that opportunity.",
            ]),
        new Capability(
            QuoteCheckKey,
            "Quote check",
            "/quotes",
            "Benchmark a new quote against the market and your own history — extract the lines, " +
            "see the market position for each, then the target and negotiation levers.",
            ["Is this quote in line with the market?", "How does this compare with our other contracts?"],
            CapabilityRoleGate.Any,
            CapabilityAvailability.NeedsValidatedContract,
            [
                "Open Quote check once at least one contract is validated, or from an Ask benchmark action.",
                "Upload the supplier's quote — Contigo extracts and normalizes each line.",
                "Compare quoted vs market band per line, then open Target and negotiation levers if you want them.",
            ]),
        new Capability(
            WorkspaceMembersKey,
            "Workspace & members",
            "/workspace/members",
            "Invite teammates, assign their role and manage the workspace member list — Workspace " +
            "Admin only.",
            ["How do I invite a teammate?", "Who can upload documents?"],
            CapabilityRoleGate.Admin,
            CapabilityAvailability.Admin,
            [
                "Open Workspace & members from the footer (Workspace Admin only).",
                "Enter the teammate's workspace email and pick a role — Procurement or Workspace Admin.",
                "Send the invitation; Procurement can ask, review and triage renewals, and Workspace " +
                "Admin can also upload, delete and manage members.",
            ]),
    ];
}
