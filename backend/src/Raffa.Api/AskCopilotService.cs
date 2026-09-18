using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Chat.Application;
using Raffa.Chat.Application.Answering;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Gate;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Planning;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain;
using Raffa.Documents.Contracts.Application;
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
/// </summary>
internal sealed class AskCopilotService(
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
    CriticalityScoreCalculator criticalityScoreCalculator,
    SavingsOpportunityService savingsOpportunityService,
    ISupplierNameLookup supplierNameLookup,
    IBenchmarkService benchmarkService,
    BenchmarkKeyResolution benchmarkKeyResolution,
    IMarketKnowledgeRetrieval marketKnowledgeRetrieval,
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
                scopeContractId, scopedContractItem, cancellationToken)
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
    private async Task<(CopilotReply Reply, bool GuardIntervened)> BuildInDomainReplyAsync(
        TenantId tenantId,
        string question,
        string? namedSupplier,
        PortfolioPage portfolio,
        IReadOnlyDictionary<EntityId, string> supplierNames,
        IReadOnlyList<(string Role, string Markdown)> recentTurns,
        EntityId? scopeContractId,
        PortfolioListItem? scopedContractItem,
        CancellationToken cancellationToken)
    {
        var plan = intentPlanner.Plan(question, namedSupplier);

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

        var packItems = plan.Intent switch
        {
            AskIntent.StructuredFact => await BuildStructuredFactPackAsync(question, namedContractItem, portfolio, cancellationToken)
                .ConfigureAwait(false),
            AskIntent.Clause => await BuildClausePackAsync(tenantId, question, namedContractItem, cancellationToken)
                .ConfigureAwait(false),
            AskIntent.MarketCompare => await BuildMarketComparePackAsync(namedContractItem, cancellationToken).ConfigureAwait(false),
            AskIntent.RenewalStrategy => namedContractItem is not null
                ? await BuildRenewalStrategyPackAsync(namedContractItem, cancellationToken).ConfigureAwait(false)
                : await BuildPortfolioStrategyPackAsync(portfolio, supplierNames, cancellationToken).ConfigureAwait(false),
            AskIntent.PortfolioStrategy => await BuildPortfolioStrategyPackAsync(portfolio, supplierNames, cancellationToken).ConfigureAwait(false),
            AskIntent.Savings => namedContractItem is not null
                ? await BuildSavingsPackAsync(tenantId, namedContractItem, cancellationToken).ConfigureAwait(false)
                : await BuildPortfolioStrategyPackAsync(portfolio, supplierNames, cancellationToken).ConfigureAwait(false),
            AskIntent.DocumentStatus => BuildDocumentStatusPack(portfolio),
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

    private async Task<IReadOnlyList<PackItem>> BuildStructuredFactPackAsync(
        string question, PortfolioListItem? namedContractItem, PortfolioPage portfolio, CancellationToken cancellationToken)
    {
        if (namedContractItem is not null)
        {
            return [BuildContractFactItem(namedContractItem, await ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false))];
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

                    items.Add(BuildContractFactItem(item, item.Type.ToString()));
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
            .Select(item => BuildContractFactItem(item, item.Type.ToString()))
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
            var tenantWideResult = await embeddingRetrievalService
                .SearchAsync(tenantId, question, ClauseTopK, cancellationToken)
                .ConfigureAwait(false);

            return tenantWideResult.IsFailure
                ? []
                : tenantWideResult.Value
                    .Select(hit => BuildClausePackItem(hit, clause: null, namedContractId: null, isPeer: false))
                    .ToList();
        }

        var contractId = new EntityId(namedContractItem.ContractId);

        var searchResult = await embeddingRetrievalService
            .SearchByContractAsync(
                new EmbeddingSearchQuery(tenantId, question, ClauseTopK, contractId, ClausePeerTopK),
                cancellationToken)
            .ConfigureAwait(false);

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
    /// </summary>
    private static PackItem BuildClausePackItem(
        EmbeddingSearchResult hit, Contract360Clause? clause, Guid? namedContractId, bool isPeer)
    {
        var (href, previewUrl) = ResolveTenantClauseLinks(clause, hit.SourceId, namedContractId);

        var baseTitle = clause is not null ? $"{clause.ClauseType} clause" : $"{hit.SourceType} excerpt";
        var subtitle = clause?.SourcePage is { } page
            ? $"p.{page}" + (clause.SourceSpan is { } span ? $" §{span}" : string.Empty)
            : null;

        return new PackItem(
            $"fact:{hit.SourceId}:chunk[{hit.ChunkIndex}]",
            PackCorpus.Tenant,
            isPeer ? $"Similar contract — {baseTitle}" : baseTitle,
            isPeer ? "similar contract, not this one" : subtitle,
            clause?.SourcePage,
            clause?.SourceSpan ?? $"chunk {hit.ChunkIndex}",
            hit.ChunkText,
            href,
            previewUrl,
            null,
            isPeer ? "similar validated contract" : "validated contract",
            []);
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
    /// does this return the viewer pair; otherwise it falls back to the pre-existing contract route
    /// (or <see langword="null"/> with no named contract) -- never a dead viewer link.
    ///
    /// <para>
    /// Honest gap (R-EVD-01 "citations resolve to Clause.SourcePage/SourceSpan when the hit is a
    /// clause, else to the page"): when the embedded chunk's own source is the whole Document rather
    /// than one extracted <c>Clause</c> row (<c>Embedding.SourceType == "Document"</c>),
    /// <paramref name="clause"/> never resolves and this falls back to the contract route even
    /// though <paramref name="sourceId"/>/the hit's own page could, in principle, still resolve a
    /// document-level viewer link. This task's own coding objective and Definition of Done line
    /// ("a tenant clause pack item carries a viewer href + real previewUrl") scope the fix to the
    /// clause-resolved case only; the Document-sourced-chunk branch is not attempted here.
    /// </para>
    /// </summary>
    /// <param name="sourceId">The cited chunk's own source id (<see cref="EmbeddingSearchResult.SourceId"/>)
    /// -- the clause id when <paramref name="clause"/> resolved it -- echoed into the viewer's
    /// optional <c>?clause=</c> query parameter (ADR-018).</param>
    /// <param name="namedContractId">The named contract's id, when the caller asked about one
    /// contract by name -- the pre-existing fallback CTA target.</param>
    internal static (string? Href, string? PreviewUrl) ResolveTenantClauseLinks(
        Contract360Clause? clause, EntityId sourceId, Guid? namedContractId)
    {
        if (clause is { SourceDocumentId: { } sourceDocumentId, SourcePage: { } sourcePage })
        {
            return (
                $"/documents/{sourceDocumentId.Value}/viewer?page={sourcePage}&clause={sourceId.Value}",
                $"/api/documents/{sourceDocumentId.Value}/preview?page={sourcePage}");
        }

        return (namedContractId is { } contractId ? $"/contracts/{contractId}" : null, null);
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

            var lineKey = $"market:{supplierName}:{line.Description}".Replace(' ', '-');

            if (line.Benchmark is { } distribution)
            {
                items.Add(new PackItem(
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
                    ]));
            }
            else
            {
                // AC-3: a missing band narrates "insufficient market data", never a fabricated
                // percentile. Previously this line was silently dropped (no PackItem at all, the
                // adapter-failure/insufficient-data branches both just `continue`d) -- leaving the
                // model nothing to cite when it had to say so, and no honest trace an operator could
                // tell apart from "this intent never ran".
                items.Add(new PackItem(
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
                    []));
            }

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
                [new PackValue("unitPrice", unitPrice.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, contract360.Overview.Currency)]));
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
    /// only ever echoes a pack's first five items, and this pack's own fixed order
    /// (when-you-must-move, then up to seven levers per priced line, then one target per line)
    /// never puts a target that early, so an HTTP-round-trip reply could not observe it without
    /// first asserting on that unrelated cap.
    /// </summary>
    internal async Task<IReadOnlyList<PackItem>> BuildRenewalStrategyPackAsync(
        PortfolioListItem namedContractItem, CancellationToken cancellationToken)
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
                BuildDateValues(pack.WhenYouMustMove.RenewalDate, pack.WhenYouMustMove.CancellationDeadline)),
        };

        var leverIndex = 0;
        foreach (var lever in pack.WhereYouCanPush)
        {
            items.Add(new PackItem(
                InsightsCitationKeys.Calc($"lever[{leverIndex++}]"),
                PackCorpus.Calc,
                $"{supplierName} — {lever.LeverType} lever",
                null, null, null,
                lever.Rationale,
                $"/contracts/{namedContractItem.ContractId}", null, null,
                "deterministic calculator", []));
        }

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
                "deterministic calculator", values));
        }

        items.Add(new PackItem(
            InsightsCitationKeys.Calc("next-steps"),
            PackCorpus.Calc,
            $"{supplierName} — next steps",
            null, null, null,
            string.Join("; ", pack.NextSteps.Select(step => $"{step.Label} ({step.DueHint})")),
            $"/contracts/{namedContractItem.ContractId}", null, null,
            "deterministic calculator", []));

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
                [new PackValue("totalScore", score.TotalScore.ToString(CultureInfo.InvariantCulture), PackValueKind.Number)]));
        }

        return items;
    }

    private async Task<IReadOnlyList<PackItem>> BuildSavingsPackAsync(
        TenantId tenantId, PortfolioListItem namedContractItem, CancellationToken cancellationToken)
    {
        var supplierName = await ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false);
        var allSavings = await savingsOpportunityService.ListAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var contractSavings = allSavings.Where(o => o.ContractId?.Value == namedContractItem.ContractId).ToList();

        if (contractSavings.Count == 0)
        {
            return [BuildContractFactItem(namedContractItem, supplierName)];
        }

        return contractSavings.Select((opportunity, index) => new PackItem(
            $"fact:{namedContractItem.ContractId}:saving[{index}]",
            PackCorpus.Tenant,
            $"{supplierName} — {opportunity.Type}",
            opportunity.ConfidenceLevel.ToString(),
            null, null,
            $"Estimated saving {opportunity.EstimatedSavingsLow}–{opportunity.EstimatedSavingsHigh} {opportunity.Currency}.",
            $"/contracts/{namedContractItem.ContractId}", null, null,
            "validated contract",
            [
                new PackValue("estimatedSavingsLow", opportunity.EstimatedSavingsLow.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, opportunity.Currency),
                new PackValue("estimatedSavingsHigh", opportunity.EstimatedSavingsHigh.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, opportunity.Currency),
            ])).ToList();
    }

    private static IReadOnlyList<PackItem> BuildDocumentStatusPack(PortfolioPage portfolio)
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

        return pending.Select((item, index) => new PackItem(
            InsightsCitationKeys.Calc($"document-status[{index}]"),
            PackCorpus.Calc,
            $"{item.Type} — not yet askable",
            null, null, null,
            $"Status: {item.Status}.",
            null, null, null,
            "deterministic calculator", [])).ToList();
    }

    // ----- Shared helpers -----

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

        if (item.AnnualSpend is { } spend)
        {
            values.Add(new PackValue("annualSpend", spend.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, "n/a"));
        }

        var snippet = item.EndDate is { } end
            ? $"{displayName} ends on {end:yyyy-MM-dd}" +
              (item.AutoRenewal ? ", auto-renews unless notice is given" : ", does not auto-renew") +
              (item.CancellationDeadline is { } cd ? $" (notice by {cd:yyyy-MM-dd})" : string.Empty) + "."
            : $"{displayName} has no validated end date yet.";

        return new PackItem(
            $"fact:{item.ContractId}:renewal",
            PackCorpus.Tenant,
            $"{displayName} · {item.Type}",
            null,
            null,
            null,
            snippet,
            $"/contracts/{item.ContractId}",
            null,
            null,
            "validated contract",
            values);
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

    private async Task<string> ResolveDisplayNameAsync(PortfolioListItem item, CancellationToken cancellationToken)
    {
        if (item.SupplierId is not { } supplierId)
        {
            return item.Type.ToString();
        }

        var names = await supplierNameLookup
            .GetNamesAsync(CurrentTenantId, [new EntityId(supplierId)], cancellationToken)
            .ConfigureAwait(false);

        return names.TryGetValue(new EntityId(supplierId), out var name) ? name : item.Type.ToString();
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

    private static string ComputeHash(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input)));
}
