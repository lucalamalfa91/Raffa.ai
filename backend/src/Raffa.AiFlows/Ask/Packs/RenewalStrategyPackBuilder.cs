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
using Raffa.AiFlows.Ask.Packs;
using Raffa.AiFlows.Ask.Orchestration;

namespace Raffa.AiFlows.Ask.Packs;

/// <summary>
/// The RenewalStrategy intent's packs (strategy, and strategy with lever and clause evidence). Moved verbatim from
/// <see cref="Raffa.AiFlows.Ask.Orchestration.AskCopilotService"/> (F1 Ask split, PR-3).
/// </summary>
public sealed class RenewalStrategyPackBuilder(
    ContractContextReader context,
    RenewalEngine renewalEngine,
    NegotiationPointsPackBuilder negotiationPoints,
    SavingsPackBuilder savings,
    ClausePackBuilder clauses,
    IClock clock)
{
    private const int MaxPricedLinesForBenchmark = MarketComparePackBuilder.MaxPricedLinesForBenchmark;

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
        var contract360 = await context.GetContract360Async(namedContractItem.ContractId, cancellationToken)
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
        var rankedPoints = await negotiationPoints.BuildNegotiationPointsPackAsync(
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
        strategyItems.AddRange(await savings.BuildLeverAddendumAsync(namedContractItem, goal, cancellationToken).ConfigureAwait(false));

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
}
