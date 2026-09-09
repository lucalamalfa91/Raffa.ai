using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Contigo.Benchmark;
using Contigo.Benchmark.Contracts;
using Contigo.Chat.Application;
using Contigo.Chat.Application.Answering;
using Contigo.Chat.Application.Capabilities;
using Contigo.Chat.Application.Gate;
using Contigo.Chat.Application.Pack;
using Contigo.Chat.Application.Planning;
using Contigo.Chat.Application.Reply;
using Contigo.Chat.Domain;
using Contigo.Documents.Contracts.Application;
using Contigo.Insights.Contracts;
using Contigo.Insights.Criticality;
using Contigo.Insights.Strategy;
using Contigo.Market.Contracts;
using Contigo.Market.Retrieval;
using Contigo.Renewals.Application;
using Contigo.Savings.Application;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Suppliers;
using Contigo.SharedKernel.Tenancy;

namespace Contigo.Api;

/// <summary>
/// The Ask V2 pack-composition root (task E13/F06/US01/T01, ask-engine; ADR-024 "context pack
/// assembled in Contigo.Api from the three sources"; parent story us-01-ask-engine). Everything
/// <c>Contigo.Chat</c>'s allow-list (<c>[SharedKernel, AiGateway]</c>) forbids that module from
/// doing itself happens here: authorization-scoped tenant facts
/// (<see cref="PortfolioQueryService"/>/<see cref="Contract360QueryService"/>), clause chunks
/// (<see cref="EmbeddingRetrievalService"/>), renewal/priority calculators
/// (<see cref="RenewalEngine"/>/<see cref="PriorityScoreCalculator"/>), market data
/// (<see cref="IBenchmarkService"/>/<see cref="IMarketKnowledgeRetrieval"/>), portfolio
/// calculators (<see cref="CriticalityScoreCalculator"/>/<see cref="StrategyPackBuilder"/>),
/// savings opportunities (<see cref="SavingsOpportunityService"/>) and supplier names
/// (<see cref="ISupplierNameLookup"/>) are all composed into one
/// <see cref="Contigo.Chat.Application.Pack.PackItem"/> list, then handed to
/// <c>Contigo.Chat</c>'s own gate/planner/guards/answer pipeline. <see cref="ChatEndpointExtensions"/>
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
/// <b>Known gaps, honestly scoped</b>: <c>Contigo.Documents.Contracts.Domain.Contract</c> has no
/// geography/product-category field, so a <see cref="BenchmarkQuery"/> built here uses
/// <c>GoverningLaw</c> as an imperfect geography proxy (documented on
/// <see cref="BuildMarketComparePackAsync"/>) — the same honest gap
/// <c>Contigo.Api.InsightsEndpointExtensions</c>'s own doc comment already names for
/// <c>/api/contracts/{id}/strategy</c>. R-STR-03 (status-aware renewal actions) is not read here —
/// a follow-up, not attempted by this task.
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
    IMarketKnowledgeRetrieval marketKnowledgeRetrieval,
    IAuditWriter auditWriter,
    ITenantContext tenantContext,
    IClock clock)
{
    private const int ClauseTopK = 5;
    private const int MarketNotesTopK = 3;
    private const int MaxPricedLinesForBenchmark = 2;
    private const int PortfolioStrategyTopN = 5;

    // Reused, not re-implemented (task's own "extend / reuse" file list): both are pure/stateless
    // (Appendix C rule 6), so a private, non-DI instance is exactly how Contigo.Chat.Application
    // .Planning.IntentPlanner already reuses AskContigoQueryRouter itself.
    private readonly AskContigoQueryRouter _legacyRouter = new();
    private readonly DeterministicQueryPlanner _legacyPlanner = new();
    private readonly DeterministicQueryHandler _legacyHandler = new(clock);

    private const string AuditAnsweredAction = "chat.answered";
    private const string AuditRedirectedAction = "chat.redirected";
    private const string AuditRefusedAction = "chat.refused";
    private const string AuditAbstainedAction = "chat.abstained";
    private const string AuditResourceType = "ask_contigo_v2";
    private const string UnattributedActor = "unattributed";

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
    /// <exception cref="ArgumentException"><paramref name="question"/> is null/blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="recentTurns"/> is <see langword="null"/>.</exception>
    public async Task<CopilotReply> AskAsync(
        TenantId tenantId,
        string question,
        IReadOnlyList<(string Role, string Markdown)> recentTurns,
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

        var gate = domainGate.Classify(question, supplierNames.Values.ToList());

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
                tenantId, question, gate.NamedSupplier, portfolio, supplierNames, recentTurns, cancellationToken)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(gate), gate.Label, "Unknown GateLabel."),
        };

        await WriteAuditAsync(tenantId, reply, guardIntervened, cancellationToken).ConfigureAwait(false);

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
            "I answer from your validated contracts and route you to the right part of Contigo:\n" +
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
    /// </summary>
    private async Task<(CopilotReply Reply, bool GuardIntervened)> BuildInDomainReplyAsync(
        TenantId tenantId,
        string question,
        string? namedSupplier,
        PortfolioPage portfolio,
        IReadOnlyDictionary<EntityId, string> supplierNames,
        IReadOnlyList<(string Role, string Markdown)> recentTurns,
        CancellationToken cancellationToken)
    {
        var plan = intentPlanner.Plan(question, namedSupplier);

        var namedContractItem = plan.NamedSupplier is null
            ? null
            : portfolio.Items.FirstOrDefault(item =>
                item.SupplierId is { } supplierId &&
                supplierNames.TryGetValue(new EntityId(supplierId), out var name) &&
                string.Equals(name, plan.NamedSupplier, StringComparison.OrdinalIgnoreCase));

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

        var boundedPack = packBudget.Apply(packItems);

        if (boundedPack.Count == 0)
        {
            var emptyActions = portfolio.Items.Count == 0
                ? capabilityRouting.ResolveActions([CapabilityIntent.HowTo(CapabilityCatalog.DocumentsKey)], routingContext)
                : [];

            return (new CopilotReply(
                ReplyKind.Abstain,
                $"Nothing in the {portfolio.Items.Count} validated contract(s) supports a reliable " +
                "answer. Try a question about dates, spend, notice periods or clauses.",
                [], emptyActions, ReplyProvenance.NoModelCall([]), []), false);
        }

        var composed = await answerComposer.AnswerAsync(question, boundedPack, recentTurns, cancellationToken)
            .ConfigureAwait(false);

        if (composed.IsFailure)
        {
            return (new CopilotReply(
                ReplyKind.Abstain,
                "Contigo could not reach the answer service just now — please try again shortly.",
                [], [], ReplyProvenance.NoModelCall([]), []), false);
        }

        var actionKeys = composed.Value.Result.ActionKeys ?? [];
        var resolvedActions = actionKeys.Count > 0
            ? capabilityRouting.ResolveActions(actionKeys.Select(CapabilityIntent.HowTo).ToList(), routingContext)
            : [];

        return (
            CopilotReplyBuilder.FromGuardedResult(composed.Value.Result, boundedPack, resolvedActions),
            composed.Value.GuardIntervened);
    }

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

        return new CopilotReply(ReplyKind.Answer, markdown, [], actions, ReplyProvenance.NoModelCall(["contigo"]), []);
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
    /// <see cref="Contigo.Chat.Application.Reply.CopilotReplyBuilder.BuildCitations"/> (copies a
    /// cited <see cref="PackItem.Title"/>/<see cref="PackItem.Snippet"/> verbatim into
    /// <see cref="Contigo.Chat.Application.Reply.ReplyCitation.Title"/>/<c>.Snippet</c>) and
    /// <c>Contigo.AiGateway.Fixtures.FixtureAiGateway.AnswerFromPack</c>'s own deterministic echo
    /// (<c>"[{n}] {item.Title}: ..."</c>, folded straight into
    /// <see cref="Contigo.Chat.Application.Reply.CopilotReply.AnswerMarkdown"/>) can surface
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
                      "contract(s) with a recorded figure — Contigo could not match the supplier " +
                      "you named, so this total is company-wide, not scoped to it."
                    : $"Total annual spend across {result.MatchedContractIds.Count} validated " +
                      "contract(s) with a recorded figure."),

            // Unreachable in practice — BuildStructuredFactPackAsync only ever calls this helper
            // inside its own "result.Kind != DeterministicQueryKind.Unsupported" branch — but kept
            // exhaustive and just as chrome-free as the two real cases above, never a default that
            // silently reintroduces developer trace text if a future DeterministicQueryKind member
            // ever reaches here uncovered.
            _ => ("Portfolio query result", "Contigo computed this from your validated contracts."),
        };

    private async Task<IReadOnlyList<PackItem>> BuildClausePackAsync(
        TenantId tenantId, string question, PortfolioListItem? namedContractItem, CancellationToken cancellationToken)
    {
        var searchResult = await embeddingRetrievalService
            .SearchAsync(tenantId, question, ClauseTopK, cancellationToken)
            .ConfigureAwait(false);

        if (searchResult.IsFailure)
        {
            return [];
        }

        Contract360Result? contract360 = namedContractItem is null
            ? null
            : await contract360QueryService
                .GetByIdAsync(tenantId, new EntityId(namedContractItem.ContractId), cancellationToken)
                .ConfigureAwait(false);

        var items = new List<PackItem>();
        foreach (var hit in searchResult.Value)
        {
            var clause = contract360?.Clauses.FirstOrDefault(c => c.ClauseId == hit.SourceId);

            items.Add(new PackItem(
                $"fact:{hit.SourceId}:chunk[{hit.ChunkIndex}]",
                PackCorpus.Tenant,
                clause is not null ? $"{clause.ClauseType} clause" : $"{hit.SourceType} excerpt",
                clause?.SourcePage is { } page ? $"p.{page}" + (clause.SourceSpan is { } span ? $" §{span}" : string.Empty) : null,
                clause?.SourcePage,
                clause?.SourceSpan ?? $"chunk {hit.ChunkIndex}",
                hit.ChunkText,
                namedContractItem is not null ? $"/contracts/{namedContractItem.ContractId}" : null,
                null,
                null,
                "validated contract",
                []));
        }

        return items;
    }

    private async Task<IReadOnlyList<PackItem>> BuildMarketComparePackAsync(
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
        var pricedLines = InsightsEndpointExtensions.ToPricedLines(contract360);

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

            // Honest gap (see this type's own doc comment): Contract has no geography field yet,
            // so GoverningLaw stands in — a real mismatch here is exactly the "insufficient market
            // data" outcome ADR-001 already treats as a first-class, honest answer, never a bare
            // fabricated number.
            var query = new BenchmarkQuery(
                supplierName,
                line.Description,
                line.Sku,
                contract360.Overview.GoverningLaw ?? "Unknown",
                line.Quantity ?? 1m,
                contract360.Overview.RenewalTermMonths is { } months ? $"{months} months" : "unknown",
                contract360.Overview.Currency,
                contract360.Overview.EffectiveDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime));

            var benchmarkResult = await benchmarkService.GetBenchmarkAsync(query, cancellationToken).ConfigureAwait(false);
            if (benchmarkResult.IsFailure)
            {
                continue;
            }

            var benchmark = benchmarkResult.Value;
            var lineKey = $"market:{supplierName}:{line.Description}".Replace(' ', '-');

            if (benchmark.HasSufficientData)
            {
                var distribution = benchmark.Distribution!;
                items.Add(new PackItem(
                    lineKey,
                    PackCorpus.Market,
                    $"{supplierName} · {line.Description}",
                    benchmark.Source,
                    null,
                    null,
                    $"P25 {distribution.P25} · P50 {distribution.P50} · P75 {distribution.P75} " +
                    $"{benchmark.Currency}/unit · n = {benchmark.SampleSize?.ToString(CultureInfo.InvariantCulture) ?? "n/a"}",
                    null,
                    null,
                    null,
                    benchmark.Source,
                    [
                        new PackValue("p25", distribution.P25.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, benchmark.Currency),
                        new PackValue("p50", distribution.P50.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, benchmark.Currency),
                        new PackValue("p75", distribution.P75.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, benchmark.Currency),
                    ]));
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

    private async Task<IReadOnlyList<PackItem>> BuildRenewalStrategyPackAsync(
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
        var pricedLines = InsightsEndpointExtensions.ToPricedLines(contract360);
        var criticalFacts = InsightsEndpointExtensions.ToCriticalFacts(contract360);
        var asOfDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var supplierName = await ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false);

        var strategyInputs = new StrategyInputs(
            contract360.ContractId, supplierName, renewal.RenewalDate, renewal.CancellationDeadline,
            renewal.DaysUntilRenewal, renewal.DaysUntilCancellationDeadline, contract360.Header.AutoRenewal,
            pricedLines, criticalFacts, asOfDate);

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
        TenantId tenantId, CopilotReply reply, bool guardIntervened, CancellationToken cancellationToken)
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
                UnattributedActor,
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
