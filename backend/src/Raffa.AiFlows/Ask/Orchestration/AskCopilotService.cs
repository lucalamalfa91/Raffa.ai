using Raffa.AiFlows.Shared.ContractContext;
using static Raffa.AiFlows.Ask.Interview.InterviewInputsFactory;
using static Raffa.AiFlows.Ask.Packs.ContractResolution;
using static Raffa.AiFlows.Ask.Packs.PackItemFactory;
using Raffa.AiFlows.Shared.Audit;
using Raffa.AiFlows.CapabilityGaps.Investigation;
using Raffa.AiFlows.Negotiation.DataCheck;
using Raffa.AiFlows.WebResearch.Orchestration;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Raffa.AiFlows.Ask.Answering;
using Raffa.AiFlows.Ask.Gate;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Packs;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.Ask.Reply;
using Raffa.AiFlows.Ask.Routing;
using Raffa.AiFlows.CapabilityGaps.Drafting;
using Raffa.AiFlows.Negotiation.Orchestration;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Planning;
using Raffa.AiFlows.Shared.Reply;
using Raffa.AiFlows.Shared.Routing;
using Raffa.AiGateway.Telemetry;
using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Application.WebResearch;
using Raffa.Chat.Domain;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Insights.Application;
using Raffa.Insights.Contracts;
using Raffa.Insights.Criticality;
using Raffa.Insights.Strategy;
using Raffa.Market.Contracts;
using Raffa.Market.Retrieval;
using Raffa.Renewals.Application;
using Raffa.Savings.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Raffa.Suppliers.Products.Application;

namespace Raffa.AiFlows.Ask.Orchestration;

/// <summary>
/// The Ask V2 pack-composition root (task E13/F06/US01/T01, ask-engine; ADR-024 "context pack
/// assembled in Raffa.Api from the three sources"; parent story us-01-ask-engine). Everything
/// <c>Raffa.Chat</c>'s allow-list (<c>[SharedKernel, AiGateway]</c>) forbids that module from
/// doing itself happens here: authorization-scoped tenant facts
/// (<see cref="PortfolioQueryService"/>/<see cref="Contract360QueryService"/>), clause chunks
/// (<see cref="EmbeddingRetrievalService"/>), renewal/priority calculators
/// (<see cref="RenewalEngine"/>/<see cref="PriorityScoreCalculator"/>), market data
/// (<see cref="IBenchmarkService"/>/<see cref="IMarketKnowledgeRetrieval"/>), portfolio
/// calculators (<see cref="CriticalityScoreCalculator"/>/<see cref="StrategyPackBuilder"/>),
/// savings opportunities (<see cref="SavingsOpportunityService"/>) and supplier names
/// (<see cref="ISupplierNameLookup"/>) are all composed into one
/// <see cref="Raffa.AiFlows.Shared.Pack.PackItem"/> list, then handed to
/// <c>Raffa.Chat</c>'s own gate/planner/guards/answer pipeline. <c>ChatEndpointExtensions</c>
/// and <c>ConversationsEndpointExtensions</c> are this service's only two callers
/// (`POST /api/chat/query`'s alias and `POST /api/conversations/{id}/messages` respectively).
///
/// <para>
/// <b>Authorization before anything (R-ASK-01, ADR-011)</b>: <see cref="AskAsync"/> opens the
/// caller's tenant scope before a single query runs, and every downstream fetch is already
/// tenant-scoped by the module it belongs to (RLS backstop plus each service's own
/// <c>tenant_id</c> filter) — the same belt-and-suspenders shape every other composition file in
/// this host already relies on. Off-domain/greeting/legal/capability/needs-document turns never
/// reach any of the fetches below (R-ASK-02) — the domain gate's own supplier-name check is the
/// <em>only</em> query this method always runs, because it is what decides whether a name is
/// known at all.
/// </para>
///
/// <para>
/// <b>Scoped entries (task E25/F03/US01/T01, NW-56; ADR-024)</b>: <see cref="AskAsync"/>'s own
/// <c>scopeContractId</c> parameter — the conversation's persisted
/// <c>Conversation.ScopeContractId</c>, threaded in by <c>ConversationsEndpointExtensions</c>
/// — resolves to a known supplier name and overrides the domain gate's own free-text extraction
/// before the reply switch decides, so a turn opened from Contract 360's "Ask about it" is always
/// about that one contract, never a generic gate/hello, regardless of whether the question itself
/// names the supplier. See <see cref="AskAsync"/>'s own scope-resolution step for the mechanism.
/// </para>
///
/// <para>
/// <b>Scope wins by id, and an unseen scope refuses (task E27/F02/US01/T01, NW-76; ADR-024 w19 cl.
/// 12; lock 4)</b>: the gate-level override above only carries a <em>name</em> forward — two
/// portfolio rows can share one supplier's display name, and a name-only lookup could land on the
/// wrong one. <see cref="BuildInDomainReplyAsync"/> also receives the scoped <em>id</em> itself
/// (not just the resolved name) and lets it win outright over that lookup, and folds it into the
/// routing context's own contract id so a follow-up action never points at the same-name-but-wrong
/// contract either. A <c>scopeContractId</c> that does not resolve to a row in this call's own
/// freshly-fetched portfolio — wrong tenant, no linked document, deleted since the conversation was
/// opened — refuses outright (<see cref="ReplyKind.Refusal"/>, the w18 acceptance runbook's "N11")
/// before a single pack item is ever built, rather than silently falling back to an unscoped answer
/// for a caller who explicitly named a contract they can no longer see. <b>Lock 4</b> exempts
/// <see cref="AskIntent.PortfolioMarketPosition"/> from both rules: that intent is always
/// portfolio-wide by construction, so it never depends on the scoped contract resolving, and is
/// never narrowed to it either.
/// </para>
///
/// <para>
/// <b>Priced-line bands share one resolution with <c>/api/contracts/{id}/strategy</c></b> (task
/// E28/F01/US01/T01, NW-82; ADR-024 w17 clause 7 "one resolution per screen"):
/// <see cref="BuildRenewalStrategyPackAsync"/>/<see cref="BuildMarketComparePackAsync"/> both
/// resolve the (supplier name, geography) key through the same <see cref="BenchmarkKeyResolution"/>
/// <c>InsightsEndpointExtensions.GetContractStrategyAsync</c> already calls, then pass it to the
/// same async <c>ContractInsightsMapper.ToPricedLines</c> overload — never
/// <c>Contract.GoverningLaw</c> as a geography stand-in (the gap this doc comment used to name; a
/// contract still has no dedicated geography column, but the workspace's own country, the same
/// proxy <c>/strategy</c> already accepted as honest in ADR-024 w17 clause 8, is a real resolution,
/// not an invented one). An incomplete key (no <c>SupplierId</c>, an unresolved name, or no
/// workspace country) leaves every line's band unset, so both methods narrate "insufficient market
/// data" exactly as <c>/strategy</c> does for the identical gap — never a fabricated percentile.
/// R-STR-03 (status-aware renewal actions) is not read here — a follow-up, not attempted by this
/// task.
/// </para>
///
/// <para>
/// <b>Grounded negotiation points, shared between chat and Renewals</b> (task E31/F02/US01/T01,
/// NW-96; ADR-024 w19 cl. 23): <see cref="BuildNegotiationPointsPackAsync"/> maps this contract's
/// clause/risk/commercial snapshot (from <see cref="Contract360Result"/> — the one mapping only this
/// host may perform, Insights stays fenced to <c>[SharedKernel, Benchmark]</c>) into
/// <c>Raffa.Insights.Application.NegotiationPointRanker.Rank</c>, which emits a point only when it is
/// grounded in a stored fact, a clause, an assessed risk or a benchmark band — never the generic
/// seven-lever dump <see cref="Raffa.Insights.Negotiation.PricedLineNegotiationCalculator"/> used to
/// emit into the <c>RenewalStrategy</c> pack below (task E31/F01/US01/T01 retired that loop — see
/// <see cref="BuildRenewalStrategyPackAsync"/>'s own doc comment). The whole ranked set is upserted to
/// <see cref="RenewalNegotiationTodoService"/> when asked (<c>persistTodos</c> — "persist-all"), while
/// only the top three ever reach the returned pack (<c>PackItem</c>s — "chat top-3"): the two counts
/// deliberately differ, so a re-ask never re-litigates a point Procurement already ticked done.
/// <b>Live Q3 turn (E31/F01/US01/T01 q3-route + E29/F02/US01/T01 todo-host-upsert)</b>:
/// <see cref="BuildInDomainReplyAsync"/>'s <see cref="AskIntent.RenewalStrategy"/> named-contract
/// branch calls <see cref="BuildRenewalStrategyWithEvidenceAsync"/> (calc + tenant + market + raffa)
/// with <c>persistTodos: true</c>, so a real Q3 ask durably upserts before
/// <see cref="AnswerComposer.AnswerAsync"/> — the <c>/renewals?select={id}</c> deep-link is true
/// after asking. Feature-03 (q3-persist, NW-97) injects that turn's own server-side
/// <c>/renewals?select={id}</c> <see cref="CopilotActionKind.Navigate"/> action — see
/// <see cref="BuildInDomainReplyAsync"/>'s own <c>isQ3PersistTurn</c> branch, never
/// <c>composed.Value.Result.ActionKeys</c> (the model's own action keys).
/// </para>
///
/// <para>
/// <b>Notice pack is a structured fact, never a clause paraphrase</b> (task E30/F01/US01/T01,
/// NW-91/NW-92; ADR-024 — this task's own citation is "w19 cl. 22; lock 8", not yet folded into
/// this ADR's own amendment history on disk, so <c>reports/architecture/waves/w19.md</c>'s
/// NW-91/NW-92 rows are the verifiable source cited from the methods below instead of a clause
/// number this file cannot confirm): <see cref="NoticeFallbackReplies.BuildNoticeFallbackReplyAsync"/> answers a notice/preavviso/
/// disdetta question (<see cref="IntentPlanner.IsNoticeQuestion"/>, the one notice lexicon) from the
/// scoped contract's own <see cref="Contract360Renewal"/> fact and — only when this contract's own
/// extracted clauses name one — a matching-clause citation built from
/// <see cref="Contract360Result.Clauses"/>, never from <see cref="EmbeddingRetrievalService"/> (every
/// notice question is exactly the shape a full HTTP round trip already exercises under this
/// project's InMemory EF Core provider, which cannot translate
/// <c>Embedding.Vector.CosineDistance</c> — <c>InMemoryAskEngineFactory</c>'s own doc comment).
/// Every date is a <see cref="PackValueKind.Date"/> value, read straight off the fact and never
/// re-derived or model arithmetic (NW-92).
/// </para>
///
/// <para>
/// <b>Five server-decided notice fallbacks</b> (task E30/F02/US01/T01, NW-94; parent story
/// us-01-notice-fallbacks, "a scoped notice turn never asks 'which supplier'"; this task's own
/// citation is "ADR-024 w19 cl. 22", the same not-yet-folded-in clause number
/// the notice pack's doc comment noted, so
/// <c>reports/architecture/waves/w19.md</c>'s NW-94 row is again the verifiable source):
/// <see cref="NoticeFallbackReplies.BuildNoticeFallbackReplyAsync"/> intercepts every notice question — before any pack
/// is built and before <see cref="AnswerComposer"/> ever runs, the same short-circuit shape
/// <see cref="GateReplyBuilder.BuildRoutingOnlyReply"/> already uses for <see cref="AskIntent.Navigate"/>/
/// <see cref="AskIntent.QuoteRoute"/> — and decides one of five outcomes purely from facts
/// <see cref="NoticeFacts.BuildNoticeFactItem"/>/<see cref="NoticeFacts.BuildMatchingClauseItem"/> compute: a resolved deadline plus a
/// span-anchored clause answers with the deep-link (two-CTA ids, NW-83/NW-93); a deadline with no
/// span answers the date alone, never a fabricated page; no deadline but a matching clause quotes
/// it verbatim; neither abstains, naming the contract; and no contract resolved at all (an
/// unscoped notice question) abstains to Portfolio rather than ever guessing — or asking — which
/// supplier. Every action is <see cref="CapabilityRouting.ResolveActions"/>-sourced, never
/// model-authored, matching this whole file's own convention.
/// </para>
///
/// <para>
/// <b>Pre-existing, unrelated gap surfaced (not caused) by this task</b>: <c>IntentPlanner</c>'s own
/// <c>NoticePattern</c> (task E27/F01/US01/T01, NW-79 — landed before feature-01 and this task)
/// already steers any "notice period" phrasing to <see cref="AskIntent.StructuredFact"/>, never
/// <see cref="AskIntent.Clause"/>. <c>Raffa.IntegrationTests.AskRaffaRagCrossTenantIsolationTests</c>
/// therefore asks "what liability coverage do we have on file" (Clause RAG) on the messages-endpoint
/// and guard-audit cases, so those proofs stay on the retrieval path instead of the structured-notice
/// abstain.
/// </para>
/// </summary>
public sealed partial class AskCopilotService(
    DomainGate domainGate,
    PortfolioQueryService portfolioQueryService,
    ISupplierNameLookup supplierNameLookup,
    CapabilityCheckRunner capabilityCheckRunner,
    InterviewPlanner interviewPlanner,
    InterviewOptions interviewOptions,
    WebResearchFlow webResearchFlow,
    WebModeFlow webModeFlow,
    IAuditWriter auditWriter,
    GateReplyBuilder gateReplies,
    AbstainRecovery abstainRecovery,
    AskTurnAudit audit,
    StructuredFactPackBuilder structuredFacts,
    ClausePackBuilder clauses,
    MarketComparePackBuilder marketCompare,
    RenewalStrategyPackBuilder renewalStrategy,
    NegotiationPointsPackBuilder negotiationPoints,
    PortfolioStrategyPackBuilder portfolioStrategy,
    SavingsPackBuilder savings,
    NoticeFallbackReplies noticeFallbacks,
    InDomainTurnPlanner inDomainPlanner,
    AgentPhase agentPhase,
    AnswerPhase answerPhase,
    CapabilityGapTurnHandler capabilityGapTurn,
    ITenantContext tenantContext,
    IClock clock,
    InvestigatorTrigger? investigatorTriggerOverride = null)
{
    /// <summary>INV-01: the deterministic trigger of the capability investigator (the shared
    /// instance over the embedded lexicon unless a test supplies its own).</summary>
    private readonly InvestigatorTrigger investigatorTrigger = investigatorTriggerOverride ?? InvestigatorTrigger.Default;

    /// <summary>The tenant scope <see cref="AskAsync"/> already opened for this call
    /// (<see cref="ITenantContext.BeginScope"/>) — every private helper below reads this instead
    /// of threading a <c>TenantId</c> parameter through the whole call tree. Falls back to
    /// <see langword="default"/> only in the never-actually-reached case where a helper is somehow
    /// invoked outside that scope (fail-closed: <see langword="default"/> resolves to no rows
    /// under RLS, never another tenant's).</summary>
    private TenantId CurrentTenantId => tenantContext.Current ?? default;

    // Test seams that stay reachable through this type (F1 Ask split, PR-2): the bodies moved to
    // ClausePackBuilder / MarketComparePackBuilder; retargeted by the last mechanical PR.
    internal static (string? Href, string? PreviewUrl) ResolveTenantClauseLinks(
        Contract360Clause? clause, EntityId sourceId, Guid? namedContractId,
        string? hitSourceType = null, int? hitPage = null) =>
        ClausePackBuilder.ResolveTenantClauseLinks(clause, sourceId, namedContractId, hitSourceType, hitPage);

    internal static PackItem BuildClausePackItem(
        EmbeddingSearchResult hit, Contract360Clause? clause, Guid? namedContractId, bool isPeer) =>
        ClausePackBuilder.BuildClausePackItem(hit, clause, namedContractId, isPeer);

    internal Task<IReadOnlyList<PackItem>> BuildMarketComparePackAsync(
        PortfolioListItem? namedContractItem, CancellationToken cancellationToken) =>
        marketCompare.BuildMarketComparePackAsync(namedContractItem, cancellationToken);

    // Test seams (F1 Ask split, PR-3): the bodies moved to RenewalStrategyPackBuilder /
    // NegotiationPointsPackBuilder; retargeted by the last mechanical PR.
    internal Task<IReadOnlyList<PackItem>> BuildRenewalStrategyPackAsync(
        PortfolioListItem namedContractItem,
        CancellationToken cancellationToken,
        bool persistTodos = false,
        string actor = "") =>
        renewalStrategy.BuildRenewalStrategyPackAsync(namedContractItem, cancellationToken, persistTodos, actor);

    internal Task<IReadOnlyList<PackItem>> BuildNegotiationPointsPackAsync(
        EntityId contractId,
        bool includeRenewalUrgency,
        bool persistTodos,
        string actor,
        CancellationToken cancellationToken) =>
        negotiationPoints.BuildNegotiationPointsPackAsync(contractId, includeRenewalUrgency, persistTodos, actor, cancellationToken);

    /// <summary>
    /// Answers one Ask turn end to end: gate → (deterministic reply, or planner → pack → guarded
    /// answer). Writes exactly one audit row (R-ASK-09) before returning.
    /// </summary>
    /// <param name="tenantId">The caller's already-resolved, already-authorized tenant.</param>
    /// <param name="question">The user's question, in its own language.</param>
    /// <param name="recentTurns">The conversation's last N turns (oldest first) — empty for a new
    /// chat.</param>
    /// <param name="actor">The caller's resolved token subject (ADR-011 w16 clause 15) — required,
    /// no default, so a placeholder can never return by omission. Recorded on the one audit row
    /// this call writes (R-ASK-09).</param>
    /// <param name="scopeContractId">The conversation's own persisted
    /// <c>Conversation.ScopeContractId</c> (task E25/F03/US01/T01, NW-56; ADR-024 "the gate
    /// resolves the scope id before the R-ASK-10 check") — set when this conversation was opened
    /// from Contract 360's "Ask about it" (ADR-024 "citation landing... → /ask?scope="), null for
    /// the global Ask bar's always-unscoped new chat. See the scope-resolution step below (right
    /// after <c>gate</c> is classified) for what this does to the turn. Threaded on into
    /// <see cref="BuildInDomainReplyAsync"/> unchanged (task E27/F02/US01/T01, NW-76) so that
    /// method can tell "no scope" from "a scope that did not resolve" — this parameter alone, not
    /// the gate's already-resolved supplier name, is what lets a same-name portfolio hit lose to
    /// the real scoped id and an unseen id refuse (see this type's own doc comment).</param>
    /// <param name="capabilityCheck">ADR-031: when given, a fresh in-domain turn starts the
    /// capability check in it, beside the answer; the caller appends its follow-up after
    /// persisting the answer. <see langword="null"/>: no check at all.</param>
    /// <exception cref="ArgumentException"><paramref name="question"/> is null/blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="recentTurns"/> is <see langword="null"/>.</exception>
    public async Task<CopilotReply> AskAsync(
        TenantId tenantId,
        string question,
        IReadOnlyList<(string Role, string Markdown)> recentTurns,
        string actor,
        EntityId? scopeContractId = null,
        AskTurnHints? hints = null,
        bool previousRaffaTurnWasInterview = false,
        CapabilityCheckSlot? capabilityCheck = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(recentTurns);

        var turnHints = hints ?? AskTurnHints.None;

        using var scope = tenantContext.BeginScope(tenantId);

        // One id for the whole turn (plan T-01): every ask.* / chat.* audit row of the turn and every
        // ai.* row its AI calls write carry it, so a turn can be followed end to end.
        using var turn = RunContext.BeginTurn();
        using var run = RunContext.BeginRun();

        var portfolio = await portfolioQueryService
            .GetPortfolioAsync(tenantId, PortfolioFilter.None, new PortfolioPageRequest(1, PortfolioPageRequest.MaxPageSize), cancellationToken)
            .ConfigureAwait(false);

        var supplierIds = portfolio.Items
            .Where(item => item.SupplierId is not null)
            .Select(item => new EntityId(item.SupplierId!.Value))
            .Distinct()
            .ToList();

        var supplierNames = supplierIds.Count > 0
            ? await supplierNameLookup.GetNamesAsync(tenantId, supplierIds, cancellationToken).ConfigureAwait(false)
            : new Dictionary<EntityId, string>();

        // Task E27/F05/US01/T01 (NW-80): DomainGate cannot normalize a name itself (Raffa.Chat's
        // allow-list is [SharedKernel, AiGateway] -- it cannot reference Raffa.Suppliers.Products),
        // so this host -- the one place ADR-002 lets both sides be referenced -- runs the real
        // SupplierNameNormalizer once per known name and hands the gate both forms (see
        // KnownSupplierName's own doc comment).
        var knownSuppliers = supplierNames.Values
            .Select(name => new KnownSupplierName(name, SupplierNameNormalizer.Normalize(name)))
            .ToList();

        var gate = domainGate.Classify(question, knownSuppliers);

        // ADR-030: an interview option that named a supplier is a known supplier by construction
        // (the planner built it from this tenant's own portfolio), so it wins over the gate's own
        // free-text extraction exactly as a scoped entry does below. Greeting/OffDomain/Legal/
        // Capability still win outright.
        if (turnHints.ForcedSupplierName is { } forcedSupplierName && gate.Label is GateLabel.NeedsDocument or GateLabel.InDomain)
        {
            gate = gate with
            {
                Label = GateLabel.InDomain,
                Reason = $"interview resolution named known supplier '{forcedSupplierName}' (ADR-030).",
                NamedSupplier = forcedSupplierName,
            };
        }

        // AC-1/AC-2 (ADR-024 "the gate resolves the scope id before the R-ASK-10 check"; task
        // E25/F03/US01/T01, NW-56): a conversation opened from Contract 360's "Ask about it"
        // carries its own persisted Conversation.ScopeContractId — ConversationsEndpointExtensions
        // .AskAndAppendAsync threads it into scopeContractId above — naming one contract this
        // tenant already validated. Resolving its supplier name and folding it into the gate's own
        // NamedSupplier BEFORE the reply switch below ever runs is what scopes every downstream
        // consumer (IntentPlanner.Plan, and every BuildXxxPackAsync that resolves
        // `namedContractItem` from it) "via the existing namedSupplier/scope path" (this task's own
        // coding objective) with no further change to any of them — and what keeps a scoped turn
        // from ever landing on the generic GateLabel.NeedsDocument redirect (some unrelated
        // capitalized word the question happens to contain) or an unscoped GateLabel.InDomain (no
        // supplier named at all, so a downstream pack would otherwise fall back to a portfolio-wide
        // answer instead of this contract's own — AC-3). Only the gate's own supplier-resolution
        // rule is affected this way; Greeting/OffDomain/Legal/Capability do not key off a named
        // supplier and still win outright, exactly as DomainGate.Classify's own deterministic-first
        // ordering already intends. A scope id that does not resolve to a portfolio row, or
        // resolves to one with no known supplier name yet (SupplierId unset, or the lookup has no
        // name for it), changes nothing here: the gate's own free-text extraction still decides,
        // same as before this task.
        var scopedContractItem = scopeContractId is { } scopedContractIdValue
            ? portfolio.Items.FirstOrDefault(item => item.ContractId == scopedContractIdValue.Value)
            : null;

        var scopedSupplierName = scopedContractItem?.SupplierId is { } scopedSupplierId &&
            supplierNames.TryGetValue(new EntityId(scopedSupplierId), out var resolvedScopedSupplierName)
                ? resolvedScopedSupplierName
                : null;

        // ADR-030: a capability-gap turn keeps its own label but takes the scoped supplier the same
        // way, so a chat opened from Contract 360 drafts for that contract.
        if (scopedSupplierName is not null && gate.Label is GateLabel.NeedsDocument or GateLabel.InDomain or GateLabel.CapabilityGap)
        {
            gate = gate with
            {
                Label = gate.Label == GateLabel.CapabilityGap ? GateLabel.CapabilityGap : GateLabel.InDomain,
                Reason = $"scoped entry resolved to known supplier '{scopedSupplierName}' before the " +
                    "gate's own free-text extraction decided (ADR-024, task E25/F03/US01/T01).",
                NamedSupplier = scopedSupplierName,
            };
        }

        // ADR-031 / INV-02: Raffa's own judgement on whether this turn asks for an operation nothing
        // in Raffa performs. It runs beside the answer, never in front of it, and since decision D3
        // it costs a model call only when the deterministic InvestigatorTrigger says the turn is
        // worth one (Chat:GapInvestigation:Mode = Triggered, the default; Always keeps the old
        // call-on-every-turn behaviour for diagnosis):
        //   - T3 (an operational request the catalog does not cover) reads the question alone, so
        //     the check starts HERE and runs in parallel with the answer, as it always did;
        //   - T1 (no intent recognised) and T2 (Raffa could not answer) need the planner and the
        //     composer, so they are decided right after the reply below, and the check starts then:
        //     the answer is never delayed, and the follow-up still lands as a separate message
        //     through CapabilityCheckRunner.AppendWhenDone.
        // Only a fresh, typed InDomain turn is eligible: the fixed catalog already had its say in
        // the gate; Greeting/OffDomain/Legal/Capability/NeedsDocument make no AI Gateway call and
        // stay that way (R-ASK-02/03, the golden set's zero-call cases); a turn resolved by key (an
        // interview option, a web consent) continues one that was already checked; and only a
        // caller that will persist a follow-up (the conversation endpoints) passes a slot.
        var gapInvestigation = "skipped";
        var turnTrace = new InDomainTurnTrace();
        var investigatorEligible = false;
        var investigatorStarted = false;
        var investigatorMode = GapInvestigationMode.Triggered;
        var triggerVerdict = TriggerVerdict.None;
        // One id per turn, shared with the run telemetry (RunContext) so every audit row of the turn agrees.
        var turnId = RunContext.Current?.TurnId ?? RunContext.NewId();

        void StartInvestigator()
        {
            var (namedItem, _, _) = ResolveNamedContractItem(scopedContractItem, gate.NamedSupplier, portfolio, supplierNames);
            var namedSupplier = namedItem?.SupplierId is { } namedSupplierId &&
                supplierNames.TryGetValue(new EntityId(namedSupplierId), out var namedSupplierName)
                    ? namedSupplierName
                    : null;

            capabilityCheck!.FollowUp = capabilityCheckRunner.Start(new CapabilityCheckRequest(
                tenantId,
                question,
                supplierNames.Values.ToList(),
                portfolio.TotalCount,
                namedSupplier,
                namedItem?.ContractId,
                turnId,
                actor));
            investigatorStarted = true;
            gapInvestigation = "started";
        }

        var investigatorTurn = capabilityCheck is not null && gate.Label == GateLabel.InDomain && IsFreshTurn(turnHints);
        if (investigatorTurn)
        {
            capabilityCheck!.TurnId = turnId;

            if (!capabilityCheckRunner.Enabled)
            {
                gapInvestigation = "off";
                triggerVerdict = new TriggerVerdict(false, false, false, InvestigatorTrigger.ReasonKillSwitch);
            }
            else
            {
                investigatorEligible = true;
                investigatorMode = capabilityCheckRunner.Mode;

                // T3 reads the question alone: it (and Always mode) starts the check now, in
                // parallel with the answer. T1/T2 are decided after the reply, below.
                if (investigatorMode == GapInvestigationMode.Always ||
                    investigatorTrigger.Evaluate(plan: null, replyOutcome: null, question).ShouldRun)
                {
                    StartInvestigator();
                }
            }
        }

        // AC-7 / R-ASK-09: the audit row must distinguish "guard caught a violation and downgraded
        // to abstain" from every other abstain path (empty pack, gateway call failed) — so every
        // branch here carries its own guardIntervened alongside the reply, rather than WriteAuditAsync
        // inferring it from ReplyKind alone (which cannot tell the two apart; see
        // BuildInDomainReplyAsync's own doc comment on its tuple return).
        // ADR-032: the composer's web-search toggle takes every question that is a search (see
        // IsWebModeTurn) to the web and to the store, the procurement-only filters lifted.
        var (reply, guardIntervened, fallbackUsed) = WebModeFlow.IsWebModeTurn(turnHints, gate, question)
            ? await webModeFlow.BuildReplyAsync(
                tenantId, question, gate, portfolio, scopedContractItem, actor,
                (webQuestion, namedSupplier, ct) => BuildInDomainReplyAsync(
                    tenantId,
                    webQuestion,
                    namedSupplier,
                    portfolio,
                    supplierNames,
                    recentTurns,
                    scopeContractId,
                    scopedContractItem,
                    actor,
                    AskTurnHints.None with { SuppressInterview = true },
                    previousRaffaTurnWasInterview: false,
                    ct),
                abstainRecovery.Resolver,
                cancellationToken)
                .ConfigureAwait(false)
            : gate.Label switch
            {
                GateLabel.Greeting or GateLabel.OffDomain => (gateReplies.BuildGreetingReply(portfolio, supplierNames), false, false),
                GateLabel.Legal => (gateReplies.BuildLegalReply(portfolio, supplierNames, gate.NamedSupplier), false, false),
                GateLabel.Capability => (gateReplies.BuildCapabilityReply(portfolio.Items.Count), false, false),
                GateLabel.NeedsDocument => (gateReplies.BuildNeedsDocumentReply(gate.NamedSupplier!, portfolio.Items.Count), false, false),
                GateLabel.CapabilityGap => await capabilityGapTurn.BuildCapabilityGapReplyAsync(
                    tenantId, question, gate, portfolio, supplierNames, scopeContractId, scopedContractItem, actor, cancellationToken)
                    .ConfigureAwait(false),
                GateLabel.InDomain => await BuildInDomainReplyAsync(
                    tenantId, question, gate.NamedSupplier, portfolio, supplierNames, recentTurns,
                    scopeContractId, scopedContractItem, actor, turnHints, previousRaffaTurnWasInterview, cancellationToken,
                    turnTrace)
                    .ConfigureAwait(false),
                _ => throw new ArgumentOutOfRangeException(nameof(gate), gate.Label, "Unknown GateLabel."),
            };

        // INV-02: T1 (the planner found no intent) and T2 (Raffa could not answer) are known only
        // now. The check starts after the reply is built — never before it, so the answer's
        // latency is unchanged — and the endpoint appends its follow-up as a separate message.
        if (investigatorEligible)
        {
            // The full verdict (T3 again, now with what T1 and T2 found), also in Always mode: the
            // audit then shows what Triggered would have done, which is how the reduction in
            // gaps-v1 calls is measured (plan 5.4).
            triggerVerdict = investigatorTrigger.Evaluate(
                turnTrace.Plan, new ReplyOutcome(reply.Kind, guardIntervened, fallbackUsed), question);

            if (!investigatorStarted)
            {
                if (triggerVerdict.ShouldRun)
                {
                    StartInvestigator();
                }
                else
                {
                    gapInvestigation = "not-triggered";
                }
            }
        }

        await audit.WriteAsync(tenantId, reply, guardIntervened, fallbackUsed, gapInvestigation, turnHints.WebMode, actor, cancellationToken)
            .ConfigureAwait(false);

        if (investigatorTurn)
        {
            await WriteTriggerAuditAsync(
                    tenantId, actor, turnId, investigatorEligible ? investigatorMode.ToString() : "off",
                    triggerVerdict, investigatorStarted, cancellationToken)
                .ConfigureAwait(false);
        }

        // ADR-030: a declined consent is audited beside the turn it became (the contracts-only
        // answer above), so "asked, said no" is visible without the query ever being logged.
        if (turnHints.DeclinedWebResearch)
        {
            await webResearchFlow.WriteDeclinedAuditAsync(tenantId, actor, cancellationToken)
                .ConfigureAwait(false);
        }

        return reply;
    }

    // ----- In-domain: planner -> pack -> guarded answer (R-ASK-03...08) -----

    /// <summary>
    /// Returns the reply alongside whether <see cref="AnswerComposer"/>'s own guard pipeline ever
    /// intervened for this turn (<see cref="AnswerComposerResult.GuardIntervened"/>) — folded into
    /// the audit entry by <see cref="AskAsync"/> as AC-7's <c>abstainGuardIntervened</c>. Only the
    /// final <c>composed.Value.GuardIntervened</c> branch below can ever be <see langword="true"/>:
    /// the routing-only reply and the empty-pack/gateway-failure abstains never called
    /// <see cref="AnswerComposer.AnswerAsync"/> at all, so there was no guard verdict to intervene —
    /// reporting <see langword="false"/> for those is what actually lets an operator tell "the pack
    /// was empty" and "the answer failed to reach the gateway" apart from a real guard catch, which
    /// a single boolean derived from <see cref="ReplyKind"/> alone could never do (every one of
    /// these paths returns <see cref="ReplyKind.Abstain"/> identically).
    ///
    /// <para>
    /// <b>Which contract "named" resolves to (task E27/F05/US01/T01, NW-80)</b>: see
    /// <see cref="ResolveNamedContractItem"/> for the scoped-id-wins / soonest-cancellation-deadline
    /// rule that replaced a plain <c>FirstOrDefault</c> here, and never silently merges multiple
    /// contracts under one supplier without saying so.
    /// </para>
    /// </summary>
    /// <param name="scopeContractId">Echoes <see cref="AskAsync"/>'s own parameter of the same name
    /// — <see langword="null"/> for an unscoped turn. Only used to tell that case apart from "a
    /// scope was set but did not resolve" below; the id itself is <paramref name="scopedContractItem"/>.</param>
    /// <param name="scopedContractItem">The portfolio row <paramref name="scopeContractId"/>
    /// resolved to in this call's own freshly-fetched <paramref name="portfolio"/>
    /// (<see langword="null"/> when <paramref name="scopeContractId"/> is itself
    /// <see langword="null"/>, <em>or</em> when it named an id this tenant cannot currently see —
    /// wrong tenant, no linked document, deleted since the conversation was opened). Task
    /// E27/F02/US01/T01 (NW-76; ADR-024 w19 cl. 12; lock 4) — see this type's own doc comment.</param>
    /// <param name="actor">Echoes <see cref="AskAsync"/>'s own required, no-default <c>actor</c>
    /// (the caller's resolved token subject, ADR-011 w16 §15) — threaded down only so the
    /// <see cref="AskIntent.RenewalStrategy"/> named-contract branch can pass it on to
    /// <see cref="BuildNegotiationPointsPackAsync"/>'s own required <c>actor</c> parameter whenever
    /// <c>persistTodos: true</c> (task E29/F02/US01/T01, todo-host-upsert). Never the model.</param>
    private async Task<(CopilotReply Reply, bool GuardIntervened, bool FallbackUsed)> BuildInDomainReplyAsync(
        TenantId tenantId,
        string question,
        string? namedSupplier,
        PortfolioPage portfolio,
        IReadOnlyDictionary<EntityId, string> supplierNames,
        IReadOnlyList<(string Role, string Markdown)> recentTurns,
        EntityId? scopeContractId,
        PortfolioListItem? scopedContractItem,
        string actor,
        AskTurnHints hints,
        bool previousRaffaTurnWasInterview,
        CancellationToken cancellationToken,
        InDomainTurnTrace? trace = null)
    {
        var inDomainTurn = inDomainPlanner.Plan(
            new AskTurnScope(
                tenantId, question, namedSupplier, recentTurns, actor, portfolio, supplierNames,
                scopeContractId, scopedContractItem, hints, previousRaffaTurnWasInterview),
            trace,
            out var refusal);
        if (inDomainTurn is null)
        {
            return (refusal!, false, false);
        }

        var plan = inDomainTurn.Plan;
        var forcedContractItem = inDomainTurn.ForcedContractItem;
        var namedContractItem = inDomainTurn.NamedContractItem;
        var disambiguationItem = inDomainTurn.DisambiguationItem;
        var supplierMatches = inDomainTurn.SupplierMatches;
        var routingContext = inDomainTurn.RoutingContext;

        // ADR-030 -- web research. A consumed consent runs the research role here, before any pack
        // is built (nothing of the tenant's data is in scope of that call, by construction); an
        // explicit or forced WebResearch intent gets the consent question, or a redirect naming
        // the closed gate. There is no path from here to the search without a consumed consent.
        if (hints.AuthorizedWebResearch is { } authorizedWebResearch && plan.Intent == AskIntent.WebResearch)
        {
            var reply = await webResearchFlow.RunAsync(tenantId, question, authorizedWebResearch, portfolio, routingContext, actor, abstainRecovery.Resolver, cancellationToken)
                .ConfigureAwait(false);
            return (reply.Reply, reply.GuardIntervened, false);
        }

        if (plan.Intent == AskIntent.WebResearch)
        {
            return (
                await webResearchFlow.BuildEntryReplyAsync(tenantId, question, portfolio, routingContext, actor, abstainRecovery.Resolver, cancellationToken)
                    .ConfigureAwait(false),
                false,
                false);
        }

        if (plan.Intent is AskIntent.Navigate or AskIntent.QuoteRoute)
        {
            return (gateReplies.BuildRoutingOnlyReply(plan, routingContext), false, false);
        }

        // ADR-030 — the interview. Decided here, after scope and supplier resolution and before
        // any pack is built, so an ambiguous turn retrieves nothing and calls no model. An answer
        // to an interview (hints.SuppressInterview) never triggers a second one, and neither does
        // a turn that directly follows one.
        if (interviewOptions.Enabled && !hints.SuppressInterview)
        {
            var interviewContext = new InterviewContext(
                HasScope: scopeContractId is not null || forcedContractItem is not null,
                NamedSupplierContractCount: supplierMatches.Count,
                PortfolioIsEmpty: portfolio.Items.Count == 0,
                PreviousRaffaTurnWasInterview: previousRaffaTurnWasInterview,
                IsNoticeQuestion: IntentPlanner.IsNoticeQuestion(question));
            var signals = AmbiguityDetector.Detect(question, plan, interviewContext, interviewOptions);
            if (signals.Verdict == AmbiguityVerdict.Ambiguous)
            {
                // The web option rides the interpretation menu only when every ADR-030 gate is
                // open and the question is a procurement topic; picking it asks consent, never
                // searches.
                var webOffer = await webResearchFlow.BuildOfferAsync(tenantId, question, cancellationToken).ConfigureAwait(false);
                var interview = interviewPlanner.Plan(
                    question, plan, signals, BuildInterviewInputs(supplierMatches, portfolio, supplierNames), webOffer);
                if (interview is not null)
                {
                    return (InterviewReplyBuilder.Interview(interview), false, false);
                }
            }
        }

        // Task E30/F02/US01/T01 (NW-94): the five notice fallbacks are fully server-decided, so a
        // notice question never reaches AnswerComposer/the AI gateway at all -- intercepted here,
        // before any pack is built, the same short-circuit shape the Navigate/QuoteRoute branch
        // above already uses. Deliberately checked before namedContractItem is known to be
        // resolved or not: BuildNoticeFallbackReplyAsync itself is what tells "a resolved contract"
        // (cases 1-4) apart from "an unscoped notice question" (case 5, NW-94's own "unscoped
        // deictic" abstain), so both must reach it rather than only the scoped half. Because every
        // notice question leaves here, the pack switch below never sees one: the structured-fact
        // pack is the plain one, with no notice branch of its own.
        if (plan.Intent == AskIntent.StructuredFact && IntentPlanner.IsNoticeQuestion(question))
        {
            return (
                await noticeFallbacks.BuildNoticeFallbackReplyAsync(question, namedContractItem, disambiguationItem, routingContext, cancellationToken)
                    .ConfigureAwait(false),
                false,
                false);
        }

        var packItems = plan.Intent switch
        {
            AskIntent.StructuredFact => await structuredFacts.BuildStructuredFactPackAsync(question, namedContractItem, portfolio, supplierNames, cancellationToken)
                .ConfigureAwait(false),
            AskIntent.Clause => await clauses.BuildClausePackAsync(tenantId, question, namedContractItem, cancellationToken)
                .ConfigureAwait(false),
            AskIntent.MarketCompare => await marketCompare.BuildMarketComparePackAsync(namedContractItem, cancellationToken).ConfigureAwait(false),
            AskIntent.RenewalStrategy => namedContractItem is not null
                ? await renewalStrategy.BuildRenewalStrategyWithEvidenceAsync(tenantId, question, namedContractItem, actor, cancellationToken, plan.Goal).ConfigureAwait(false)
                : await portfolioStrategy.BuildPortfolioStrategyPackAsync(portfolio, supplierNames, cancellationToken).ConfigureAwait(false),
            AskIntent.PortfolioStrategy => await portfolioStrategy.BuildPortfolioStrategyPackAsync(portfolio, supplierNames, cancellationToken).ConfigureAwait(false),
            AskIntent.PortfolioSavingsTarget => await savings.BuildPortfolioSavingsTargetPackAsync(portfolio, plan.Goal, cancellationToken).ConfigureAwait(false),
            AskIntent.Savings => namedContractItem is not null
                ? await savings.BuildSavingsLeverPackAsync(tenantId, namedContractItem, plan.Goal, actor, cancellationToken).ConfigureAwait(false)
                : await portfolioStrategy.BuildPortfolioStrategyPackAsync(portfolio, supplierNames, cancellationToken).ConfigureAwait(false),
            AskIntent.DocumentStatus => DocumentStatusPack.Build(portfolio, supplierNames),
            _ => [],
        };

        // NW-80 "never silently merge": prepended, not appended, so PackBudget.Apply (which always
        // keeps at least its first item) and FixtureAiGateway.AnswerFromPack (which cites only the
        // first few pack items) can never drop the one sentence telling the user which contract was
        // chosen among several.
        if (disambiguationItem is not null)
        {
            packItems = packItems.Prepend(disambiguationItem).ToList();
        }

        var agents = await agentPhase.RunAsync(
                question,
                plan.Goal,
                packItems,
                new AgentPhaseRequest(
                    plan.Intent is AskIntent.DocumentStatus or AskIntent.Clause ? []
                    : namedContractItem is not null ? [namedContractItem]
                    : MarketDataCheck.ContractsForDataCheck(packItems, portfolio),
                    IntentPolicy.RunsMarketResearch(plan.Intent, portfolio),
                    IntentPolicy.IsCouncilIntent(plan.Intent, namedContractItem),
                    AppendCalendarItem: true),
                cancellationToken)
            .ConfigureAwait(false);

        return await answerPhase.ComposeAsync(inDomainTurn, agents, cancellationToken).ConfigureAwait(false);
    }

    private static string ComputeHash(string input) => QueryHasher.ComputeHash(input);
}
