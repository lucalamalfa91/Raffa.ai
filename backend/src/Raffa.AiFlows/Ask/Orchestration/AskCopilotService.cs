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
    IntentPlanner intentPlanner,
    AnswerComposer answerComposer,
    PackBudget packBudget,
    CapabilityRouting capabilityRouting,
    PortfolioQueryService portfolioQueryService,
    Contract360QueryService contract360QueryService,
    EmbeddingRetrievalService embeddingRetrievalService,
    RenewalEngine renewalEngine,
    PriorityScoreCalculator priorityScoreCalculator,
    RenewalNegotiationTodoService renewalNegotiationTodoService,
    CriticalityScoreCalculator criticalityScoreCalculator,
    SavingsOpportunityService savingsOpportunityService,
    ISupplierNameLookup supplierNameLookup,
    IMarketDealLookup marketDealLookup,
    AskAgentFlow askAgentFlow,
    NegotiationDraftingWorkflow negotiationDraftingWorkflow,
    CapabilityCheckRunner capabilityCheckRunner,
    InterviewPlanner interviewPlanner,
    InterviewOptions interviewOptions,
    WebResearchFlow webResearchFlow,
    WebModeFlow webModeFlow,
    IAuditWriter auditWriter,
    GateReplyBuilder gateReplies,
    AbstainRecovery abstainRecovery,
    AskTurnAudit audit,
    ContractContextReader context,
    StructuredFactPackBuilder structuredFacts,
    ClausePackBuilder clauses,
    MarketComparePackBuilder marketCompare,
    NoticeFallbackReplies noticeFallbacks,
    ITenantContext tenantContext,
    IClock clock,
    InvestigatorTrigger? investigatorTriggerOverride = null)
{
    /// <summary>INV-01: the deterministic trigger of the capability investigator (the shared
    /// instance over the embedded lexicon unless a test supplies its own).</summary>
    private readonly InvestigatorTrigger investigatorTrigger = investigatorTriggerOverride ?? InvestigatorTrigger.Default;

    private const int MaxPricedLinesForBenchmark = MarketComparePackBuilder.MaxPricedLinesForBenchmark;
    private const int PortfolioStrategyTopN = 5;

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
                GateLabel.CapabilityGap => await BuildCapabilityGapReplyAsync(
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
        // A bare follow-up ("non mi hai risposto", "e quindi?") is planned on the previous user
        // question too, so it inherits that turn's intent and saving goal instead of falling
        // through to the StructuredFact default (see IntentPlanner.Plan's own doc comment).
        var previousUserQuestion = recentTurns
            .LastOrDefault(turn => string.Equals(turn.Role, "you", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase))
            .Markdown;

        var plan = hints.ForcedIntent is { } forcedIntent
            ? intentPlanner.Plan(question, namedSupplier, previousUserQuestion, forcedIntent)
            : intentPlanner.Plan(question, namedSupplier, previousUserQuestion);

        // INV-02: hand the plan to the caller, which decides trigger T1 (no intent recognised)
        // from it once the reply exists. Observation only: nothing below reads the trace.
        if (trace is not null)
        {
            trace.Plan = plan;
        }

        // ADR-030: an interview option that named one contract narrows this turn to it exactly as
        // a scoped conversation would; an id this tenant cannot see refuses (the NW-76 posture),
        // never silently widens into a portfolio-wide answer.
        PortfolioListItem? forcedContractItem = null;
        if (hints.ForcedContractId is { } forcedContractId)
        {
            forcedContractItem = portfolio.Items.FirstOrDefault(item => item.ContractId == forcedContractId.Value);
            if (forcedContractItem is null && plan.Intent != AskIntent.PortfolioMarketPosition)
            {
                return (gateReplies.BuildUnseenScopeRefusal(portfolio), false, false);
            }
        }

        // AC-3, lock 4 (task E27/F02/US01/T01, NW-76): a scope id present on this conversation but
        // absent from this turn's own freshly-fetched portfolio refuses outright, before a single
        // BuildXxxPackAsync call below ever runs (no pack leak) -- never a silent fall-through to
        // an unscoped answer for a caller who explicitly named a contract they can no longer see.
        // PortfolioMarketPosition is exempt: lock 4 already makes it portfolio-wide regardless of
        // scope, so it never depends on the scoped contract resolving at all.
        if (scopeContractId is not null && scopedContractItem is null && plan.Intent != AskIntent.PortfolioMarketPosition)
        {
            return (gateReplies.BuildUnseenScopeRefusal(portfolio), false, false);
        }

        // AC-2 (NW-76) / NW-80 (task E27/F05/US01/T01): a resolved, visible scope id wins outright
        // over a same-name portfolio lookup -- two rows can share one supplier's display name, and
        // picking whichever one a name match happens to hit first would silently answer about the
        // wrong contract. Lock 4: PortfolioMarketPosition is never narrowed to the scoped contract,
        // so it alone passes null through here and falls back to ResolveNamedContractItem's own
        // name-based, soonest-cancellation-deadline lookup below (which still applies for every
        // other, unscoped turn, and for every turn once no scope resolved at all).
        var (namedContractItem, disambiguationItem, supplierMatches) = ResolveNamedContractItem(
            plan.Intent == AskIntent.PortfolioMarketPosition ? null : (scopedContractItem ?? forcedContractItem),
            plan.NamedSupplier, portfolio, supplierNames);

        // AC-2: follows namedContractItem above, so a scoped turn's follow-up actions (e.g. "open
        // this contract") also target the real scoped id, never a same-name-but-wrong contract.
        var contractIdForActions = namedContractItem is not null ? new EntityId(namedContractItem.ContractId) : (EntityId?)null;
        var routingContext = new RoutingContext(portfolio.TotalCount, CapabilityCallerRole.Standard, contractIdForActions);

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
                ? await BuildRenewalStrategyWithEvidenceAsync(tenantId, question, namedContractItem, actor, cancellationToken, plan.Goal).ConfigureAwait(false)
                : await BuildPortfolioStrategyPackAsync(portfolio, supplierNames, cancellationToken).ConfigureAwait(false),
            AskIntent.PortfolioStrategy => await BuildPortfolioStrategyPackAsync(portfolio, supplierNames, cancellationToken).ConfigureAwait(false),
            AskIntent.PortfolioSavingsTarget => await BuildPortfolioSavingsTargetPackAsync(portfolio, plan.Goal, cancellationToken).ConfigureAwait(false),
            AskIntent.Savings => namedContractItem is not null
                ? await BuildSavingsLeverPackAsync(tenantId, namedContractItem, plan.Goal, actor, cancellationToken).ConfigureAwait(false)
                : await BuildPortfolioStrategyPackAsync(portfolio, supplierNames, cancellationToken).ConfigureAwait(false),
            AskIntent.DocumentStatus => DocumentStatusPack.Build(portfolio, supplierNames),
            _ => [],
        };

        // Task E31/F03/US01/T01 (q3-persist; NW-97; ADR-024 w19 cl. 21/ADR-028): exactly the
        // condition the switch above already used to pick the live Q3 composition
        // (BuildRenewalStrategyWithEvidenceAsync, persistTodos: true) -- re-read, never
        // re-derived, so the server-injected Renewals action below can never fire for a turn that
        // did not just upsert. See the injected-action block near this method's own return.
        var isQ3PersistTurn = plan.Intent == AskIntent.RenewalStrategy && namedContractItem is not null;

        // NW-80 "never silently merge": prepended, not appended, so PackBudget.Apply (which always
        // keeps at least its first item) and FixtureAiGateway.AnswerFromPack (which cites only the
        // first few pack items) can never drop the one sentence telling the user which contract was
        // chosen among several.
        if (disambiguationItem is not null)
        {
            packItems = packItems.Prepend(disambiguationItem).ToList();
        }

        // Ask's agentic flow (Raffa.AiFlows.Negotiation.Orchestration.AskAgentFlow), one coordinated sequence
        // before the answer role writes (persona v2.5: say what is missing, answer from the market):
        //   1. market data check (deterministic, below): for the contract the turn is about -- or,
        //      on a multi-contract turn (a quarter, savings across contracts), the ones the pack is
        //      about -- which fields are missing and what the market data says in their place;
        //   2. market researcher (agent): queries the market RAG for what is still missing, the
        //      same supplier first, then similar or related contracts;
        //   3. negotiation council (agents), on a savings or negotiation turn: two analysts over the
        //      enriched pack, then the strategist's ranked plays.
        // Steps 1-2 append market items after the intent's own (so those keep their priority under
        // the budget); step 3's plays go right after the target verdict. Not a clause or
        // document-status turn's data check: the market holds no clause text to stand in for the
        // contract's own. A failed agent degrades the flow, never the turn.
        var dataChecks = new List<MarketSafetyNet.ContractCheck>();
        IReadOnlyList<PortfolioListItem> dataCheckContracts =
            plan.Intent is AskIntent.DocumentStatus or AskIntent.Clause ? []
            : namedContractItem is not null ? [namedContractItem]
            : ContractsForDataCheck(packItems, portfolio);

        var flow = await askAgentFlow.RunAsync(
                new AskFlowRequest(
                    question,
                    packItems,
                    plan.Goal,
                    dataCheckContracts.Count > 0 ? ct => RunMarketDataCheckAsync(dataCheckContracts, dataChecks, ct) : null,
                    RunMarketResearch: portfolio.Items.Count > 0 && plan.Intent != AskIntent.DocumentStatus,
                    ConveneCouncil: IsCouncilIntent(plan.Intent, namedContractItem)),
                cancellationToken)
            .ConfigureAwait(false);

        // F2-T01: the flow's steps, failures and market queries (names and counts) ride on the turn's one audit row.
        RunContext.AddTurnDetail(flow.ToAuditDetail());

        if (flow.MarketItems.Count > 0)
        {
            packItems = [.. packItems, .. flow.MarketItems];
        }

        if (flow.CouncilItems.Count > 0)
        {
            packItems = InsertCouncilItems(packItems, flow.CouncilItems);
        }

        var boundedPack = packBudget.Apply(packItems);

        // A question about a period ("this quarter", "fine trimestre") gets today's date and the
        // calendar quarter as one more citable item -- appended after the budget (it is a few
        // dozen tokens) so the model can state the quarter's end as a pack value instead of
        // declining or computing a date NumericGuard would reject.
        if (boundedPack.Count > 0 && CalendarPackItem.IsPeriodQuestion(question))
        {
            boundedPack = [.. boundedPack, CalendarPackItem.Build(DateOnly.FromDateTime(clock.UtcNow.UtcDateTime))];
        }

        // Every turn that cannot end in a grounded answer still ends in a proposal (persona v2.4:
        // Ask never answers "I don't have data I trust enough"): the screen where this kind of
        // answer lives, two questions that route to a real answer (ADR-024 "every abstain has a
        // clickable next step"), and HelpfulFallbackAnswer's way forward for this kind of question.
        var recoveryActions = abstainRecovery.ResolveAbstainRecoveryActions(portfolio, routingContext, plan.Intent);
        var recoveryFollowUps = AbstainRecovery.AbstainFollowUps(question, portfolio, plan.Intent);
        // The deterministic proposal opens, like the model must, with the honest gaps the market data
        // check found and what the market says in their place (step 1 of the flow above).
        var italianQuestion = HelpfulFallbackAnswer.IsItalian(question);
        var lead = MarketSafetyNet.Lead(dataChecks, italianQuestion);
        if (lead is null && plan.Intent == AskIntent.Clause && namedContractItem is not null && boundedPack.Count == 0)
        {
            // Honest first: the clause is not on file for this contract.
            var clauseSupplier = await context.ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false);
            lead = MarketSafetyNet.ClauseNotFoundLead(clauseSupplier, italianQuestion);
        }

        var proposal = (lead ?? string.Empty) +
            HelpfulFallbackAnswer.Proposal(question, AbstainRecovery.ProposalArea(question, plan.Intent), emptyWorkspace: portfolio.Items.Count == 0);

        if (boundedPack.Count == 0)
        {
            // ADR-027 §D8 (task E16/F02/US03/T01): no number here. `portfolio.Items.Count` is the
            // UNFILTERED portfolio -- every bootstrap shell a still-processing document created --
            // so rendering it as "N validated contract(s)" asserted a fabricated fact to the user.
            // The one definition of "validated" is ADR-026 §D2's CountValidatedContractsAsync; the
            // pre-question off-state already renders that number from the server, and the
            // proposal needs none.
            return (new CopilotReply(
                ReplyKind.Abstain, proposal, [], recoveryActions, ReplyProvenance.NoModelCall([]), recoveryFollowUps), false, false);
        }

        // ADR-030 stage 3: a decline the model explains as an ambiguous question is kept (not
        // regenerated) so the interpretation menu below can offer the readings instead.
        bool KeepDecline(string? reason) =>
            interviewOptions.Enabled
            && interviewOptions.ConvertAmbiguousAbstain
            && !hints.SuppressInterview
            && !previousRaffaTurnWasInterview
            && AmbiguousAbstainDetector.IsAmbiguous(reason);

        var composed = await answerComposer.AnswerAsync(question, boundedPack, recentTurns, KeepDecline, cancellationToken)
            .ConfigureAwait(false);

        if (composed.IsFailure)
        {
            return (new CopilotReply(
                ReplyKind.Abstain,
                HelpfulFallbackAnswer.ServiceBusy(question),
                [], recoveryActions, ReplyProvenance.NoModelCall([]), recoveryFollowUps), false, false);
        }

        var result = composed.Value.Result;

        // ADR-030 stage 3: the model itself says the question was ambiguous -- offer the
        // interpretation menu (deterministic options, never the model's) instead of the abstain
        // block with its dead "Open Ask Raffa" button.
        if (!result.CanDetermine && !composed.Value.GuardIntervened && KeepDecline(result.AbstainReason))
        {
            var webOffer = await webResearchFlow.BuildOfferAsync(tenantId, question, cancellationToken).ConfigureAwait(false);
            var interview = interviewPlanner.PlanInterpretationMenu(question, plan, webOffer);
            if (interview is not null)
            {
                return (InterviewReplyBuilder.Interview(interview), false, false);
            }
        }

        // The composed-from-the-pack answer is only as relevant as the pack. A structured question
        // the deterministic planner could not handle gets a stand-in snapshot of the soonest-ending
        // contracts (no calc:structured-query item) -- quoting that back would answer a question
        // that was not asked (GAP-ASK-UNANSWERABLE-STILL-ANSWERED), so that one case stays an
        // honest abstain.
        if (composed.Value.FallbackUsed && plan.Intent == AskIntent.StructuredFact && namedContractItem is null &&
            !boundedPack.Any(item => item.CitationKey == InsightsCitationKeys.Calc("structured-query")))
        {
            return (
                new CopilotReply(
                    ReplyKind.Abstain,
                    proposal,
                    [],
                    recoveryActions,
                    new ReplyProvenance([], result.Metadata.ModelId, result.Metadata.PromptVersion, result.Metadata.InputHash),
                    recoveryFollowUps),
                true,
                false);
        }

        // Still a decline after the regenerate-once pass (or one kept for an interview that did
        // not materialise): never shown as "cannot determine" -- the user gets the proposal.
        if (!result.CanDetermine)
        {
            return (
                new CopilotReply(
                    ReplyKind.Abstain,
                    proposal,
                    [],
                    recoveryActions,
                    new ReplyProvenance([], result.Metadata.ModelId, result.Metadata.PromptVersion, result.Metadata.InputHash),
                    recoveryFollowUps),
                composed.Value.GuardIntervened,
                composed.Value.FallbackUsed);
        }

        // Model action keys were already normalized to catalog keys by AnswerComposer
        // (ActionKeyNormalizer); a key whose route needs an id this turn does not carry (Contract 360
        // on a portfolio-wide turn) is dropped here rather than failing CapabilityRouting.ForKey.
        var actionKeys = ActionKeyNormalizer.Routable(result.ActionKeys, routingContext);
        var resolvedActions = actionKeys.Count > 0
            ? capabilityRouting.ResolveActions(actionKeys.Select(CapabilityIntent.HowTo).ToList(), routingContext)
            : [];

        // Task E31/F03/US01/T01 (q3-persist; NW-97; ADR-024 w19 cl. 21/ADR-028; parent story
        // us-01-q3-persist AC-2): the live Q3 answer's server-injected `/renewals?select={id}`
        // Navigate action -- built the same way every other deep-link in this file already is
        // (CapabilityRouting.ResolveActions -> ForKey -> BuildHref's own RenewalsKey branch), never
        // from composed.Value.Result.ActionKeys above (the model's own action keys --
        // FixtureAiGateway.AnswerFromPack never populates them for a pack-JSON turn at all, and a
        // live Foundry answer must not get to invent this link either). routingContext already
        // carries this turn's namedContractItem id (set above, before the Navigate/QuoteRoute
        // short-circuit), so ForKey needs no extra id resolution and cannot throw for a missing
        // placeholder. Concat + Distinct() folds the case where actionKeys also named this
        // capability into one action rather than two identical buttons -- the same "Record
        // equality" de-dup ResolveActions itself already performs internally (CapabilityRouting's
        // own type doc comment), extended here across the two lists this method now combines.
        if (isQ3PersistTurn)
        {
            var injectedRenewalsAction = capabilityRouting.ResolveActions(
                [CapabilityIntent.HowTo(CapabilityCatalog.RenewalsKey)], routingContext);
            resolvedActions = resolvedActions.Concat(injectedRenewalsAction).Distinct().ToList();
        }

        // A savings answer always leads somewhere: the Savings opportunities it is about and the
        // Renewals deadlines that time them, first (the first action is the primary button) --
        // server-resolved from the catalog, never from the model's own keys, merged with them the
        // same Distinct() way as the Q3 injection above.
        if (result.CanDetermine && plan.Intent is AskIntent.Savings or AskIntent.PortfolioSavingsTarget)
        {
            var injectedSavingsActions = capabilityRouting.ResolveActions(
                [CapabilityIntent.Savings, CapabilityIntent.Deadline], routingContext);
            resolvedActions = injectedSavingsActions.Concat(resolvedActions).Distinct().ToList();
        }

        return (
            CopilotReplyBuilder.FromGuardedResult(result, boundedPack, resolvedActions, recoveryActions, recoveryFollowUps),
            composed.Value.GuardIntervened,
            composed.Value.FallbackUsed);
    }

    // ----- Per-intent pack composition -----

    /// <summary>
    /// <c>internal</c>, not <c>private</c> — see <see cref="BuildMarketComparePackAsync"/>'s own
    /// doc comment for why (test-reachability precedent, <see cref="ResolveTenantClauseLinks"/>).
    /// <c>Raffa.Api.Tests.AskPricedLinesParityTests</c> calls this directly to compare the
    /// <c>calc:target[...]</c> item's <see cref="PackValue"/>s against <c>GET
    /// /api/contracts/{id}/strategy</c>'s own JSON (AC-2) -- <c>FixtureAiGateway.AnswerFromPack</c>
    /// only ever echoes a pack's first five items, and calling this method directly is what lets
    /// that test (and <c>Raffa.Api.Tests.AskQ3RenewalStrategyTests</c>, task E31/F01/US01/T01)
    /// assert past that unrelated cap regardless of this pack's own item order.
    ///
    /// <para>
    /// <b>Tenant clause evidence is this method's own caller's job</b> (AC-2, task E31/F01/US01/T01,
    /// NW-95): this method returns <c>calc</c>/<c>market</c>/<c>raffa</c> items only, never
    /// <c>tenant</c> — it has no <c>question</c> text to search clause embeddings with, and adding
    /// one just for this would break <c>AskPricedLinesParityTests</c>' own direct call (a signature
    /// this task deliberately leaves alone). <see cref="BuildRenewalStrategyWithEvidenceAsync"/> is
    /// the composition that appends this contract's own scoped clause evidence
    /// (<see cref="ClausePackBuilder.BuildClausePackAsync"/>, the epic-28 scoped RAG) and is what <see cref="BuildInDomainReplyAsync"/>
    /// actually calls for a live <see cref="AskIntent.RenewalStrategy"/> turn.
    /// </para>
    ///
    /// <para>
    /// <b>Fixed item order</b>: when-you-must-move, then up to three grounded ranked points
    /// (<see cref="BuildNegotiationPointsPackAsync"/>, task E31/F02/US01/T01, NW-96 — never the
    /// generic seven-lever dump this method used to emit here, retired by this same task per
    /// epic-31's own "Out of scope: no ungrounded '7 lever' dump"), then one target per priced line,
    /// then next-steps, then up to two market bands, then one raffa Renewals citation.
    /// </para>
    /// </summary>
    internal async Task<IReadOnlyList<PackItem>> BuildRenewalStrategyPackAsync(
        PortfolioListItem namedContractItem,
        CancellationToken cancellationToken,
        bool persistTodos = false,
        string actor = "")
    {
        var contract360 = await contract360QueryService
            .GetByIdAsync(CurrentTenantId, new EntityId(namedContractItem.ContractId), cancellationToken)
            .ConfigureAwait(false);

        if (contract360 is null)
        {
            return [];
        }

        var renewal = ContractInsightsMapper.ComputeRenewal(contract360.Header, renewalEngine);
        var asOfDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        // One resolution per screen (ADR-024 w17 clause 7), extended to Ask (task E28/F01/US01/T01,
        // NW-82): the same BenchmarkKeyResolution key, and the same async ToPricedLines overload,
        // GET /api/contracts/{id}/strategy already calls -- so the two paths query the benchmark
        // service with an identical (supplier name, geography) key and cannot compute two different
        // bands for the same line (AC-1/AC-2). Previously this called the *sync* ToPricedLines
        // overload, which never resolves a band at all (every line's Benchmark stays null by
        // construction) -- Ask's own strategy pack narrated "insufficient market data" on every
        // priced line even when /strategy found a real one; that drift is this task's own bug to
        // close.
        var (benchmarkSupplierName, geography) = await context.ResolveBenchmarkKeyAsync(contract360.Header.SupplierId, cancellationToken)
            .ConfigureAwait(false);

        var pricedLines = await context.ResolvePricedLinesAsync(contract360, benchmarkSupplierName, geography, asOfDate, cancellationToken)
            .ConfigureAwait(false);

        var criticalFacts = ContractInsightsMapper.ToCriticalFacts(contract360);
        var supplierName = await context.ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false);

        // Composed by the same mapping GET /api/contracts/{id}/strategy uses
        // (ContractInsightsMapper.ToStrategyInputs / StrategyPackBuilder.Build), so Ask and the
        // endpoint narrate the identical targets whenever a band exists (AC-2) and the identical
        // "insufficient market data" explanation when it does not (AC-3;
        // PricedLineNegotiationCalculator.ComputeTargetRange). Ask overrides SupplierName with its
        // own always-available ResolveDisplayNameAsync result (falls back to the contract type,
        // never null, unlike /strategy's own name -- which is only as good as this same
        // BenchmarkKeyResolution call) purely for narration text ("Notify X of intent..." etc.); the
        // override never touches a priced line's own Benchmark/band, so it cannot desync the numbers
        // this task fixes.
        var strategyInputs = ContractInsightsMapper
            .ToStrategyInputs(contract360, renewal, pricedLines, criticalFacts, asOfDate)
            with
            { SupplierName = supplierName };

        var pack = StrategyPackBuilder.Build(strategyInputs);
        var currency = contract360.Overview.Currency;
        var items = new List<PackItem>
        {
            new(
                InsightsCitationKeys.Calc("when-you-must-move"),
                PackCorpus.Calc,
                $"{supplierName} — when you must move",
                null, null, null,
                pack.WhenYouMustMove.Explanation,
                $"/contracts/{namedContractItem.ContractId}", null, null,
                "deterministic calculator",
                BuildDateValues(pack.WhenYouMustMove.RenewalDate, pack.WhenYouMustMove.CancellationDeadline),
                namedContractItem.ContractId.ToString()),
        };

        // Grounded-only ranked points (task E31/F02/US01/T01, point-ranker; NW-96; ADR-024 w19 cl.
        // 23), wired into a live turn here (task E31/F01/US01/T01, q3-route; NW-95). Replaces the
        // ungrounded seven-lever loop. includeRenewalUrgency is false -- the when-you-must-move
        // item above already covers that ground. persistTodos is true on the live Q3 path
        // (E29/F02/US01/T01 todo-host-upsert; NW-85) so ranking and upsert share one call;
        // direct test callers leave the default false.
        var rankedPoints = await BuildNegotiationPointsPackAsync(
                new EntityId(namedContractItem.ContractId),
                includeRenewalUrgency: false,
                persistTodos,
                persistTodos ? actor : string.Empty,
                cancellationToken)
            .ConfigureAwait(false);
        items.AddRange(rankedPoints);

        var targetIndex = 0;
        foreach (var target in pack.Targets)
        {
            var values = new List<PackValue>();
            if (target.OpeningTarget is { } opening)
            {
                values.Add(new PackValue("openingTarget", opening.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, currency));
            }

            if (target.AcceptableRangeLow is { } low)
            {
                values.Add(new PackValue("acceptableRangeLow", low.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, currency));
            }

            if (target.AcceptableRangeHigh is { } high)
            {
                values.Add(new PackValue("acceptableRangeHigh", high.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, currency));
            }

            if (target.WalkAwayThreshold is { } walkAway)
            {
                values.Add(new PackValue("walkAwayThreshold", walkAway.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, currency));
            }

            items.Add(new PackItem(
                InsightsCitationKeys.Calc($"target[{targetIndex++}]"),
                PackCorpus.Calc,
                $"{supplierName} — {target.Description} target",
                null, null, null,
                target.Explanation,
                $"/contracts/{namedContractItem.ContractId}", null, null,
                "deterministic calculator", values,
                namedContractItem.ContractId.ToString()));
        }

        items.Add(new PackItem(
            InsightsCitationKeys.Calc("next-steps"),
            PackCorpus.Calc,
            $"{supplierName} — next steps",
            null, null, null,
            string.Join("; ", pack.NextSteps.Select(step => $"{step.Label} ({step.DueHint})")),
            $"/contracts/{namedContractItem.ContractId}", null, null,
            "deterministic calculator", [],
            namedContractItem.ContractId.ToString()));

        // Market corpus (AC-2 "+ market"): the same benchmarked priced lines above, as citable
        // PackCorpus.Market items -- BuildMarketPricedLineItem is the same per-line shape
        // BuildMarketComparePackAsync already builds (task E28/F01/US01/T01, NW-82), reused rather
        // than duplicated so a missing band never narrates two different "insufficient market data"
        // wordings for the identical line.
        foreach (var line in pricedLines.Take(MaxPricedLinesForBenchmark))
        {
            if (line.UnitPrice is null)
            {
                continue;
            }

            items.Add(BuildMarketPricedLineItem(supplierName, line));
        }

        // AC-2 "+ one raffa Renewals item": a feature citation card, the same FeatureCitation shape
        // BuildCapabilityReply already cites directly into a reply (R-SYS-03), but as a genuine
        // PackItem here so the `answer` role can cite it like any other pack source. Task
        // E31/F03/US01/T01 (q3-persist, NW-97) injects the server-injected /renewals?select=
        // Navigate action in BuildInDomainReplyAsync's own isQ3PersistTurn branch -- this is the
        // citation card, never the CTA.
        if (BuildFeatureCitationPackItem(CapabilityCatalog.RenewalsKey) is { } renewalsItem)
        {
            items.Add(renewalsItem);
        }

        return items;
    }

    /// <summary>
    /// Task E31/F01/US01/T01 (q3-route; NW-95; ADR-024 w19 cl. 23; parent story us-01-q3-route
    /// AC-1/AC-2/AC-3) plus E29/F02/US01/T01 persist: the full Q3 "contrattare"/"rinnovo" answer
    /// pack — <see cref="BuildRenewalStrategyPackAsync"/>'s calc/market/raffa items plus this
    /// contract's own clause evidence (<see cref="ClausePackBuilder.BuildClausePackAsync"/>, epic-28 scoped RAG),
    /// with <c>persistTodos: true</c> so the ranked set is upserted before
    /// <see cref="AnswerComposer.AnswerAsync"/>. Tenant evidence is appended, not prepended: the
    /// calc corpus's own "when you must move" item must stay first (AC-3).
    ///
    /// <para>
    /// <paramref name="persistTodos"/> is <see langword="true"/> for the live Q3 turn (the user is
    /// asking what to negotiate, and the Renewals link the reply offers is true after the turn) and
    /// <see langword="false"/> for the drafted email (F1-D07, decision D7): a draft is a copyable
    /// text with no side effect, so building its pack writes nothing.
    /// </para>
    /// </summary>
    internal async Task<IReadOnlyList<PackItem>> BuildRenewalStrategyWithEvidenceAsync(
        TenantId tenantId,
        string question,
        PortfolioListItem namedContractItem,
        string actor,
        CancellationToken cancellationToken,
        SavingsGoal? goal = null,
        bool persistTodos = true)
    {
        var strategyItems = (await BuildRenewalStrategyPackAsync(
                namedContractItem, cancellationToken, persistTodos, persistTodos ? actor : string.Empty)
            .ConfigureAwait(false)).ToList();

        // The money behind the strategy: the target verdict, the grounded levers, the supplier's
        // market deals and the playbook entries for those levers (AskCopilotService.Savings.cs).
        strategyItems.AddRange(await BuildLeverAddendumAsync(namedContractItem, goal, cancellationToken).ConfigureAwait(false));

        // Tenant clause evidence (AC-2). SearchByContractAsync uses CosineDistance, which
        // InMemory EF cannot translate — the same constraint the notice fallbacks document
        // and therefore never call embeddings. A translation miss is empty tenant corpus,
        // never a failed Q3 turn: calc/market/raffa + persist already completed above.
        IReadOnlyList<PackItem> clauseItems;
        try
        {
            clauseItems = await clauses.BuildClausePackAsync(tenantId, question, namedContractItem, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("could not be translated", StringComparison.Ordinal))
        {
            clauseItems = [];
        }

        return DistinctByCitationKey(strategyItems.Concat(clauseItems));
    }

    /// <summary>
    /// Task E31/F02/US01/T01 (point-ranker; NW-96; ADR-024 w19 cl. 23; parent story
    /// us-01-point-ranker AC-1/AC-2/AC-3): the shared negotiation-points pack builder. Maps this
    /// contract's clause/risk/commercial snapshot into
    /// <see cref="NegotiationPointInputs"/> — the one mapping only this host may perform
    /// (<c>Raffa.Insights</c> stays fenced to <c>[SharedKernel, Benchmark]</c>; ADR-002) — ranks it
    /// through <see cref="NegotiationPointRanker.Rank"/>, and returns at most the top three as
    /// <see cref="PackItem"/>s for the chat pack ("chat top-3"). When <paramref name="persistTodos"/>
    /// is <see langword="true"/>, the <em>whole</em> grounded set (never just the returned top three)
    /// is upserted to <see cref="RenewalNegotiationTodoService"/> first — "persist-all" — so a point
    /// ranked #4 still reaches <c>/renewals?select=</c> even though chat never narrates it. Returns
    /// <c>[]</c> when the contract does not resolve or the ranker grounds nothing — never a
    /// fabricated point (Appendix C rule 10); <see cref="RenewalNegotiationTodoService.UpsertAsync"/>
    /// itself refuses an empty point set, so an all-ungrounded contract is never called with one.
    /// </summary>
    /// <param name="contractId">The contract to rank.</param>
    /// <param name="includeRenewalUrgency">When <see langword="true"/>, prepends one additional
    /// <c>calc:when-you-must-move</c> item narrating <see cref="RenewalCalculationResult.Explanation"/>
    /// — for a caller with no other renewal-timing summary in its own pack (e.g. a standalone
    /// negotiation-points question). A caller that already renders its own "when you must move"
    /// section (<see cref="BuildRenewalStrategyPackAsync"/>) passes <see langword="false"/> so the
    /// same date is never narrated twice in one answer.</param>
    /// <param name="persistTodos">See this method's own doc comment ("persist-all").</param>
    /// <param name="actor">The caller's resolved token subject (ADR-011 w16 §15) — required, no
    /// default, whenever <paramref name="persistTodos"/> is <see langword="true"/>
    /// (<see cref="RenewalNegotiationTodoService.UpsertAsync"/>'s own required, no-default
    /// <c>actor</c> parameter); ignored otherwise.</param>
    internal async Task<IReadOnlyList<PackItem>> BuildNegotiationPointsPackAsync(
        EntityId contractId,
        bool includeRenewalUrgency,
        bool persistTodos,
        string actor,
        CancellationToken cancellationToken)
    {
        if (persistTodos)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(actor);
        }

        var contract360 = await contract360QueryService
            .GetByIdAsync(CurrentTenantId, contractId, cancellationToken)
            .ConfigureAwait(false);

        if (contract360 is null)
        {
            return [];
        }

        var renewal = ContractInsightsMapper.ComputeRenewal(contract360.Header, renewalEngine);
        var asOfDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        // One resolution per screen (ADR-024 w17 clause 7), same as BuildRenewalStrategyPackAsync/
        // BuildMarketComparePackAsync (task E28/F01/US01/T01, NW-82) -- one (supplier name,
        // geography) key, one async ToPricedLines call, so this pack's above-band-price point can
        // never disagree with /strategy or the market-compare pack about the same line's band.
        var (benchmarkSupplierName, geography) = await context.ResolveBenchmarkKeyAsync(contract360.Header.SupplierId, cancellationToken)
            .ConfigureAwait(false);
        var pricedLines = await context.ResolvePricedLinesAsync(contract360, benchmarkSupplierName, geography, asOfDate, cancellationToken)
            .ConfigureAwait(false);

        var rankerInputs = new NegotiationPointInputs(
            contractId,
            pricedLines,
            contract360.Header.AutoRenewal,
            renewal.RenewalDate,
            renewal.CancellationDeadline ?? contract360.Header.CancellationDeadline,
            contract360.Clauses.Select(c => new NegotiationClauseSnapshot(c.ClauseType, c.RawText)).ToList(),
            contract360.Risks
                .Select(r => new NegotiationRiskSnapshot(
                    r.RiskType,
                    r.Description,
                    ContractInsightsMapper.ToCriticalityRiskSeverity(r.Severity) ?? CriticalityRiskSeverity.None))
                .ToList(),
            contract360.Overview.PaymentTerms);

        var points = NegotiationPointRanker.Rank(rankerInputs);

        // "Persist-all": the whole ranked set, never the chat-bounded top three below -- see this
        // method's own doc comment. RenewalNegotiationTodoService.UpsertAsync itself rejects an
        // empty point set (PointsRequiredError), so a contract that grounds nothing is simply never
        // called rather than special-cased here.
        if (persistTodos && points.Count > 0)
        {
            var todoPoints = points
                .Select(p => new RenewalNegotiationTodoPoint(
                    p.Topic.ToPointKey(), p.Topic.ToDisplayLabel(), p.Rank, p.Current, p.Target, p.WhyItMatters, p.CitationKeys))
                .ToList();

            await renewalNegotiationTodoService
                .UpsertAsync(CurrentTenantId, contractId, todoPoints, actor, cancellationToken)
                .ConfigureAwait(false);
        }

        var supplierName = await context.ResolveDisplayNameAsync(contract360.Header.SupplierId, contract360.Header.Type, cancellationToken)
            .ConfigureAwait(false);

        var items = new List<PackItem>();

        if (includeRenewalUrgency)
        {
            items.Add(new PackItem(
                InsightsCitationKeys.Calc("when-you-must-move"),
                PackCorpus.Calc,
                $"{supplierName} — when you must move",
                null, null, null,
                renewal.Explanation,
                $"/contracts/{contractId.Value}", null, null,
                "deterministic calculator",
                BuildDateValues(renewal.RenewalDate, renewal.CancellationDeadline ?? contract360.Header.CancellationDeadline)));
        }

        const int ChatTopCount = 3;
        foreach (var point in points.Take(ChatTopCount))
        {
            items.Add(new PackItem(
                InsightsCitationKeys.Calc($"negotiation-point[{point.Topic.ToPointKey()}]"),
                PackCorpus.Calc,
                $"{supplierName} — {point.Topic.ToDisplayLabel()}",
                point.Strength.ToString(),
                null, null,
                $"{point.Current} Target: {point.Target} {point.WhyItMatters}",
                $"/contracts/{contractId.Value}", null, null,
                "deterministic calculator", []));
        }

        return items;
    }

    private async Task<IReadOnlyList<PackItem>> BuildPortfolioStrategyPackAsync(
        PortfolioPage portfolio, IReadOnlyDictionary<EntityId, string> supplierNames, CancellationToken cancellationToken)
    {
        if (portfolio.Items.Count == 0)
        {
            return [];
        }

        var allSavings = await savingsOpportunityService.ListAsync(CurrentTenantId, cancellationToken).ConfigureAwait(false);

        var contracts = new List<Contract360Result>();
        foreach (var item in portfolio.Items)
        {
            var contract360 = await contract360QueryService
                .GetByIdAsync(CurrentTenantId, new EntityId(item.ContractId), cancellationToken)
                .ConfigureAwait(false);

            if (contract360 is not null)
            {
                contracts.Add(contract360);
            }
        }

        var portfolioAnnualSpendByCurrency = ContractInsightsMapper.ComputePortfolioAnnualSpendByCurrency(contracts);

        var inputs = contracts
            .Select(contract => ContractInsightsMapper.ToCriticalityInputs(
                contract,
                ContractInsightsMapper.ComputePriority(contract.Header, renewalEngine, priorityScoreCalculator),
                portfolioAnnualSpendByCurrency,
                allSavings))
            .ToList();

        var scores = criticalityScoreCalculator.CalculateMany(inputs);

        var items = new List<PackItem>();
        foreach (var score in scores.Take(PortfolioStrategyTopN))
        {
            var contractItem = portfolio.Items.First(item => item.ContractId == score.ContractId.Value);
            var name = contractItem.SupplierId is { } sid && supplierNames.TryGetValue(new EntityId(sid), out var resolvedName)
                ? resolvedName
                : contractItem.Type.ToString();

            var topComponent = new[]
            {
                ("renewal urgency", score.RenewalUrgency),
                ("risk severity", score.RiskSeverity),
                ("spend weight", score.SpendWeight),
                ("savings potential", score.SavingsPotential),
                ("open critical facts", score.OpenCriticalFacts),
            }.MaxBy(c => c.Item2.Score);

            items.Add(new PackItem(
                InsightsCitationKeys.Calc($"criticality[{score.ContractId}]"),
                PackCorpus.Calc,
                $"{name} — criticality {score.TotalScore:0.#}/100",
                $"driven by {topComponent.Item1}",
                null, null,
                topComponent.Item2.Explanation,
                $"/contracts/{contractItem.ContractId}", null, null,
                "deterministic calculator",
                [new PackValue("totalScore", score.TotalScore.ToString(CultureInfo.InvariantCulture), PackValueKind.Number)],
                contractItem.ContractId.ToString()));
        }

        return items;
    }

    // ----- Shared helpers -----

    private static bool IsCouncilIntent(AskIntent intent, PortfolioListItem? namedContractItem) =>
        intent is AskIntent.Savings or AskIntent.PortfolioSavingsTarget ||
        (intent == AskIntent.RenewalStrategy && namedContractItem is not null);

    /// <summary>Council plays go right after the calculators' target verdict (or the first item
    /// when there is none), ahead of the raw evidence they summarize.</summary>
    private static IReadOnlyList<PackItem> InsertCouncilItems(IReadOnlyList<PackItem> packItems, IReadOnlyList<PackItem> councilItems)
    {
        var list = packItems.ToList();
        var anchor = list.FindIndex(i => i.CitationKey is "calc:savings-target" or "calc:portfolio-target");
        var insertAt = anchor >= 0 ? anchor + 1 : Math.Min(1, list.Count);
        list.InsertRange(insertAt, councilItems);
        return list;
    }

    private static string ComputeHash(string input) => QueryHasher.ComputeHash(input);
}
