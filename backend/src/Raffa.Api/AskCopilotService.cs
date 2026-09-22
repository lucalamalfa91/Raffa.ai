using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Chat.Application;
using Raffa.Chat.Application.Answering;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Council;
using Raffa.Chat.Application.Gate;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Planning;
using Raffa.Chat.Application.Reply;
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

namespace Raffa.Api;

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
/// <see cref="Raffa.Chat.Application.Pack.PackItem"/> list, then handed to
/// <c>Raffa.Chat</c>'s own gate/planner/guards/answer pipeline. <see cref="ChatEndpointExtensions"/>
/// and <see cref="ConversationsEndpointExtensions"/> are this service's only two callers
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
/// <c>Conversation.ScopeContractId</c>, threaded in by <see cref="ConversationsEndpointExtensions"/>
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
/// same async <c>InsightsEndpointExtensions.ToPricedLines</c> overload — never
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
/// number this file cannot confirm): <see cref="BuildNoticePackAsync"/> answers a notice/preavviso/
/// disdetta question (<see cref="IntentPlanner"/>'s own notice lexicon, mirrored locally because
/// <c>IntentPlanner.cs</c> is outside this task's file scope) from the scoped contract's own
/// <see cref="Contract360Renewal"/> fact, a host-computed day count (<see cref="RenewalEngine"/>/
/// <see cref="IClock"/>, the same fallback <see cref="BuildRenewalStrategyPackAsync"/> already uses
/// via <see cref="InsightsEndpointExtensions.ToStrategyInputs"/>), <see cref="StrategyPackBuilder"/>'s
/// own "when you must move" explanation, and — only when this contract's own extracted clauses name
/// one — a matching-clause citation built from <see cref="Contract360Result.Clauses"/>, never from
/// <see cref="EmbeddingRetrievalService"/> (every notice question this pack answers is exactly the
/// shape a full HTTP round trip already exercises under this project's InMemory EF Core provider,
/// which cannot translate <c>Embedding.Vector.CosineDistance</c> — <c>InMemoryAskEngineFactory</c>'s
/// own doc comment). Every date is a <see cref="PackValueKind.Date"/> value; "N days" is only ever a
/// calculator-produced <see cref="PackValueKind.Number"/> value, never <c>EndDate − CancellationDeadline</c>
/// and never model arithmetic (NW-92).
/// </para>
///
/// <para>
/// <b>Five server-decided notice fallbacks</b> (task E30/F02/US01/T01, NW-94; parent story
/// us-01-notice-fallbacks, "a scoped notice turn never asks 'which supplier'"; this task's own
/// citation is "ADR-024 w19 cl. 22", the same not-yet-folded-in clause number
/// <see cref="BuildNoticePackAsync"/>'s own doc comment already notes, so
/// <c>reports/architecture/waves/w19.md</c>'s NW-94 row is again the verifiable source):
/// <see cref="BuildNoticeFallbackReplyAsync"/> intercepts every notice question — before any pack
/// is built and before <see cref="AnswerComposer"/> ever runs, the same short-circuit shape
/// <see cref="BuildRoutingOnlyReply"/> already uses for <see cref="AskIntent.Navigate"/>/
/// <see cref="AskIntent.QuoteRoute"/> — and decides one of five outcomes purely from facts
/// <see cref="BuildNoticePackAsync"/>'s own helpers already compute: a resolved deadline plus a
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
internal sealed partial class AskCopilotService(
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
    IBenchmarkService benchmarkService,
    BenchmarkKeyResolution benchmarkKeyResolution,
    IMarketKnowledgeRetrieval marketKnowledgeRetrieval,
    IMarketDealLookup marketDealLookup,
    NegotiationCouncil negotiationCouncil,
    IAuditWriter auditWriter,
    ITenantContext tenantContext,
    IClock clock)
{
    private const int ClauseTopK = 5;

    /// <summary>Task E28/F02/US01/T01 (NW-81 AC-2 "lower K"): the labelled "similar types" peer
    /// slice always asks for fewer rows than <see cref="ClauseTopK"/> -- it is corroborating
    /// evidence from a different, merely similar-type contract, never this contract's own primary
    /// evidence.</summary>
    private const int ClausePeerTopK = 2;

    private const int MarketNotesTopK = 3;
    private const int MaxPricedLinesForBenchmark = 2;
    private const int PortfolioStrategyTopN = 5;

    // Reused, not re-implemented (task's own "extend / reuse" file list): both are pure/stateless
    // (Appendix C rule 6), so a private, non-DI instance is exactly how Raffa.Chat.Application
    // .Planning.IntentPlanner already reuses AskRaffaQueryRouter itself.
    private readonly AskRaffaQueryRouter _legacyRouter = new();
    private readonly DeterministicQueryPlanner _legacyPlanner = new();
    private readonly DeterministicQueryHandler _legacyHandler = new(clock);

    private const string AuditAnsweredAction = "chat.answered";
    private const string AuditRedirectedAction = "chat.redirected";
    private const string AuditRefusedAction = "chat.refused";
    private const string AuditAbstainedAction = "chat.abstained";
    private const string AuditResourceType = "ask_raffa_v2";

    /// <summary>The tenant scope <see cref="AskAsync"/> already opened for this call
    /// (<see cref="ITenantContext.BeginScope"/>) — every private helper below reads this instead
    /// of threading a <c>TenantId</c> parameter through the whole call tree. Falls back to
    /// <see langword="default"/> only in the never-actually-reached case where a helper is somehow
    /// invoked outside that scope (fail-closed: <see langword="default"/> resolves to no rows
    /// under RLS, never another tenant's).</summary>
    private TenantId CurrentTenantId => tenantContext.Current ?? default;

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
    /// <exception cref="ArgumentException"><paramref name="question"/> is null/blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="recentTurns"/> is <see langword="null"/>.</exception>
    public async Task<CopilotReply> AskAsync(
        TenantId tenantId,
        string question,
        IReadOnlyList<(string Role, string Markdown)> recentTurns,
        string actor,
        EntityId? scopeContractId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        ArgumentNullException.ThrowIfNull(recentTurns);

        using var scope = tenantContext.BeginScope(tenantId);

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

        if (scopedSupplierName is not null && gate.Label is GateLabel.NeedsDocument or GateLabel.InDomain)
        {
            gate = gate with
            {
                Label = GateLabel.InDomain,
                Reason = $"scoped entry resolved to known supplier '{scopedSupplierName}' before the " +
                    "gate's own free-text extraction decided (ADR-024, task E25/F03/US01/T01).",
                NamedSupplier = scopedSupplierName,
            };
        }

        // AC-7 / R-ASK-09: the audit row must distinguish "guard caught a violation and downgraded
        // to abstain" from every other abstain path (empty pack, gateway call failed) — so every
        // branch here carries its own guardIntervened alongside the reply, rather than WriteAuditAsync
        // inferring it from ReplyKind alone (which cannot tell the two apart; see
        // BuildInDomainReplyAsync's own doc comment on its tuple return).
        var (reply, guardIntervened) = gate.Label switch
        {
            GateLabel.Greeting or GateLabel.OffDomain => (BuildGreetingReply(portfolio, supplierNames), false),
            GateLabel.Legal => (BuildLegalReply(portfolio, supplierNames, gate.NamedSupplier), false),
            GateLabel.Capability => (BuildCapabilityReply(portfolio.Items.Count), false),
            GateLabel.NeedsDocument => (BuildNeedsDocumentReply(gate.NamedSupplier!, portfolio.Items.Count), false),
            GateLabel.InDomain => await BuildInDomainReplyAsync(
                tenantId, question, gate.NamedSupplier, portfolio, supplierNames, recentTurns,
                scopeContractId, scopedContractItem, actor, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(gate), gate.Label, "Unknown GateLabel."),
        };

        await WriteAuditAsync(tenantId, reply, guardIntervened, actor, cancellationToken).ConfigureAwait(false);

        return reply;
    }

    // ----- Gate-level deterministic replies (R-ASK-02) -----

    private CopilotReply BuildGreetingReply(
        PortfolioPage portfolio, IReadOnlyDictionary<EntityId, string> supplierNames)
    {
        var (hookSentence, hookContractId) = BuildPortfolioHook(portfolio, supplierNames);
        var routingContext = new RoutingContext(portfolio.TotalCount, CapabilityCallerRole.Standard, hookContractId);

        var actions = hookContractId is not null
            ? capabilityRouting.ResolveActions([CapabilityIntent.HowTo(CapabilityCatalog.ContractDetailKey)], routingContext)
            : capabilityRouting.ResolveActions([CapabilityIntent.HowTo(CapabilityCatalog.DocumentsKey)], routingContext);

        return RedirectReplyBuilder.GreetingOrOffDomain(hookSentence, actions);
    }

    private CopilotReply BuildLegalReply(
        PortfolioPage portfolio, IReadOnlyDictionary<EntityId, string> supplierNames, string? namedSupplier)
    {
        var contractId = namedSupplier is null ? null : FindContractIdBySupplier(portfolio, supplierNames, namedSupplier);
        var routingContext = new RoutingContext(portfolio.TotalCount, CapabilityCallerRole.Standard, contractId);

        var actions = contractId is not null
            ? capabilityRouting.ResolveActions([CapabilityIntent.HowTo(CapabilityCatalog.ContractDetailKey)], routingContext)
            : capabilityRouting.ResolveActions([CapabilityIntent.HowTo(CapabilityCatalog.DocumentsKey)], routingContext);

        return RedirectReplyBuilder.Legal(actions);
    }

    private CopilotReply BuildCapabilityReply(int validatedContractCount)
    {
        var routingContext = new RoutingContext(validatedContractCount, CapabilityCallerRole.Standard);
        var actions = capabilityRouting.ResolveActions([CapabilityIntent.CapabilityList], routingContext);

        var citations = BuildFeatureCitations(
            [CapabilityCatalog.DocumentsKey, CapabilityCatalog.PortfolioKey, CapabilityCatalog.RenewalsKey, CapabilityCatalog.QuoteCheckKey]);

        const string markdown =
            "I answer from your validated contracts and route you to the right part of Raffa:\n" +
            "- Documents [1] — upload contracts, review weak facts.\n" +
            "- Portfolio [2] — every contract, spend, liability and risk in one table.\n" +
            "- Renewals [3] — deadlines and the action for each.\n" +
            "- Quote check [4] — benchmark a new quote against the market and your history.\n" +
            "Ask me about dates, spend, notice periods, clauses — or which of these to open.";

        return RedirectReplyBuilder.Capability(markdown, citations, actions);
    }

    private CopilotReply BuildNeedsDocumentReply(string namedSupplier, int validatedContractCount)
    {
        var routingContext = new RoutingContext(validatedContractCount, CapabilityCallerRole.Standard);
        var actions = capabilityRouting.ResolveActions([CapabilityIntent.UnknownSupplier], routingContext);
        return RedirectReplyBuilder.NeedsDocument(namedSupplier, actions);
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
    private async Task<(CopilotReply Reply, bool GuardIntervened)> BuildInDomainReplyAsync(
        TenantId tenantId,
        string question,
        string? namedSupplier,
        PortfolioPage portfolio,
        IReadOnlyDictionary<EntityId, string> supplierNames,
        IReadOnlyList<(string Role, string Markdown)> recentTurns,
        EntityId? scopeContractId,
        PortfolioListItem? scopedContractItem,
        string actor,
        CancellationToken cancellationToken)
    {
        // A bare follow-up ("non mi hai risposto", "e quindi?") is planned on the previous user
        // question too, so it inherits that turn's intent and saving goal instead of falling
        // through to the StructuredFact default (see IntentPlanner.Plan's own doc comment).
        var previousUserQuestion = recentTurns
            .LastOrDefault(turn => string.Equals(turn.Role, "you", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase))
            .Markdown;

        var plan = intentPlanner.Plan(question, namedSupplier, previousUserQuestion);

        // AC-3, lock 4 (task E27/F02/US01/T01, NW-76): a scope id present on this conversation but
        // absent from this turn's own freshly-fetched portfolio refuses outright, before a single
        // BuildXxxPackAsync call below ever runs (no pack leak) -- never a silent fall-through to
        // an unscoped answer for a caller who explicitly named a contract they can no longer see.
        // PortfolioMarketPosition is exempt: lock 4 already makes it portfolio-wide regardless of
        // scope, so it never depends on the scoped contract resolving at all.
        if (scopeContractId is not null && scopedContractItem is null && plan.Intent != AskIntent.PortfolioMarketPosition)
        {
            return (BuildUnseenScopeRefusal(portfolio), false);
        }

        // AC-2 (NW-76) / NW-80 (task E27/F05/US01/T01): a resolved, visible scope id wins outright
        // over a same-name portfolio lookup -- two rows can share one supplier's display name, and
        // picking whichever one a name match happens to hit first would silently answer about the
        // wrong contract. Lock 4: PortfolioMarketPosition is never narrowed to the scoped contract,
        // so it alone passes null through here and falls back to ResolveNamedContractItem's own
        // name-based, soonest-cancellation-deadline lookup below (which still applies for every
        // other, unscoped turn, and for every turn once no scope resolved at all).
        var (namedContractItem, disambiguationItem) = ResolveNamedContractItem(
            plan.Intent == AskIntent.PortfolioMarketPosition ? null : scopedContractItem,
            plan.NamedSupplier, portfolio, supplierNames);

        // AC-2: follows namedContractItem above, so a scoped turn's follow-up actions (e.g. "open
        // this contract") also target the real scoped id, never a same-name-but-wrong contract.
        var contractIdForActions = namedContractItem is not null ? new EntityId(namedContractItem.ContractId) : (EntityId?)null;
        var routingContext = new RoutingContext(portfolio.TotalCount, CapabilityCallerRole.Standard, contractIdForActions);

        if (plan.Intent is AskIntent.Navigate or AskIntent.QuoteRoute)
        {
            return (BuildRoutingOnlyReply(plan, routingContext), false);
        }

        // Task E30/F02/US01/T01 (NW-94): the five notice fallbacks are fully server-decided, so a
        // notice question never reaches AnswerComposer/the AI gateway at all -- intercepted here,
        // before any pack is built, the same short-circuit shape the Navigate/QuoteRoute branch
        // above already uses. Deliberately checked before namedContractItem is known to be
        // resolved or not: BuildNoticeFallbackReplyAsync itself is what tells "a resolved contract"
        // (cases 1-4) apart from "an unscoped notice question" (case 5, NW-94's own "unscoped
        // deictic" abstain), so both must reach it rather than only the scoped half. This makes
        // BuildStructuredFactOrNoticePackAsync's own "namedContractItem is not null &&
        // NoticeQuestionPattern.IsMatch(question)" branch unreachable from this call site --
        // feature-01's own method, left exactly as written (this task's "do not touch the pack"),
        // the same "unreachable in practice, kept exhaustive" shape DescribeStructuredResult's own
        // default case below already documents for an identical reason.
        if (plan.Intent == AskIntent.StructuredFact && NoticeQuestionPattern.IsMatch(question))
        {
            return (
                await BuildNoticeFallbackReplyAsync(namedContractItem, disambiguationItem, routingContext, cancellationToken)
                    .ConfigureAwait(false),
                false);
        }

        var packItems = plan.Intent switch
        {
            AskIntent.StructuredFact => await BuildStructuredFactOrNoticePackAsync(question, namedContractItem, portfolio, supplierNames, cancellationToken)
                .ConfigureAwait(false),
            AskIntent.Clause => await BuildClausePackAsync(tenantId, question, namedContractItem, cancellationToken)
                .ConfigureAwait(false),
            AskIntent.MarketCompare => await BuildMarketComparePackAsync(namedContractItem, cancellationToken).ConfigureAwait(false),
            AskIntent.RenewalStrategy => namedContractItem is not null
                ? await BuildRenewalStrategyWithEvidenceAsync(tenantId, question, namedContractItem, actor, cancellationToken, plan.Goal).ConfigureAwait(false)
                : await BuildPortfolioStrategyPackAsync(portfolio, supplierNames, cancellationToken).ConfigureAwait(false),
            AskIntent.PortfolioStrategy => await BuildPortfolioStrategyPackAsync(portfolio, supplierNames, cancellationToken).ConfigureAwait(false),
            AskIntent.PortfolioSavingsTarget => await BuildPortfolioSavingsTargetPackAsync(portfolio, plan.Goal, cancellationToken).ConfigureAwait(false),
            AskIntent.Savings => namedContractItem is not null
                ? await BuildSavingsLeverPackAsync(tenantId, namedContractItem, plan.Goal, actor, cancellationToken).ConfigureAwait(false)
                : await BuildPortfolioStrategyPackAsync(portfolio, supplierNames, cancellationToken).ConfigureAwait(false),
            AskIntent.DocumentStatus => BuildDocumentStatusPack(portfolio, supplierNames),
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

        // The negotiation council (Raffa.Chat.Application.Council): for a savings or negotiation
        // turn, two analysts read the pack in parallel and a strategist turns their findings into
        // ranked plays -- inserted right after the target verdict so the budget keeps them and the
        // answer leads with them. A failed agent degrades the council, never the turn.
        if (IsCouncilIntent(plan.Intent, namedContractItem))
        {
            var council = await negotiationCouncil.RunAsync(question, packItems, plan.Goal, cancellationToken).ConfigureAwait(false);
            if (council.Items.Count > 0)
            {
                packItems = InsertCouncilItems(packItems, council.Items);
            }
        }

        var boundedPack = packBudget.Apply(packItems);

        if (boundedPack.Count == 0)
        {
            // ADR-027 §D8 (task E16/F02/US03/T01): no number here. `portfolio.Items.Count` is the
            // UNFILTERED portfolio -- every bootstrap shell a still-processing document created --
            // so rendering it as "N validated contract(s)" asserted a fabricated fact to the user.
            // The one definition of "validated" is ADR-026 §D2's CountValidatedContractsAsync; the
            // pre-question off-state already renders that number from the server, and this
            // sentence needs none.
            return (new CopilotReply(
                ReplyKind.Abstain,
                "Nothing in your validated contracts supports a reliable answer. " +
                "Try a question about dates, spend, notice periods or clauses.",
                [], ResolveAbstainRecoveryActions(portfolio, routingContext), ReplyProvenance.NoModelCall([]), []), false);
        }

        var composed = await answerComposer.AnswerAsync(question, boundedPack, recentTurns, cancellationToken)
            .ConfigureAwait(false);

        if (composed.IsFailure)
        {
            return (new CopilotReply(
                ReplyKind.Abstain,
                "Raffa could not reach the answer service just now — please try again shortly.",
                [], ResolveAbstainRecoveryActions(portfolio, routingContext), ReplyProvenance.NoModelCall([]), []), false);
        }

        var actionKeys = composed.Value.Result.ActionKeys ?? [];
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

        return (
            CopilotReplyBuilder.FromGuardedResult(
                composed.Value.Result, boundedPack, resolvedActions, ResolveAbstainRecoveryActions(portfolio, routingContext)),
            composed.Value.GuardIntervened);
    }

    /// <summary>
    /// AC-3 (task E27/F02/US01/T01, NW-76; ADR-024 w19 cl. 12): the reply for a
    /// <c>scopeContractId</c> that does not resolve to a row in this turn's own freshly-fetched
    /// portfolio. <see cref="ReplyKind.Refusal"/>, not <see cref="ReplyKind.Abstain"/> — this is not
    /// "a real question with no groundable evidence", it is "the caller named a contract they
    /// cannot see", the same distinction <see cref="RedirectReplyBuilder.Legal"/> already draws for
    /// a different refusal. Reuses <see cref="ResolveAbstainRecoveryActions"/> for the same
    /// upload-or-ask-again next step every other "cannot proceed for this contract" reply in this
    /// method already offers, rather than inventing a third recovery shape — a bare
    /// <see cref="RoutingContext"/> with no <c>ContractId</c>, since the one contract this turn
    /// named is exactly the one that is not available to route to.
    /// </summary>
    private CopilotReply BuildUnseenScopeRefusal(PortfolioPage portfolio)
    {
        var routingContext = new RoutingContext(portfolio.TotalCount, CapabilityCallerRole.Standard);

        return new CopilotReply(
            ReplyKind.Refusal,
            "The contract this conversation is scoped to is no longer available to you. Open " +
            "Portfolio to pick a contract, or ask a portfolio-wide question instead.",
            [],
            ResolveAbstainRecoveryActions(portfolio, routingContext),
            ReplyProvenance.NoModelCall([]),
            []);
    }

    /// <summary>
    /// Task E27/F05/US01/T01 (NW-80; parent story us-01-supplier-resolution AC-2): which single
    /// contract a named-supplier turn answers about, replacing a plain <c>FirstOrDefault</c> over
    /// <paramref name="portfolio"/> (whichever row the portfolio query happened to return first for
    /// that supplier — never a deliberate choice, and never told to the user) with two rules, tried
    /// in order:
    ///
    /// <list type="number">
    /// <item><b>Scoped id wins.</b> <paramref name="scopedContractItem"/> is <see cref="AskAsync"/>'s
    /// own already-resolved <c>Conversation.ScopeContractId</c> (Contract 360's "Ask about it") — it
    /// names one exact contract with no ambiguity at all, so it is returned unchanged and no
    /// disambiguation note is produced (the user already knows which contract they opened this chat
    /// from). <see cref="BuildInDomainReplyAsync"/> passes <see langword="null"/> here instead for
    /// <see cref="AskIntent.PortfolioMarketPosition"/> (lock 4; task E27/F02/US01/T01, NW-76), which
    /// is never narrowed to a scoped contract, so that intent always falls through to rule 2.</item>
    /// <item><b>Else, the soonest cancellation deadline / renewal wins</b> — the same ordering
    /// <c>Raffa.Renewals.Application.RenewalPipelineBuilder.Build</c> uses: cancellation deadline
    /// when known, else renewal date, unknown-urgency rows last. Sorting directly on
    /// <see cref="PortfolioListItem.CancellationDeadline"/>/<see cref="PortfolioListItem.RenewalDate"/>
    /// *is* that ordering, not an approximation of it —
    /// <c>Raffa.Renewals.Application.RenewalEngine.Calculate</c>'s own doc comment records that its
    /// <c>RenewalDate</c> "reproduces <see cref="PortfolioListItem.RenewalDate"/>'s own convention on
    /// purpose". When more than one contract matches, <see cref="BuildMultiContractDisambiguationItem"/>
    /// is what keeps the pick from being a silent merge.</item>
    /// </list>
    /// </summary>
    /// <returns>The contract this turn answers about (or <see langword="null"/> when no supplier was
    /// named, or a named supplier has no contract at all — <see cref="GateLabel.NeedsDocument"/>
    /// already handled the latter upstream so this is not expected in practice), and a
    /// <see cref="PackCorpus.Calc"/> pack item naming the choice, only when the choice was actually
    /// ambiguous.</returns>
    private static (PortfolioListItem? Item, PackItem? DisambiguationItem) ResolveNamedContractItem(
        PortfolioListItem? scopedContractItem,
        string? namedSupplier,
        PortfolioPage portfolio,
        IReadOnlyDictionary<EntityId, string> supplierNames)
    {
        if (scopedContractItem is not null)
        {
            return (scopedContractItem, null);
        }

        if (namedSupplier is null)
        {
            return (null, null);
        }

        var matches = portfolio.Items
            .Where(item =>
                item.SupplierId is { } supplierId &&
                supplierNames.TryGetValue(new EntityId(supplierId), out var name) &&
                string.Equals(name, namedSupplier, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.CancellationDeadline ?? item.RenewalDate ?? DateOnly.MaxValue)
            .ThenBy(item => item.ContractId)
            .ToList();

        if (matches.Count == 0)
        {
            return (null, null);
        }

        var chosen = matches[0];

        if (matches.Count == 1)
        {
            return (chosen, null);
        }

        return (chosen, BuildMultiContractDisambiguationItem(chosen, matches.Count, namedSupplier));
    }

    /// <summary>
    /// NW-80's required disambiguation sentence: <c>"Using {Type} CT-01 (renews …). Ask if you meant
    /// another."</c> (task text, verbatim). A <see cref="PackCorpus.Calc"/> pack item, not free-form
    /// model prose, so the fact of which contract was picked is grounded evidence the `answer` role
    /// can cite rather than something it might choose to omit — both the fixture and the live gateway
    /// cite pack items, never invent independent sentences (<c>Raffa.AiGateway.Fixtures
    /// .FixtureAiGateway.AnswerFromPack</c>'s own "no chunk concatenation" convention), and
    /// <see cref="BuildInDomainReplyAsync"/> prepends this item so <see cref="PackBudget.Apply"/> and
    /// that fixture's first-N-items citation both always keep it.
    ///
    /// <para>
    /// "CT-01" is this one reply's own local label, not a persisted contract code (no such column
    /// exists on <c>Contract</c> yet) — it exists only so "ask if you meant another" has something
    /// short to refer back to. It is always "01" because <paramref name="chosen"/> is always the
    /// soonest match (position 1 of <see cref="ResolveNamedContractItem"/>'s own soonest-first sort)
    /// and this sentence only ever names the one contract actually chosen, never enumerates the
    /// others.
    /// </para>
    /// </summary>
    private static PackItem BuildMultiContractDisambiguationItem(
        PortfolioListItem chosen, int matchCount, string supplierDisplayName)
    {
        const string ChosenContractLabel = "CT-01";

        // Never fabricated (Appendix C rule 10): renewal date first (what RenewalPipelineBuilder's
        // own sort actually ordered on when known), else the cancellation deadline, else the bare
        // end date, else an honest "unknown" rather than inventing one.
        var renewalText = chosen.RenewalDate is { } renewalDate
            ? $"renews {renewalDate:yyyy-MM-dd}"
            : chosen.CancellationDeadline is { } deadline
                ? $"cancellation deadline {deadline:yyyy-MM-dd}"
                : chosen.EndDate is { } endDate
                    ? $"ends {endDate:yyyy-MM-dd}"
                    : "renewal date unknown";

        var snippet =
            $"Using {chosen.Type} {ChosenContractLabel} ({renewalText}). {supplierDisplayName} has " +
            $"{matchCount} contracts on file — ask if you meant another.";

        return new PackItem(
            InsightsCitationKeys.Calc("multi-contract-resolution"),
            PackCorpus.Calc,
            $"{supplierDisplayName} — multiple contracts",
            null,
            null,
            null,
            snippet,
            null, // Href is always null for PackCorpus.Calc (PackItem.Href's own doc comment).
            null,
            null,
            "deterministic calculator",
            []);
    }

    /// <summary>
    /// The one recovery action every abstain path this method's caller can reach attaches
    /// (E25/F05/US01/T01, story us-01-abstain-recovery-backend AC-1/AC-3; ADR-024 "every abstain
    /// has a clickable next step"). Never derived from <c>AiAnswerResult.ActionKeys</c> — an
    /// abstaining model has nothing grounded to suggest, and AC-2 requires a real catalog href
    /// regardless of what it returned. Zero validated contracts is the one failure Ask can actually
    /// unblock (upload something), so that case gets the Documents upload action; otherwise the
    /// recovery is the Ask capability's own "ask about dates, spend, notice periods and clauses"
    /// hint (<see cref="CapabilityCatalog.AskKey"/>'s own catalog description), which is exactly
    /// what every abstain reply's own prose already suggests trying next. Both target capabilities
    /// are <see cref="CapabilityRoleGate.Any"/>, so this never role-gates away to an empty list.
    /// </summary>
    private IReadOnlyList<CopilotAction> ResolveAbstainRecoveryActions(PortfolioPage portfolio, RoutingContext routingContext) =>
        portfolio.Items.Count == 0
            // Documents is Always-available, so HowTo(DocumentsKey) would Navigate to /documents.
            // UnknownSupplier is the catalog path that emits CopilotActionKind.Upload at /documents.
            ? capabilityRouting.ResolveActions([CapabilityIntent.UnknownSupplier], routingContext)
            : capabilityRouting.ResolveActions([CapabilityIntent.HowTo(CapabilityCatalog.AskKey)], routingContext);

    private CopilotReply BuildRoutingOnlyReply(IntentPlanResult plan, RoutingContext routingContext)
    {
        var intents = plan.Intent == AskIntent.QuoteRoute
            ? new List<CapabilityIntent> { CapabilityIntent.Benchmark }
            : new List<CapabilityIntent> { CapabilityIntent.HowTo(CapabilityCatalog.DocumentsKey) };

        var actions = capabilityRouting.ResolveActions(intents, routingContext);

        var markdown = plan.Intent == AskIntent.QuoteRoute
            ? "That is a job for Quote check: it compares a quote or contract with market " +
              "benchmarks and with what you already pay."
            : "Here you go.";

        return new CopilotReply(ReplyKind.Answer, markdown, [], actions, ReplyProvenance.NoModelCall(["raffa"]), []);
    }

    // ----- Per-intent pack composition -----

    /// <summary>
    /// <paramref name="supplierNames"/> is the per-turn name map <see cref="AskAsync"/> already
    /// resolved for the whole portfolio, so every multi-contract item below can be titled
    /// "Salesforce · MSA" rather than "MSA · MSA" (R-SUP-04; closes the golden set's
    /// GAP-ASK-STRUCTURED-PACK-DROPS-SUPPLIER-NAME). The named-contract branch keeps its own
    /// single lookup, which is the same source of truth.
    /// </summary>
    private async Task<IReadOnlyList<PackItem>> BuildStructuredFactPackAsync(
        string question,
        PortfolioListItem? namedContractItem,
        PortfolioPage portfolio,
        IReadOnlyDictionary<EntityId, string> supplierNames,
        CancellationToken cancellationToken)
    {
        if (namedContractItem is not null)
        {
            var fact = BuildContractFactItem(
                namedContractItem,
                await ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false));
            // Clause rows, not embedding search: InMemory EF cannot translate pgvector
            // CosineDistance, and a date/spend fact should quote the extracted clause when one
            // exists rather than 500 the whole Ask turn.
            var excerpts = await BuildNamedContractExcerptItemsAsync(namedContractItem, cancellationToken)
                .ConfigureAwait(false);
            var grounded = excerpts
                .Where(item => item.DocumentId is not null && !string.IsNullOrWhiteSpace(item.Snippet))
                .ToList();
            if (grounded.Count == 0)
            {
                return [fact];
            }

            // Document quotes first so the cited Ask card is the page excerpt, not the
            // portfolio paraphrase. Copy structured values onto the lead excerpt so numeric
            // grounding of the date/spend still holds.
            var lead = grounded[0] with { Values = fact.Values.Count > 0 ? fact.Values : grounded[0].Values };
            var rest = grounded.Skip(1).Concat(excerpts.Where(item => item.DocumentId is null));
            return [lead, ..rest, fact];
        }

        var routeDecision = _legacyRouter.Route(question);
        if (routeDecision.Intent == QueryIntent.Structured)
        {
            var deterministicQuery = _legacyPlanner.Plan(routeDecision);
            var contractFacts = portfolio.Items.Select(ToContractFact).ToList();
            var result = _legacyHandler.Handle(deterministicQuery, contractFacts);

            if (result.Kind != DeterministicQueryKind.Unsupported)
            {
                var items = new List<PackItem>();
                foreach (var contractId in result.MatchedContractIds)
                {
                    var item = portfolio.Items.FirstOrDefault(i => i.ContractId == contractId.Value);
                    if (item is null)
                    {
                        continue;
                    }

                    items.Add(BuildContractFactItem(item, DisplayNameFor(item, supplierNames)));
                }

                var (aggregateTitle, aggregateSnippet) = DescribeStructuredResult(result);

                items.Add(new PackItem(
                    InsightsCitationKeys.Calc("structured-query"),
                    PackCorpus.Calc,
                    aggregateTitle,
                    null,
                    null,
                    null,
                    aggregateSnippet,
                    null,
                    null,
                    null,
                    "deterministic calculator",
                    result.AggregateAnnualSpend is { } total
                        ? [new PackValue("aggregateAnnualSpend", total.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, "n/a")]
                        : [new PackValue("matchedContractCount", result.MatchedContractIds.Count.ToString(CultureInfo.InvariantCulture), PackValueKind.Number)]));

                return items;
            }
        }

        // Neither a named supplier nor a recognized deterministic query — an honest portfolio
        // snapshot (soonest-first) rather than nothing at all.
        return portfolio.Items
            .OrderBy(item => item.EndDate ?? DateOnly.MaxValue)
            .Take(5)
            .Select(item => BuildContractFactItem(item, DisplayNameFor(item, supplierNames)))
            .ToList();
    }

    /// <summary>
    /// Human-facing title/snippet for the one calculator-derived <see cref="PackItem"/>
    /// <see cref="BuildStructuredFactPackAsync"/> layers on top of the per-contract fact items it
    /// builds from <see cref="DeterministicQueryResult.MatchedContractIds"/> — the aggregate
    /// count/total a "which contracts renew in the next 120 days" or "what is our annual spend"
    /// question needs grounded, over and above each individual contract's own citation.
    ///
    /// <para>
    /// Deliberately never <see cref="DeterministicQueryResult.Explanation"/> itself: that field's
    /// own doc comment says plainly it is "not meant to be shown to an end user as-is" — a
    /// developer/test trace naming the literal filter predicate (e.g. "deterministic filter on
    /// Contract.AutoRenewal/EndDate, Appendix C rule 6 — no LLM"). Both
    /// <see cref="Raffa.Chat.Application.Reply.CopilotReplyBuilder.BuildCitations"/> (copies a
    /// cited <see cref="PackItem.Title"/>/<see cref="PackItem.Snippet"/> verbatim into
    /// <see cref="Raffa.Chat.Application.Reply.ReplyCitation.Title"/>/<c>.Snippet</c>) and
    /// <c>Raffa.AiGateway.Fixtures.FixtureAiGateway.AnswerFromPack</c>'s own deterministic echo
    /// (<c>"[{n}] {item.Title}: ..."</c>, folded straight into
    /// <see cref="Raffa.Chat.Application.Reply.CopilotReply.AnswerMarkdown"/>) can surface
    /// whatever this method returns directly to the end user — this is the one place standing
    /// between a developer-trace string and R-ASK-08 ("Engineer chrome... is never rendered
    /// here"; this task's own DoD line: "no `Document:` guid and no 'Structured query' substring
    /// in any reply").
    /// </para>
    /// </summary>
    private static (string Title, string Snippet) DescribeStructuredResult(DeterministicQueryResult result) =>
        result.Kind switch
        {
            DeterministicQueryKind.RenewalWindow => (
                "Contracts matching your renewal window",
                $"{result.MatchedContractIds.Count} contract(s) in your portfolio auto-renew inside " +
                "the window you asked about."),

            DeterministicQueryKind.AnnualSpend => (
                "Annual spend total",
                result.SupplierScopeUnresolved
                    ? $"Total annual spend across {result.MatchedContractIds.Count} validated " +
                      "contract(s) with a recorded figure — Raffa could not match the supplier " +
                      "you named, so this total is company-wide, not scoped to it."
                    : $"Total annual spend across {result.MatchedContractIds.Count} validated " +
                      "contract(s) with a recorded figure."),

            // Unreachable in practice — BuildStructuredFactPackAsync only ever calls this helper
            // inside its own "result.Kind != DeterministicQueryKind.Unsupported" branch — but kept
            // exhaustive and just as chrome-free as the two real cases above, never a default that
            // silently reintroduces developer trace text if a future DeterministicQueryKind member
            // ever reaches here uncovered.
            _ => ("Portfolio query result", "Raffa computed this from your validated contracts."),
        };

    // Task E30/F01/US01/T01 (NW-91/NW-92): the same notice/preavviso/disdetta/cancellation-deadline
    // lexicon IntentPlanner.NoticePattern already matches to steer this question to
    // AskIntent.StructuredFact in the first place (task E27/F01/US01/T01, NW-79/NW-91) --
    // duplicated here, not referenced, because IntentPlanner.cs is outside this task's own "Files
    // to create or modify" (the same "each composition file owns its own copy" shape
    // InsightsEndpointExtensions.ComputeRenewal's own doc comment already accepts for an identical
    // reason).
    private static readonly Regex NoticeQuestionPattern = new(
        @"\b(notice|preavviso|disdetta(\s+period)?|cancellation\s+deadline)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // The matching-clause evidence item's own vocabulary (task E30/F01/US01/T01): a notice question
    // is supported by whichever of this contract's own extracted clauses actually discusses when or
    // how notice must be given -- termination, cancellation, notice and auto-renewal clauses all
    // qualify; a generic "payment terms" or "liability" clause never does.
    private static readonly Regex NoticeClauseTypePattern = new(
        @"notice|cancellat|terminat|auto.?renew|renewal",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Task E30/F01/US01/T01 (NW-91/NW-92): <see cref="AskIntent.StructuredFact"/> covers both a
    /// plain structured-fact question and a notice/preavviso/disdetta one — <see cref="IntentPlanner"/>
    /// deliberately reuses the one intent for both (its own notice-lexicon doc comment) rather than
    /// adding an eleventh <see cref="AskIntent"/> member. This is the split point: a notice question
    /// about a resolved, named contract gets the structured notice pack
    /// (<see cref="BuildNoticePackAsync"/>); everything else (no contract in scope, or a plain
    /// dates/spend question) keeps the pre-existing <see cref="BuildStructuredFactPackAsync"/>
    /// behaviour unchanged.
    /// </summary>
    private async Task<IReadOnlyList<PackItem>> BuildStructuredFactOrNoticePackAsync(
        string question,
        PortfolioListItem? namedContractItem,
        PortfolioPage portfolio,
        IReadOnlyDictionary<EntityId, string> supplierNames,
        CancellationToken cancellationToken) =>
        namedContractItem is not null && NoticeQuestionPattern.IsMatch(question)
            ? await BuildNoticePackAsync(namedContractItem, cancellationToken).ConfigureAwait(false)
            : await BuildStructuredFactPackAsync(question, namedContractItem, portfolio, supplierNames, cancellationToken).ConfigureAwait(false);

    /// <summary>
    /// Task E30/F01/US01/T01 (NW-91/NW-92, parent story us-01-notice-pack AC-1/AC-2/AC-3): the
    /// structured notice pack, in the task's own pack order — the scoped fact
    /// (<c>endDate</c>/<c>cancellationDeadline</c>/<c>autoRenewal</c>/<c>renewalTermMonths</c>), the
    /// host-computed day count, <see cref="StrategyPackBuilder"/>'s own "when you must move"
    /// explanation, then a matching-clause evidence item when one exists. See this type's own doc
    /// comment for the full rationale (why RAG is never called from here despite the coding
    /// objective naming it a fallback).
    ///
    /// <para>
    /// <b>Never model arithmetic (AC-2, NW-92)</b>: every date below is read straight off
    /// <see cref="Contract360Renewal"/> (never re-derived), and the day count is
    /// <see cref="InsightsEndpointExtensions.ToStrategyInputs"/>'s own fallback — the same
    /// <see cref="RenewalEngine"/> result <see cref="BuildRenewalStrategyPackAsync"/> already
    /// computes, falling back to this contract's own asOf-relative day count only because
    /// <see cref="Raffa.Renewals.Application.ContractRenewalTerms.CancellationNoticeDays"/> has no
    /// persisted column this wave (that record's own doc comment) — never
    /// <c>EndDate − CancellationDeadline</c>, which this task's own coding objective forbids
    /// outright.
    /// </para>
    /// </summary>
    internal async Task<IReadOnlyList<PackItem>> BuildNoticePackAsync(
        PortfolioListItem namedContractItem, CancellationToken cancellationToken)
    {
        var contract360 = await contract360QueryService
            .GetByIdAsync(CurrentTenantId, new EntityId(namedContractItem.ContractId), cancellationToken)
            .ConfigureAwait(false);

        if (contract360 is null)
        {
            return [];
        }

        var supplierName = await ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false);
        var asOfDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        // Same RenewalEngine + IClock composition BuildRenewalStrategyPackAsync already uses (this
        // task's own "daysUntilNotice computed host-side via RenewalEngine/IClock") --
        // ToStrategyInputs' own fallback (that method's doc comment: "the cancellation deadline
        // comes from two places") is what actually produces a day count when, as always this wave,
        // no notice-day count is on file: it falls back to Contract360Header.CancellationDeadline
        // (the same raw fact Contract360Renewal.CancellationDeadline carries) with the days-left
        // count derived from asOfDate the same way the engine would -- never EndDate minus
        // CancellationDeadline.
        var renewal = InsightsEndpointExtensions.ComputeRenewal(contract360.Header, renewalEngine);
        var strategyInputs = InsightsEndpointExtensions
            .ToStrategyInputs(contract360, renewal, pricedLines: [], criticalFacts: [], asOfDate)
            with
            { SupplierName = supplierName };
        var whenYouMustMove = StrategyPackBuilder.Build(strategyInputs).WhenYouMustMove;

        var items = new List<PackItem>
        {
            BuildNoticeFactItem(namedContractItem, contract360.Renewal, supplierName),
            BuildWhenYouMustMoveItem(namedContractItem.ContractId, supplierName, whenYouMustMove),
        };

        var clauseItem = BuildMatchingClauseItem(contract360, namedContractItem.ContractId);
        if (clauseItem is not null)
        {
            items.Add(clauseItem);
        }

        return items;
    }

    /// <summary>
    /// Task E30/F01/US01/T01: the notice pack's first item — the scoped fact itself
    /// (<c>endDate</c>/<c>cancellationDeadline</c>/<c>autoRenewal</c>/<c>renewalTermMonths</c>, task
    /// text verbatim), read straight off <paramref name="renewal"/> (<see cref="Contract360Renewal"/>,
    /// already tenant+contract scoped by <see cref="Contract360QueryService.GetByIdAsync"/>).
    /// <c>autoRenewal</c> itself carries no <see cref="PackValue"/> (no <see cref="PackValueKind"/>
    /// fits a boolean) — it decides which of the three honest snippets below applies instead, the
    /// same role it already plays in <see cref="BuildContractFactItem"/>'s own snippet. Every date is
    /// a <see cref="PackValueKind.Date"/> value (AC-2); <c>renewalTermMonths</c> is a bare
    /// <see cref="PackValueKind.Number"/>, never fabricated when the contract has none on file.
    /// </summary>
    private static PackItem BuildNoticeFactItem(PortfolioListItem item, Contract360Renewal renewal, string displayName)
    {
        var values = new List<PackValue>();
        if (renewal.EndDate is { } endDate)
        {
            values.Add(new PackValue("endDate", endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PackValueKind.Date));
        }

        if (renewal.CancellationDeadline is { } deadline)
        {
            values.Add(new PackValue("cancellationDeadline", deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PackValueKind.Date));
        }

        if (renewal.RenewalTermMonths is { } termMonths)
        {
            values.Add(new PackValue("renewalTermMonths", termMonths.ToString(CultureInfo.InvariantCulture), PackValueKind.Number));
        }

        string snippet;
        if (!renewal.AutoRenewal)
        {
            // AC-3: autoRenewal=false -> no notice window, the contract just ends on endDate.
            snippet = renewal.EndDate is { } end
                ? $"{displayName} does not auto-renew: no notice window applies, the contract ends on {end:yyyy-MM-dd}."
                : $"{displayName} does not auto-renew, and Raffa has no end date on file yet (Appendix C rule 10).";
        }
        else if (renewal.CancellationDeadline is { } deadlineDate)
        {
            // AC-3's fixture shape: the date, then "if missed" only when a real renewalTermMonths is
            // on file -- never a fabricated "renews automatically" with no term to name.
            snippet = renewal.RenewalTermMonths is { } months
                ? $"{displayName}'s notice deadline is {deadlineDate:yyyy-MM-dd}. If missed, the " +
                  $"contract renews for {months} month(s)."
                : $"{displayName}'s notice deadline is {deadlineDate:yyyy-MM-dd}.";
        }
        else
        {
            snippet = $"{displayName} auto-renews, but Raffa has no validated notice deadline on " +
                "file yet (Appendix C rule 10).";
        }

        return new PackItem(
            $"fact:{item.ContractId}:notice",
            PackCorpus.Tenant,
            $"{displayName} · notice",
            null,
            null,
            null,
            snippet,
            $"/contracts/{item.ContractId}",
            null,
            null,
            "validated contract",
            values,
            item.ContractId.ToString());
    }

    /// <summary>
    /// Task E30/F01/US01/T01: the notice pack's second item — <see cref="StrategyPackBuilder"/>'s
    /// own "when you must move" explanation (task text: "miss / passed / no auto-renew"), the same
    /// honest narration <see cref="BuildRenewalStrategyPackAsync"/> already cites verbatim, never
    /// re-worded here. <c>daysUntilNotice</c> is the one new <see cref="PackValue"/> this item
    /// carries beyond <see cref="BuildDateValues"/>'s existing two dates — AC-3's "deadline passed N
    /// days ago" names an N that must itself be a pack value, and
    /// <see cref="WhenYouMustMove.DaysLeft"/> (signed — negative means already passed, never floored
    /// to zero, that record's own doc comment) is exactly that N, computed by the calculators, never
    /// restated by a model.
    /// </summary>
    private static PackItem BuildWhenYouMustMoveItem(Guid contractId, string displayName, WhenYouMustMove whenYouMustMove)
    {
        var values = new List<PackValue>(BuildDateValues(whenYouMustMove.RenewalDate, whenYouMustMove.CancellationDeadline));
        if (whenYouMustMove.DaysLeft is { } daysLeft)
        {
            values.Add(new PackValue("daysUntilNotice", daysLeft.ToString(CultureInfo.InvariantCulture), PackValueKind.Number));
        }

        return new PackItem(
            InsightsCitationKeys.Calc("when-you-must-move"),
            PackCorpus.Calc,
            $"{displayName} — when you must move",
            null,
            null,
            null,
            whenYouMustMove.Explanation,
            $"/contracts/{contractId}",
            null,
            null,
            "deterministic calculator",
            values,
            contractId.ToString());
    }

    /// <summary>
    /// Task E30/F01/US01/T01: the notice pack's optional third item — supporting evidence for the
    /// <c>cancellationDeadline</c> fact above, the first of this contract's own extracted
    /// <see cref="Contract360Clause"/> rows whose <see cref="Contract360Clause.ClauseType"/> or
    /// <see cref="Contract360Clause.RawText"/> names notice/cancellation/termination/auto-renewal
    /// (<see cref="NoticeClauseTypePattern"/>). <see langword="null"/> when none matches — "else
    /// abstain honestly" (task text): the fact and explanation items already ground the reply, so a
    /// missing evidence item narrows the citation, it never becomes a fabricated one. Reuses
    /// <see cref="ResolveTenantClauseLinks"/>'s own tier-1 viewer link, the identical resolution
    /// <see cref="BuildClausePackAsync"/> already gives a resolved <see cref="Contract360Clause"/>.
    /// </summary>
    private static PackItem? BuildMatchingClauseItem(Contract360Result contract360, Guid namedContractId)
    {
        var clause = contract360.Clauses.FirstOrDefault(c =>
            NoticeClauseTypePattern.IsMatch(c.ClauseType) || NoticeClauseTypePattern.IsMatch(c.RawText));

        if (clause is null)
        {
            return null;
        }

        var (href, previewUrl) = ResolveTenantClauseLinks(clause, clause.ClauseId, namedContractId);
        var subtitle = clause.SourcePage is { } page
            ? $"p.{page}" + (clause.SourceSpan is { } span ? $" §{span}" : string.Empty)
            : null;

        return new PackItem(
            $"fact:{clause.ClauseId}:notice-clause",
            PackCorpus.Tenant,
            $"{clause.ClauseType} clause",
            subtitle,
            clause.SourcePage,
            clause.SourceSpan,
            clause.RawText,
            href,
            previewUrl,
            null,
            "validated contract",
            [],
            namedContractId.ToString(),
            clause.SourceDocumentId?.Value.ToString());
    }

    // ----- Notice fallbacks (task E30/F02/US01/T01, NW-94) -----

    /// <summary>
    /// Task E30/F02/US01/T01 (NW-94; parent story us-01-notice-fallbacks AC-1/AC-2/AC-3, "a scoped
    /// notice turn never asks 'which supplier'"): the five server-decided outcomes for a notice
    /// question, called directly from <see cref="BuildInDomainReplyAsync"/>'s own short-circuit
    /// (see that method's own comment at the call site) rather than through
    /// <see cref="BuildStructuredFactOrNoticePackAsync"/>'s wrapper -- this method reuses
    /// <see cref="BuildNoticeFactItem"/>/<see cref="BuildMatchingClauseItem"/> directly (the same
    /// two feature-01 helpers <see cref="BuildNoticePackAsync"/> itself calls) so it gets each
    /// item typed instead of re-parsing a flattened <see cref="PackItem"/> list, and resolves
    /// <see cref="Contract360Result"/> exactly once, the same "one fetch per intent" shape every
    /// other <c>BuildXxxPackAsync</c> method in this file already follows.
    ///
    /// <para>
    /// <b>Case 5 first -- unscoped deictic</b>: <paramref name="namedContractItem"/> is
    /// <see langword="null"/> -- no conversation scope, no resolved named supplier. There is no
    /// "this contract" to answer about, and falling through to the generic structured-fact
    /// portfolio snapshot (as a pre-NW-94 notice question would have) risks exactly the confused,
    /// half-scoped experience this story rules out. <see cref="ReplyKind.Abstain"/>, a Portfolio
    /// recovery action (<see cref="BuildUnscopedNoticeAbstain"/>) -- send the caller to pick a
    /// contract, never a guessed one.
    /// </para>
    ///
    /// <para>
    /// <b>Cases 1-4 -- a resolved contract</b>: two independent facts decide the outcome.
    /// <c>hasDeadline</c> is <see cref="Contract360Renewal.CancellationDeadline"/> known -- the
    /// literal date NW-92 requires this pack to answer with, read the same way
    /// <see cref="BuildNoticeFactItem"/>'s own primary branch already reads it. <c>hasSpan</c> is
    /// the matching clause resolving to a real document page --
    /// <see cref="ResolveTenantClauseLinks"/>'s tier 1 -- read straight off the already-built
    /// <see cref="PackItem.Page"/>/<see cref="PackItem.DocumentId"/> rather than re-deriving that
    /// tiering a second time (tier 1 is exactly "both are non-null", per that method's own doc
    /// comment).
    /// <list type="number">
    /// <item>Deadline + span -&gt; <see cref="ReplyKind.Answer"/>, citing the fact item and the
    /// clause item -- a clause citation whose own <see cref="PackItem.ContractId"/>/
    /// <see cref="PackItem.DocumentId"/>/<see cref="PackItem.Page"/>/<see cref="PackItem.Href"/>
    /// are all real (NW-83) is what lets the client build the two-CTA card from it alone (NW-93;
    /// "software-architect: payload carries the ids; no new reply kind" -- this reply's own
    /// <see cref="ReplyKind"/> is the ordinary <see cref="ReplyKind.Answer"/>).</item>
    /// <item>Deadline, no span -&gt; <see cref="ReplyKind.Answer"/>, citing the fact item alone --
    /// never the clause, whether because none matched or because it matched with no page anchor:
    /// citing an unanchored clause here would blur this case with case 3's own "quote the clause"
    /// shape, and there is no page to send the reader to either way (no fabricated page).</item>
    /// <item>No deadline, a clause matches -&gt; <see cref="ReplyKind.Answer"/>, quoting the
    /// clause's own text (<see cref="PackItem.Snippet"/>) verbatim as the entire answer -- there is
    /// no date to state, so the clause text is the whole grounded claim.</item>
    /// <item>Neither -&gt; <see cref="ReplyKind.Abstain"/>, naming this contract's own supplier
    /// (<see cref="BuildNoGroundableNoticeAbstain"/>) -- NW-59's own "an abstain still carries a
    /// real, catalog-sourced recovery action" pattern (<c>AskAbstainRecoveryActionTests</c>), here
    /// specialised to the one action that actually helps: open the contract this turn already
    /// named, never the generic <see cref="ResolveAbstainRecoveryActions"/> hint.</item>
    /// </list>
    /// A contract resolved by <see cref="ResolveNamedContractItem"/> but since vanished from
    /// <see cref="Contract360QueryService"/> (deleted mid-call -- the same rare race
    /// <see cref="BuildNoticePackAsync"/> itself already answers with an empty pack) falls straight
    /// into the "neither" branch: there is no fact and no evidence either way, so it is
    /// indistinguishable from a contract that genuinely has neither.
    /// </para>
    ///
    /// <para>
    /// <b>Disambiguation is never dropped (NW-80 "never silently merge")</b>:
    /// <paramref name="disambiguationItem"/> -- <see cref="BuildMultiContractDisambiguationItem"/>'s
    /// own pack item, non-null only when <see cref="ResolveNamedContractItem"/> picked one contract
    /// among several sharing a supplier's display name -- is prepended to every answer case's own
    /// citations and to its own answer text, the identical "prepended, not appended" rule
    /// <see cref="BuildInDomainReplyAsync"/>'s own pack-building path already applies, so this
    /// short-circuit cannot silently narrate the wrong one of several same-name contracts either.
    /// Never folded into the case-4 abstain: an abstain already says "cannot determine", and this
    /// keeps that reply's shape identical to every other abstain in this file (no citations).
    /// </para>
    /// </summary>
    /// <param name="namedContractItem">The turn's own resolved contract (scoped id, or a
    /// name/soonest-deadline match) -- <see langword="null"/> for case 5.</param>
    /// <param name="disambiguationItem">Echoes <see cref="BuildInDomainReplyAsync"/>'s own
    /// same-named local of this call -- see this method's own "Disambiguation is never dropped"
    /// paragraph above.</param>
    /// <param name="routingContext">The turn's own already-resolved <see cref="RoutingContext"/>
    /// (built once by <see cref="BuildInDomainReplyAsync"/>, before this method is ever called) --
    /// its <see cref="RoutingContext.ContractId"/> already matches <paramref name="namedContractItem"/>
    /// exactly, so every action below resolves through the identical routing facts the rest of this
    /// turn uses.</param>
    private async Task<CopilotReply> BuildNoticeFallbackReplyAsync(
        PortfolioListItem? namedContractItem,
        PackItem? disambiguationItem,
        RoutingContext routingContext,
        CancellationToken cancellationToken)
    {
        if (namedContractItem is null)
        {
            return BuildUnscopedNoticeAbstain(routingContext);
        }

        var contract360 = await contract360QueryService
            .GetByIdAsync(CurrentTenantId, new EntityId(namedContractItem.ContractId), cancellationToken)
            .ConfigureAwait(false);

        var supplierName = await ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false);

        // Case 1/2/4's shared action -- "360 Review" (task text) -- always resolves for a scoped
        // turn: ContractDetailKey's own CapabilityAvailability.Always (CapabilityCatalog.cs) never
        // routes through the NeedsValidatedContract/Upload replacement, unlike PortfolioKey below.
        var reviewActions = capabilityRouting.ResolveActions(
            [CapabilityIntent.HowTo(CapabilityCatalog.ContractDetailKey)], routingContext);

        if (contract360 is null)
        {
            return BuildNoGroundableNoticeAbstain(supplierName, reviewActions);
        }

        var factItem = BuildNoticeFactItem(namedContractItem, contract360.Renewal, supplierName);
        var clauseItem = BuildMatchingClauseItem(contract360, namedContractItem.ContractId);
        var hasDeadline = contract360.Renewal.CancellationDeadline is not null;
        var hasSpan = clauseItem is { Page: not null, DocumentId: not null };

        var groundingItems = new List<PackItem>();
        if (disambiguationItem is not null)
        {
            groundingItems.Add(disambiguationItem);
        }

        if (hasDeadline)
        {
            groundingItems.Add(factItem);
            if (hasSpan)
            {
                groundingItems.Add(clauseItem!);
            }

            return BuildNoticeAnswer(PrefixWithDisambiguation(factItem.Snippet, disambiguationItem), groundingItems, reviewActions);
        }

        if (clauseItem is not null)
        {
            groundingItems.Add(clauseItem);
            return BuildNoticeAnswer(PrefixWithDisambiguation(clauseItem.Snippet, disambiguationItem), groundingItems, reviewActions);
        }

        return BuildNoGroundableNoticeAbstain(supplierName, reviewActions);
    }

    private static string PrefixWithDisambiguation(string answer, PackItem? disambiguationItem) =>
        disambiguationItem is null ? answer : $"{disambiguationItem.Snippet} {answer}";

    /// <summary>Cases 1/2/3's shared reply shape: an <see cref="ReplyKind.Answer"/> grounded in
    /// <paramref name="groundingItems"/> alone (in the order the case built them), never a model
    /// call. <see cref="ReplyProvenance.Sources"/> is derived from the resolved citations, the same
    /// way <see cref="CopilotReplyBuilder.FromGuardedResult"/> derives it, rather than hard-coded --
    /// a prepended disambiguation item is <see cref="PackCorpus.Calc"/>, not
    /// <see cref="PackCorpus.Tenant"/>, so a fixed single-source list would under-report it.</summary>
    private static CopilotReply BuildNoticeAnswer(
        string answerMarkdown, IReadOnlyList<PackItem> groundingItems, IReadOnlyList<CopilotAction> actions)
    {
        var citations = CopilotReplyBuilder.BuildCitations(
            groundingItems.Select(item => item.CitationKey).ToList(), groundingItems);
        var sources = citations.Select(c => c.Corpus).Distinct(StringComparer.Ordinal).ToList();

        return new CopilotReply(ReplyKind.Answer, answerMarkdown, citations, actions, ReplyProvenance.NoModelCall(sources), []);
    }

    /// <summary>Case 4 (a resolved contract with neither a deadline nor a matching clause) and the
    /// rare contract360-null race both collapse here -- see this type's "A contract resolved... but
    /// since vanished" note on <see cref="BuildNoticeFallbackReplyAsync"/>. No citations, matching
    /// every other abstain in this file (<see cref="ReplyKind.Abstain"/>'s own doc comment: "empty
    /// unless a citation genuinely backs the decline").</summary>
    private static CopilotReply BuildNoGroundableNoticeAbstain(string supplierName, IReadOnlyList<CopilotAction> reviewActions) =>
        new(
            ReplyKind.Abstain,
            $"Raffa could not find a validated notice deadline for {supplierName}, and no clause on " +
            "file names one either. Open Contract 360 to review the source document.",
            [],
            reviewActions,
            ReplyProvenance.NoModelCall([]),
            []);

    /// <summary>Case 5 (NW-94): an unscoped notice question has no "this contract" to answer about.
    /// Abstains with a Portfolio recovery instead of guessing one -- the honest counterpart, for the
    /// unscoped turn, to this story's own "a scoped notice turn never asks 'which supplier'": never
    /// guess, and never ask either, just say so and point at the one screen that lets the caller
    /// pick. <see cref="CapabilityCatalog.PortfolioKey"/>'s own <c>NeedsValidatedContract</c>
    /// availability (CapabilityCatalog.cs) already replaces this with the Documents upload action
    /// for a zero-validated-contract tenant (<see cref="CapabilityRouting"/>'s own "Availability
    /// replacement" rule) -- correct here too: Portfolio is exactly as unusable as the notice
    /// question itself would be for that tenant.</summary>
    private CopilotReply BuildUnscopedNoticeAbstain(RoutingContext routingContext) =>
        new(
            ReplyKind.Abstain,
            "This looks like a notice question, but no contract is in scope for this conversation. " +
            "Open Portfolio and ask again from the contract you mean.",
            [],
            capabilityRouting.ResolveActions([CapabilityIntent.HowTo(CapabilityCatalog.PortfolioKey)], routingContext),
            ReplyProvenance.NoModelCall([]),
            []);

    /// <summary>
    /// Task E28/F02/US01/T01 (NW-81; ADR-024 w19 cl. 15; parent story us-01-rag-contract-filter
    /// AC-1/AC-2/AC-3): when a contract is named, this used to call
    /// <see cref="EmbeddingRetrievalService.SearchAsync"/> -- a cosine top-K over <b>every</b>
    /// embedding this tenant owns -- and relied on <paramref name="namedContractItem"/> only to
    /// resolve a citation's clause/link, never to filter the search itself. That is the exact NW-81
    /// defect: a tenant with 37 contracts could get another supplier's MSA back for a question about
    /// one named contract (Q2/Q3 "do not mix 37 contracts"). This now calls
    /// <see cref="EmbeddingRetrievalService.SearchByContractAsync"/>, which returns two slices --
    /// this contract's own chunks, and a lower-K, separately labelled "similar types" peer slice
    /// from other validated contracts of the same
    /// <see cref="Raffa.Documents.Contracts.Domain.ContractDocumentType"/> (AC-1/AC-2) --
    /// and builds the pack from those instead. Market notes are never reached from here at all
    /// (AC-3): <see cref="IMarketKnowledgeRetrieval"/> is only ever called from
    /// <see cref="BuildMarketComparePackAsync"/>, a different intent branch.
    /// </summary>
    private async Task<IReadOnlyList<PackItem>> BuildClausePackAsync(
        TenantId tenantId, string question, PortfolioListItem? namedContractItem, CancellationToken cancellationToken)
    {
        if (namedContractItem is null)
        {
            // No contract is in scope for this turn (a fully generic clause question) -- there is
            // no "this contract" or "similar types" to split against, so this is the one path that
            // still searches the whole tenant corpus. ADR-024 w19's own NW-79 amendment still names
            // this the "unscoped Clause RAG" fallback; NW-81 only requires filtering a turn that
            // already names a contract.
            try
            {
                var tenantWideResult = await embeddingRetrievalService
                    .SearchAsync(tenantId, question, ClauseTopK, cancellationToken)
                    .ConfigureAwait(false);

                return tenantWideResult.IsFailure
                    ? []
                    : tenantWideResult.Value
                        .Select(hit => BuildClausePackItem(hit, clause: null, namedContractId: null, isPeer: false))
                        .ToList();
            }
            catch (InvalidOperationException)
            {
                // EF InMemory cannot translate Vector.CosineDistance (see InMemoryAskEngineFactory).
                return [];
            }
        }

        var contractId = new EntityId(namedContractItem.ContractId);

        Result<EmbeddingContractScopedSearchResult> searchResult;
        try
        {
            searchResult = await embeddingRetrievalService
                .SearchByContractAsync(
                    new EmbeddingSearchQuery(tenantId, question, ClauseTopK, contractId, ClausePeerTopK),
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // EF InMemory cannot translate Vector.CosineDistance (see InMemoryAskEngineFactory).
            return [];
        }

        if (searchResult.IsFailure)
        {
            return [];
        }

        var contract360 = await contract360QueryService
            .GetByIdAsync(tenantId, contractId, cancellationToken)
            .ConfigureAwait(false);

        var items = new List<PackItem>();

        foreach (var hit in searchResult.Value.ThisContract)
        {
            var clause = contract360?.Clauses.FirstOrDefault(c => c.ClauseId == hit.SourceId);
            items.Add(BuildClausePackItem(hit, clause, namedContractItem.ContractId, isPeer: false));
        }

        // AC-2: labelled separately, and never resolved against this contract's own Contract360
        // clauses or link -- a peer hit belongs to a different, unresolved contract by construction
        // (SearchByContractAsync's own peer slice already excludes contractId itself), so it must
        // never be citable as if it were this contract's own evidence (R-ASK-04).
        foreach (var hit in searchResult.Value.SimilarTypes)
        {
            items.Add(BuildClausePackItem(hit, clause: null, namedContractId: null, isPeer: true));
        }

        return items;
    }

    /// <summary>
    /// One clause-pack <see cref="PackItem"/> from a raw <see cref="EmbeddingSearchResult"/> hit
    /// (task E28/F02/US01/T01, NW-81 AC-1/AC-2). <paramref name="isPeer"/> marks a hit from the
    /// "similar types" peer slice: its title and provenance are labelled as a different, merely
    /// similar-type contract so neither the model nor a reader can mistake it for
    /// <paramref name="namedContractId"/>'s own evidence -- callers pass <see langword="null"/> for
    /// both <paramref name="clause"/> and <paramref name="namedContractId"/> on a peer hit, which
    /// also (via <see cref="ResolveTenantClauseLinks"/>'s own fallback) leaves it with no href: a
    /// peer citation names a fact pattern, never a clickable route into a contract the pack never
    /// resolved.
    ///
    /// <para>
    /// <b>Real ids, not the citation key (task E28/F03/US01/T01, NW-83; ADR-024 w19 cl. 17 "no
    /// citation without a pack source")</b>: <c>ContractId</c> is <paramref name="namedContractId"/>
    /// verbatim -- already <see langword="null"/> for a peer or an unscoped (tenant-wide) hit, so
    /// this never re-derives it. <c>DocumentId</c> is the resolved clause's own
    /// <see cref="Contract360Clause.SourceDocumentId"/>, or -- closing the gap
    /// <see cref="ResolveTenantClauseLinks"/>'s own doc comment used to name -- the hit's own source
    /// id when <see cref="EmbeddingSearchResult.SourceType"/> is <c>"Document"</c>, which is what
    /// <c>DocumentProcessingPipeline.IndexForRetrievalAsync</c> stamps on <em>every</em> chunk it
    /// indexes today (no clause-level embedding exists yet, so this is the common case, not the
    /// edge case). Both are peer-gated through the local <c>hitSourceType</c>/<c>hitPage</c>
    /// variables below, not just <paramref name="namedContractId"/> -- a peer's own document is a
    /// real, resolvable id and would otherwise leak through this fallback despite the href already
    /// being suppressed for it.
    /// </para>
    ///
    /// <para>
    /// <c>internal</c>, not <c>private</c> -- the same test-reachability precedent
    /// <see cref="ResolveTenantClauseLinks"/> already establishes: <c>Raffa.Api.Tests</c> asserts
    /// directly on the returned <see cref="PackItem"/>'s <c>ContractId</c>/<c>DocumentId</c>/
    /// <c>Page</c>/<c>Href</c> (task E28/F03/US01/T01's own Definition of Done line) without first
    /// standing up a full <c>AskAsync</c>/HTTP round trip through a real embedding search.
    /// </para>
    /// </summary>
    internal static PackItem BuildClausePackItem(
        EmbeddingSearchResult hit, Contract360Clause? clause, Guid? namedContractId, bool isPeer)
    {
        // NW-81's own peer-isolation rule, extended to ids: gating the raw hit fields here (not
        // just namedContractId, already null for a peer at the call site) is what stops
        // ResolveTenantClauseLinks' Document-sourced tier from resolving a peer's own document into
        // a clickable href, and DocumentId below from resolving it into an id, either.
        var hitSourceType = isPeer ? null : hit.SourceType;
        var hitPage = isPeer ? null : hit.Page;

        var (href, previewUrl) = ResolveTenantClauseLinks(clause, hit.SourceId, namedContractId, hitSourceType, hitPage);

        var baseTitle = clause is not null ? $"{clause.ClauseType} clause" : $"{hit.SourceType} excerpt";
        var subtitle = clause?.SourcePage is { } page
            ? $"p.{page}" + (clause.SourceSpan is { } span ? $" §{span}" : string.Empty)
            : null;

        var documentId = clause?.SourceDocumentId?.Value.ToString()
            ?? (string.Equals(hitSourceType, "Document", StringComparison.Ordinal) ? hit.SourceId.Value.ToString() : null);

        return new PackItem(
            $"fact:{hit.SourceId}:chunk[{hit.ChunkIndex}]",
            PackCorpus.Tenant,
            isPeer ? $"Similar contract — {baseTitle}" : baseTitle,
            isPeer ? "similar contract, not this one" : subtitle,
            clause?.SourcePage ?? hit.Page,
            clause?.SourceSpan ?? hit.Section ?? $"chunk {hit.ChunkIndex}",
            hit.ChunkText,
            href,
            previewUrl,
            null,
            isPeer ? "similar validated contract" : "validated contract",
            [],
            namedContractId?.ToString(),
            documentId);
    }

    /// <summary>
    /// Task E25/F02/US01/T01 (NW-55; ADR-024; ADR-018 w17 clause 9 viewer route): the tenant
    /// citation card's real deep-link and page preview -- <c>/documents/:documentId/viewer?page=
    /// &amp;clause=</c> and the existing <c>/api/documents/{id}/preview?page=</c> route -- replacing
    /// the bare <c>/contracts/{id}</c> CTA whenever this hit actually resolves to one real page of
    /// one real document. Same shape, and the same fallback rule, as the Contract 360 client's own
    /// <c>contract360ViewModel.ts</c> <c>resolveViewerHref</c> ("No document or no sourcePage -&gt;
    /// no link"): only when <paramref name="clause"/> carries both a
    /// <see cref="Contract360Clause.SourceDocumentId"/> and a <see cref="Contract360Clause.SourcePage"/>
    /// does this return the viewer pair.
    ///
    /// <para>
    /// <b>Three tiers (task E28/F03/US01/T01, NW-83; parent story us-01-citation-ids-backend AC-2)</b>:
    /// (1) <paramref name="clause"/> resolves to a real document page -- the viewer, clause
    /// included, exactly as before this task. (2) No <c>Clause</c> row, but
    /// <paramref name="hitSourceType"/> is <c>"Document"</c> and <paramref name="hitPage"/> is known
    /// -- the same viewer, page only (no clause to highlight). This is the honest gap this method's
    /// own doc comment used to name ("the Document-sourced-chunk branch is not attempted here") --
    /// closed now because it is, today, the <em>only</em> shape a real indexed chunk has
    /// (<c>DocumentProcessingPipeline.IndexForRetrievalAsync</c> indexes every page under
    /// <c>Embedding.SourceType == "Document"</c>; no clause-level embedding exists yet). (3) Neither
    /// resolves -- the pre-existing <c>/contracts/{id}</c> CTA, now carrying whichever locator
    /// survives (a clause id we know but could not look up, else a known page) so the 360 screen can
    /// still land the reader near the right evidence -- never a preview (AC-3: previewUrl is tenant
    /// <em>pages</em> only).
    /// </para>
    /// </summary>
    /// <param name="sourceId">The cited chunk's own source id (<see cref="EmbeddingSearchResult.SourceId"/>)
    /// -- the clause id when <paramref name="clause"/> resolved it, else the hit's own source id --
    /// echoed into the viewer's optional <c>?clause=</c> query parameter (ADR-018) or (tier 2) the
    /// viewer route itself.</param>
    /// <param name="namedContractId">The named contract's id, when the caller asked about one
    /// contract by name -- the tier-3 fallback CTA target.</param>
    /// <param name="hitSourceType">The raw hit's own <see cref="EmbeddingSearchResult.SourceType"/>
    /// ("Document"/"Clause"), or <see langword="null"/> when the caller withholds it (a peer hit, or
    /// a pre-NW-83 caller) -- defaulted so every existing call site keeps compiling unchanged.</param>
    /// <param name="hitPage">The raw hit's own <see cref="EmbeddingSearchResult.Page"/>, under the
    /// same withholding rule as <paramref name="hitSourceType"/>.</param>
    internal static (string? Href, string? PreviewUrl) ResolveTenantClauseLinks(
        Contract360Clause? clause, EntityId sourceId, Guid? namedContractId,
        string? hitSourceType = null, int? hitPage = null)
    {
        // Tier 1: a real Clause row anchored to a document page (AC-1/AC-2) -- unchanged from
        // NW-55.
        if (clause is { SourceDocumentId: { } sourceDocumentId, SourcePage: { } sourcePage })
        {
            return (
                $"/documents/{sourceDocumentId.Value}/viewer?page={sourcePage}&clause={sourceId.Value}",
                $"/api/documents/{sourceDocumentId.Value}/preview?page={sourcePage}");
        }

        // Tier 2 ("else viewer ?page= + evidence highlight"): a Document-sourced chunk with a known
        // page -- today's common real-indexing shape (see this method's own doc comment).
        if (clause is null && hitPage is { } page && string.Equals(hitSourceType, "Document", StringComparison.Ordinal))
        {
            return (
                $"/documents/{sourceId.Value}/viewer?page={page}",
                $"/api/documents/{sourceId.Value}/preview?page={page}");
        }

        // Tier 3 ("else 360 ?clause=/?page="): neither resolves -- fall back to the contract route,
        // carrying whichever locator survives.
        if (namedContractId is not { } contractId)
        {
            return (null, null);
        }

        if (clause is null && string.Equals(hitSourceType, "Clause", StringComparison.Ordinal))
        {
            return ($"/contracts/{contractId}?clause={sourceId.Value}", null);
        }

        return (hitPage is { } fallbackPage ? $"/contracts/{contractId}?page={fallbackPage}" : $"/contracts/{contractId}", null);
    }

    /// <summary>
    /// Page excerpts for a named-contract structured fact card, taken from already-extracted
    /// <see cref="Contract360Result.Clauses"/> — the actual document sentence, not the portfolio
    /// paraphrase. Does not call embedding search (InMemory CosineDistance cannot translate).
    /// </summary>
    private async Task<IReadOnlyList<PackItem>> BuildNamedContractExcerptItemsAsync(
        PortfolioListItem namedContractItem, CancellationToken cancellationToken)
    {
        var contract360 = await contract360QueryService
            .GetByIdAsync(CurrentTenantId, new EntityId(namedContractItem.ContractId), cancellationToken)
            .ConfigureAwait(false);

        if (contract360 is null)
        {
            return [];
        }

        var items = new List<PackItem>();
        foreach (var clause in contract360.Clauses)
        {
            if (string.IsNullOrWhiteSpace(clause.RawText) || clause.SourceDocumentId is null)
            {
                continue;
            }

            var (href, previewUrl) = ResolveTenantClauseLinks(
                clause, clause.ClauseId, namedContractItem.ContractId);
            var page = clause.SourcePage;
            var subtitle = page is { } knownPage
                ? $"p.{knownPage}" + (clause.SourceSpan is { } span ? $" §{span}" : string.Empty)
                : clause.SourceSpan;

            items.Add(new PackItem(
                $"fact:{clause.ClauseId}:clause",
                PackCorpus.Tenant,
                $"{clause.ClauseType} clause",
                subtitle,
                page,
                clause.SourceSpan,
                clause.RawText,
                href,
                previewUrl,
                null,
                "validated contract",
                [],
                namedContractItem.ContractId.ToString(),
                clause.SourceDocumentId.Value.ToString()));
        }

        return items;
    }

    /// <summary>
    /// <c>internal</c>, not <c>private</c> — the same test-reachability precedent
    /// <see cref="ResolveTenantClauseLinks"/> already establishes (<c>Raffa.Api.Tests</c>' own
    /// <c>InternalsVisibleTo</c> grant), so <c>Raffa.Api.Tests</c> can assert directly on the
    /// "insufficient market data" branch below without depending on
    /// <c>FixtureAiGateway.AnswerFromPack</c>'s own unrelated top-5-citation cap.
    /// </summary>
    internal async Task<IReadOnlyList<PackItem>> BuildMarketComparePackAsync(
        PortfolioListItem? namedContractItem, CancellationToken cancellationToken)
    {
        if (namedContractItem is null)
        {
            return [];
        }

        var contract360 = await contract360QueryService
            .GetByIdAsync(CurrentTenantId, new EntityId(namedContractItem.ContractId), cancellationToken)
            .ConfigureAwait(false);

        if (contract360 is null)
        {
            return [];
        }

        var supplierName = await ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false);
        var asOfDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        // Same (supplier name, geography) key and the same async ToPricedLines overload
        // BuildRenewalStrategyPackAsync/GET /api/contracts/{id}/strategy use (ADR-024 w17 clause 7;
        // task E28/F01/US01/T01, NW-82) -- one resolution, three consumers, never GoverningLaw as a
        // geography stand-in (see this type's own doc comment).
        var (benchmarkSupplierName, geography) = await ResolveBenchmarkKeyAsync(contract360.Header.SupplierId, cancellationToken)
            .ConfigureAwait(false);

        var pricedLines = await InsightsEndpointExtensions
            .ToPricedLines(contract360, benchmarkService, benchmarkSupplierName, geography, asOfDate, cancellationToken)
            .ConfigureAwait(false);

        var items = new List<PackItem>
        {
            BuildContractFactItem(namedContractItem, supplierName),
        };

        foreach (var line in pricedLines.Take(MaxPricedLinesForBenchmark))
        {
            if (line.UnitPrice is not { } unitPrice)
            {
                continue;
            }

            // Same per-line shape BuildRenewalStrategyPackAsync's own market corpus now reuses (task
            // E31/F01/US01/T01, NW-95) -- one construction, so a missing band never narrates two
            // different "insufficient market data" wordings for the identical line.
            items.Add(BuildMarketPricedLineItem(supplierName, line));

            items.Add(new PackItem(
                $"fact:{namedContractItem.ContractId}:priced-line[{line.Description}].unitPrice".Replace(' ', '-'),
                PackCorpus.Tenant,
                $"{supplierName} · {line.Description} · current price",
                null,
                null,
                null,
                $"Current unit price {unitPrice} {contract360.Overview.Currency}.",
                $"/contracts/{namedContractItem.ContractId}",
                null,
                null,
                "validated contract",
                [new PackValue("unitPrice", unitPrice.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, contract360.Overview.Currency)],
                namedContractItem.ContractId.ToString()));
        }

        var noteQuery = $"{supplierName}";
        var notesResult = await marketKnowledgeRetrieval
            .SearchAsync(noteQuery, MarketNotesTopK, null, cancellationToken)
            .ConfigureAwait(false);

        if (notesResult.IsSuccess)
        {
            items.AddRange(notesResult.Value.Select(ToMarketPackItem));
        }

        return items;
    }

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
    /// (<see cref="BuildClausePackAsync"/>, the epic-28 scoped RAG) and is what <see cref="BuildInDomainReplyAsync"/>
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

        var renewal = InsightsEndpointExtensions.ComputeRenewal(contract360.Header, renewalEngine);
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
        var (benchmarkSupplierName, geography) = await ResolveBenchmarkKeyAsync(contract360.Header.SupplierId, cancellationToken)
            .ConfigureAwait(false);

        var pricedLines = await InsightsEndpointExtensions
            .ToPricedLines(contract360, benchmarkService, benchmarkSupplierName, geography, asOfDate, cancellationToken)
            .ConfigureAwait(false);

        var criticalFacts = InsightsEndpointExtensions.ToCriticalFacts(contract360);
        var supplierName = await ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false);

        // Composed by the same mapping GET /api/contracts/{id}/strategy uses
        // (InsightsEndpointExtensions.ToStrategyInputs / StrategyPackBuilder.Build), so Ask and the
        // endpoint narrate the identical targets whenever a band exists (AC-2) and the identical
        // "insufficient market data" explanation when it does not (AC-3;
        // PricedLineNegotiationCalculator.ComputeTargetRange). Ask overrides SupplierName with its
        // own always-available ResolveDisplayNameAsync result (falls back to the contract type,
        // never null, unlike /strategy's own name -- which is only as good as this same
        // BenchmarkKeyResolution call) purely for narration text ("Notify X of intent..." etc.); the
        // override never touches a priced line's own Benchmark/band, so it cannot desync the numbers
        // this task fixes.
        var strategyInputs = InsightsEndpointExtensions
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
    /// contract's own clause evidence (<see cref="BuildClausePackAsync"/>, epic-28 scoped RAG),
    /// with <c>persistTodos: true</c> so the ranked set is upserted before
    /// <see cref="AnswerComposer.AnswerAsync"/>. Tenant evidence is appended, not prepended: the
    /// calc corpus's own "when you must move" item must stay first (AC-3).
    /// </summary>
    internal async Task<IReadOnlyList<PackItem>> BuildRenewalStrategyWithEvidenceAsync(
        TenantId tenantId,
        string question,
        PortfolioListItem namedContractItem,
        string actor,
        CancellationToken cancellationToken,
        SavingsGoal? goal = null)
    {
        var strategyItems = (await BuildRenewalStrategyPackAsync(
                namedContractItem, cancellationToken, persistTodos: true, actor)
            .ConfigureAwait(false)).ToList();

        // The money behind the strategy: the target verdict, the grounded levers, the supplier's
        // market deals and the playbook entries for those levers (AskCopilotService.Savings.cs).
        strategyItems.AddRange(await BuildLeverAddendumAsync(namedContractItem, goal, cancellationToken).ConfigureAwait(false));

        // Tenant clause evidence (AC-2). SearchByContractAsync uses CosineDistance, which
        // InMemory EF cannot translate — the same constraint BuildNoticePackAsync documents
        // and therefore never calls embeddings. A translation miss is empty tenant corpus,
        // never a failed Q3 turn: calc/market/raffa + persist already completed above.
        IReadOnlyList<PackItem> clauseItems;
        try
        {
            clauseItems = await BuildClausePackAsync(tenantId, question, namedContractItem, cancellationToken)
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

        var renewal = InsightsEndpointExtensions.ComputeRenewal(contract360.Header, renewalEngine);
        var asOfDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        // One resolution per screen (ADR-024 w17 clause 7), same as BuildRenewalStrategyPackAsync/
        // BuildMarketComparePackAsync (task E28/F01/US01/T01, NW-82) -- one (supplier name,
        // geography) key, one async ToPricedLines call, so this pack's above-band-price point can
        // never disagree with /strategy or the market-compare pack about the same line's band.
        var (benchmarkSupplierName, geography) = await ResolveBenchmarkKeyAsync(contract360.Header.SupplierId, cancellationToken)
            .ConfigureAwait(false);
        var pricedLines = await InsightsEndpointExtensions
            .ToPricedLines(contract360, benchmarkService, benchmarkSupplierName, geography, asOfDate, cancellationToken)
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
                    InsightsEndpointExtensions.ToCriticalityRiskSeverity(r.Severity) ?? CriticalityRiskSeverity.None))
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

        var supplierName = await ResolveDisplayNameAsync(contract360.Header.SupplierId, contract360.Header.Type, cancellationToken)
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

        var portfolioAnnualSpendByCurrency = InsightsEndpointExtensions.ComputePortfolioAnnualSpendByCurrency(contracts);

        var inputs = contracts
            .Select(contract => InsightsEndpointExtensions.ToCriticalityInputs(
                contract,
                InsightsEndpointExtensions.ComputePriority(contract.Header, renewalEngine, priorityScoreCalculator),
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

    private static IReadOnlyList<PackItem> BuildDocumentStatusPack(
        PortfolioPage portfolio, IReadOnlyDictionary<EntityId, string> supplierNames)
    {
        var pending = portfolio.Items
            .Where(item => !string.Equals(item.Status, "completed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (pending.Count == 0)
        {
            return
            [
                new PackItem(
                    InsightsCitationKeys.Calc("document-status"), PackCorpus.Calc, "Document status",
                    null, null, null, "Every validated document is askable.", null, null, null,
                    "deterministic calculator", []),
            ];
        }

        // Named by supplier (R-SUP-04) and linked to the review queue, so the reply can say whose
        // document is still pending and route straight to it -- never "Msa — not yet askable".
        var reviewHref = CapabilityCatalog.Find(CapabilityCatalog.DocumentsAttentionKey)?.RoutePattern;

        return pending.Select((item, index) => new PackItem(
            InsightsCitationKeys.Calc($"document-status[{index}]"),
            PackCorpus.Calc,
            $"{ContractTitle(item, DisplayNameFor(item, supplierNames))} — not yet askable",
            null, null, null,
            $"{ContractTitle(item, DisplayNameFor(item, supplierNames))} is not validated yet (status: {item.Status}).",
            reviewHref, null, null,
            "deterministic calculator", [],
            item.ContractId.ToString())).ToList();
    }

    // ----- Shared helpers -----

    /// <summary>
    /// The name a contract is shown under everywhere in a pack (R-SUP-04): the resolved supplier
    /// name from the per-turn map <see cref="AskAsync"/> builds, else the contract type -- never a
    /// guid. Synchronous twin of <see cref="ResolveDisplayNameAsync(PortfolioListItem, CancellationToken)"/>
    /// for the multi-contract paths that already hold the whole map.
    /// </summary>
    private static string DisplayNameFor(PortfolioListItem item, IReadOnlyDictionary<EntityId, string> supplierNames) =>
        item.SupplierId is { } supplierId && supplierNames.TryGetValue(new EntityId(supplierId), out var name)
            ? name
            : item.Type.ToString();

    /// <summary>
    /// "Salesforce · MSA" -- the one title shape every contract-level pack item uses, so a reply
    /// can always say which supplier it means. When no supplier name resolved (the display name fell
    /// back to the type), the title says so instead of the misleading "MSA · MSA".
    /// </summary>
    private static string ContractTitle(PortfolioListItem item, string displayName) =>
        string.Equals(displayName, item.Type.ToString(), StringComparison.Ordinal)
            ? $"Unnamed supplier · {item.Type}"
            : $"{displayName} · {item.Type}";

    /// <summary>
    /// Task E25/F02/US01/T01 (NW-55): still the pre-existing <c>/contracts/{id}</c> CTA and a
    /// <see langword="null"/> <see cref="PackItem.PreviewUrl"/> -- deliberately untouched by this
    /// task. A contract-level fact (e.g. "ends on 2027-01-01") has no single source page: it is
    /// computed from <see cref="PortfolioListItem"/> columns, which name no
    /// <c>SourceDocumentId</c>/<c>SourcePage</c> at all, so there is nothing here for
    /// <see cref="ResolveTenantClauseLinks"/>'s viewer link to resolve against. AC-3 ("PreviewUrl is
    /// set only for tenant <em>pages</em>") already reads this item as correctly page-less, not as a
    /// gap: a contract route is still a valid <see cref="PackItem.Href"/> shape for
    /// <see cref="PackCorpus.Tenant"/> per that field's own doc comment ("a document/contract route
    /// for PackCorpus.Tenant").
    /// </summary>
    private PackItem BuildContractFactItem(PortfolioListItem item, string displayName)
    {
        var values = new List<PackValue>();
        if (item.EndDate is { } endDate)
        {
            values.Add(new PackValue("endDate", endDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PackValueKind.Date));
        }

        if (item.CancellationDeadline is { } deadline)
        {
            values.Add(new PackValue("cancellationDeadline", deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PackValueKind.Date));
        }

        // The real contract currency, never "n/a": NumericGuard matches an amount only against a
        // pack value of the same currency, so "n/a" forced the model into bare "667000.00" figures
        // (closes the golden set's GAP-ASK-SPEND-CURRENCY-NA).
        var currency = string.IsNullOrWhiteSpace(item.Currency) ? "n/a" : item.Currency.Trim();
        if (item.AnnualSpend is { } spend)
        {
            values.Add(new PackValue("annualSpend", spend.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, currency));
        }

        var title = ContractTitle(item, displayName);
        var spendSentence = item.AnnualSpend is { } annualSpend && currency != "n/a"
            ? $" Annual spend {currency} {annualSpend.ToString("0.##", CultureInfo.InvariantCulture)}."
            : string.Empty;

        var snippet = item.EndDate is { } end
            ? $"{title} ends on {end:yyyy-MM-dd}" +
              (item.AutoRenewal ? ", auto-renews unless notice is given" : ", does not auto-renew") +
              (item.CancellationDeadline is { } cd ? $" (notice by {cd:yyyy-MM-dd})" : string.Empty) + "." + spendSentence
            : $"{title} has no validated end date yet.{spendSentence}";

        return new PackItem(
            $"fact:{item.ContractId}:renewal",
            PackCorpus.Tenant,
            title,
            null,
            null,
            null,
            snippet,
            $"/contracts/{item.ContractId}",
            null,
            null,
            "validated contract",
            values,
            item.ContractId.ToString());
    }

    private static IReadOnlyList<PackValue> BuildDateValues(DateOnly? renewalDate, DateOnly? cancellationDeadline)
    {
        var values = new List<PackValue>();
        if (renewalDate is { } renewal)
        {
            values.Add(new PackValue("renewalDate", renewal.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PackValueKind.Date));
        }

        if (cancellationDeadline is { } deadline)
        {
            values.Add(new PackValue("cancellationDeadline", deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PackValueKind.Date));
        }

        return values;
    }

    private static PackItem ToMarketPackItem(MarketNote note) =>
        new(
            $"market:{note.RecordId}",
            PackCorpus.Market,
            note.Title,
            note.Provenance,
            null,
            null,
            note.Snippet,
            null,
            null,
            note.RecordId,
            note.Provenance,
            []);

    /// <summary>
    /// One <see cref="PackCorpus.Market"/> item for a benchmarked priced line -- a representative
    /// band (P25/P50/P75 + provenance) when one resolved, else an honest "insufficient market data"
    /// entry, never a silently dropped line (AC-3, task E28/F01/US01/T01, NW-82). Shared by
    /// <see cref="BuildMarketComparePackAsync"/> and <see cref="BuildRenewalStrategyPackAsync"/>
    /// (task E31/F01/US01/T01, NW-95) so the two packs can never narrate two different wordings for
    /// the identical line's missing band.
    /// </summary>
    private static PackItem BuildMarketPricedLineItem(string supplierName, PricedLine line)
    {
        var lineKey = $"market:{supplierName}:{line.Description}".Replace(' ', '-');

        if (line.Benchmark is not { } distribution)
        {
            return new PackItem(
                lineKey,
                PackCorpus.Market,
                $"{supplierName} · {line.Description}",
                null,
                null,
                null,
                "Insufficient market data for this line — too few comparables to publish a " +
                "benchmark (Appendix C rule 10; ADR-001).",
                null,
                null,
                null,
                "insufficient market data",
                []);
        }

        return new PackItem(
            lineKey,
            PackCorpus.Market,
            $"{supplierName} · {line.Description}",
            line.AdapterName,
            null,
            null,
            $"P25 {distribution.P25} · P50 {distribution.P50} · P75 {distribution.P75} " +
            $"{line.Currency}/unit · n = {line.SampleSize?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}",
            null,
            null,
            null,
            FormatRepresentativeProvenance(line),
            [
                new PackValue("p25", distribution.P25.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, line.Currency),
                new PackValue("p50", distribution.P50.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, line.Currency),
                new PackValue("p75", distribution.P75.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, line.Currency),
            ]);
    }

    /// <summary>
    /// One <see cref="PackCorpus.Raffa"/> citation item for a capability (AC-2 "one raffa Renewals
    /// item", task E31/F01/US01/T01, NW-95) -- the same <see cref="FeatureCitation"/> shape
    /// <see cref="BuildFeatureCitations"/> already cites directly into a deterministic reply
    /// (<see cref="BuildCapabilityReply"/>), but as a genuine <see cref="PackItem"/> here so the
    /// `answer` role can cite it like any other pack source instead of it being injected out of
    /// band. <see langword="null"/> for an unrecognized key -- never fabricated (Appendix C rule
    /// 10) -- though every key this file passes is one of <see cref="CapabilityCatalog"/>'s own
    /// constants, so this is not expected in practice.
    /// </summary>
    private static PackItem? BuildFeatureCitationPackItem(string capabilityKey)
    {
        var capability = CapabilityCatalog.Find(capabilityKey);
        if (capability is null)
        {
            return null;
        }

        var feature = FeatureCitation.For(capability);

        return new PackItem(
            $"raffa:{capabilityKey}",
            PackCorpus.Raffa,
            feature.Title,
            feature.Subtitle,
            null,
            null,
            feature.Snippet,
            feature.Href,
            null,
            null,
            "Raffa feature",
            []);
    }

    private static IReadOnlyList<ReplyCitation> BuildFeatureCitations(IReadOnlyList<string> keys)
    {
        var citations = new List<ReplyCitation>();
        var n = 1;

        foreach (var key in keys)
        {
            var capability = CapabilityCatalog.Find(key);
            if (capability is null)
            {
                continue;
            }

            var feature = FeatureCitation.For(capability);
            citations.Add(new ReplyCitation(
                n++, feature.Corpus, feature.Title, feature.Subtitle, feature.Snippet,
                null, null, null, null, null, feature.Href, null));
        }

        return citations;
    }

    private static (string? Sentence, EntityId? ContractId) BuildPortfolioHook(
        PortfolioPage portfolio, IReadOnlyDictionary<EntityId, string> supplierNames)
    {
        if (portfolio.Items.Count == 0)
        {
            return (null, null);
        }

        var soonest = portfolio.Items
            .Where(item => item.CancellationDeadline is not null)
            .OrderBy(item => item.CancellationDeadline)
            .FirstOrDefault()
            ?? portfolio.Items.OrderBy(item => item.EndDate ?? DateOnly.MaxValue).First();

        var name = soonest.SupplierId is { } supplierId && supplierNames.TryGetValue(new EntityId(supplierId), out var resolved)
            ? resolved
            : soonest.Type.ToString();

        var sentence = soonest.EndDate is { } end
            ? $"your {name} contract runs until {end:yyyy-MM-dd}"
            : $"your {name} contract is on file and ready to ask about";

        return (sentence, new EntityId(soonest.ContractId));
    }

    private static EntityId? FindContractIdBySupplier(
        PortfolioPage portfolio, IReadOnlyDictionary<EntityId, string> supplierNames, string namedSupplier)
    {
        var match = portfolio.Items.FirstOrDefault(item =>
            item.SupplierId is { } supplierId &&
            supplierNames.TryGetValue(new EntityId(supplierId), out var name) &&
            string.Equals(name, namedSupplier, StringComparison.OrdinalIgnoreCase));

        return match is null ? null : new EntityId(match.ContractId);
    }

    /// <summary>
    /// Resolves the (supplier name, geography) key <see cref="BuildRenewalStrategyPackAsync"/>/
    /// <see cref="BuildMarketComparePackAsync"/> need for the async
    /// <c>InsightsEndpointExtensions.ToPricedLines</c> overload — the same
    /// <see cref="BenchmarkKeyResolution"/> <c>GET /api/contracts/{id}/strategy</c> already resolves
    /// (ADR-024 w17 clause 7 "one resolution per screen"; task E28/F01/US01/T01, NW-82). An
    /// <see cref="BenchmarkKeyResult.Incomplete"/> key (no <c>SupplierId</c>, an unresolved name, or
    /// no workspace country) maps to <c>(null, null)</c>, so <c>ToPricedLines</c> leaves every
    /// line's band unset rather than querying with a fabricated geography.
    /// </summary>
    private async Task<(string? SupplierName, string? Geography)> ResolveBenchmarkKeyAsync(
        EntityId? supplierId, CancellationToken cancellationToken)
    {
        var key = await benchmarkKeyResolution
            .ResolveAsync(CurrentTenantId, supplierId, cancellationToken)
            .ConfigureAwait(false);

        return key is BenchmarkKeyResult.Complete complete ? (complete.Supplier, complete.Geography) : (null, null);
    }

    /// <summary>
    /// <see cref="BuildMarketComparePackAsync"/>'s own market-corpus provenance label for a line
    /// whose band resolved (task text, verbatim: "a provenance label (representative · adapter · n
    /// · as-of)") — distinct from, and simpler than,
    /// <c>Raffa.Insights.Strategy.StrategyPackBuilder.AnnotateTargetExplanation</c>'s own
    /// "representative (source: X; n=Y; as of Z)" phrase folded into a target's free-text
    /// explanation (that file is out of this task's "Files to create or modify" scope, so its own
    /// wording is left exactly as it already is); both name the same three facts — adapter, sample
    /// size, as-of date — because ADR-001 w17 clause 4 requires a representative claim to carry
    /// them, never a bare percentile.
    /// </summary>
    private static string FormatRepresentativeProvenance(PricedLine line) =>
        $"representative · {line.AdapterName} · n={line.SampleSize?.ToString(CultureInfo.InvariantCulture) ?? "n/a"} " +
        $"· as of {(line.AsOf is { } asOf ? asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "n/a")}";

    private Task<string> ResolveDisplayNameAsync(PortfolioListItem item, CancellationToken cancellationToken) =>
        ResolveDisplayNameAsync(item.SupplierId is { } supplierId ? new EntityId(supplierId) : null, item.Type, cancellationToken);

    /// <summary>
    /// Shared core of <see cref="ResolveDisplayNameAsync(PortfolioListItem, CancellationToken)"/> —
    /// task E31/F02/US01/T01's own <see cref="BuildNegotiationPointsPackAsync"/> resolves a display
    /// name from a bare <see cref="Contract360Header.SupplierId"/> rather than a full
    /// <see cref="PortfolioListItem"/> (it starts from a contract id, not a portfolio row), so this
    /// is the one place both paths fall back to the contract type when no supplier name resolves
    /// (Appendix C rule 10: never fabricate a name that was not resolved).
    /// </summary>
    private async Task<string> ResolveDisplayNameAsync(
        EntityId? supplierId, ContractDocumentType type, CancellationToken cancellationToken)
    {
        if (supplierId is not { } id)
        {
            return type.ToString();
        }

        var names = await supplierNameLookup
            .GetNamesAsync(CurrentTenantId, [id], cancellationToken)
            .ConfigureAwait(false);

        return names.TryGetValue(id, out var name) ? name : type.ToString();
    }

    private static ContractFact ToContractFact(PortfolioListItem item) =>
        new(
            new EntityId(item.ContractId),
            item.SupplierId is { } supplierId ? new EntityId(supplierId) : null,
            item.AnnualSpend,
            item.EndDate,
            item.AutoRenewal);

    private async Task WriteAuditAsync(
        TenantId tenantId, CopilotReply reply, bool guardIntervened, string actor, CancellationToken cancellationToken)
    {
        var action = reply.Kind switch
        {
            ReplyKind.Answer => AuditAnsweredAction,
            ReplyKind.Abstain => AuditAbstainedAction,
            ReplyKind.Redirect => AuditRedirectedAction,
            ReplyKind.Refusal => AuditRefusedAction,
            _ => AuditAnsweredAction,
        };

        var packHash = ComputeHash(string.Join('|', reply.Citations.Select(c => c.N + ":" + c.Corpus)));

        // AC-7 / R-ASK-09: "audit records abstainGuardIntervened=true" when Guards.RegenerateOnce's
        // retry-then-downgrade path fired — same field name Application.RagAnswerService's own
        // (older, evidence-only) audit entry already uses, see that type's own WriteAsync call, so
        // an operator/query filters on one consistent key regardless of which Ask path produced the
        // row.
        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId,
                actor,
                action,
                AuditResourceType,
                packHash,
                clock.UtcNow,
                $"kind={reply.Kind} citationCount={reply.Citations.Count} actionCount={reply.Actions.Count} " +
                $"packHash={packHash} abstainGuardIntervened={guardIntervened}"),
            cancellationToken).ConfigureAwait(false);
    }

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

    private static string ComputeHash(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}
