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
/// The shared negotiation-points pack builder (own class so the Savings and RenewalStrategy builders do not form a cycle). Moved verbatim from
/// <see cref="Raffa.AiFlows.Ask.Orchestration.AskCopilotService"/> (F1 Ask split, PR-3).
/// </summary>
public sealed class NegotiationPointsPackBuilder(
    ContractContextReader context,
    RenewalEngine renewalEngine,
    RenewalNegotiationTodoService renewalNegotiationTodoService,
    ITenantContext tenantContext,
    IClock clock)
{
    /// <summary>The tenant scope <c>AskAsync</c> already opened for this call
    /// (<see cref="ITenantContext.BeginScope"/>). Falls back to <see langword="default"/> only
    /// outside that scope (fail-closed: <see langword="default"/> resolves to no rows under RLS).</summary>
    private TenantId CurrentTenantId => tenantContext.Current ?? default;

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
    /// section (<c>RenewalStrategyPackBuilder.BuildRenewalStrategyPackAsync</c>) passes <see langword="false"/> so the
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

        var contract360 = await context.GetContract360Async(contractId.Value, cancellationToken)
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
}
