using System.Globalization;
using Raffa.Benchmark.Contracts;
using Raffa.Chat.Application.Capabilities;
using Raffa.Chat.Application.Pack;
using Raffa.Chat.Application.Planning;
using Raffa.Chat.Application.Playbook;
using Raffa.Documents.Contracts.Application;
using Raffa.Insights.Application;
using Raffa.Insights.Contracts;
using Raffa.Insights.Savings;
using Raffa.Market.Contracts;
using Raffa.Market.Retrieval;
using Raffa.Savings.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Api;

/// <summary>
/// The savings-consultant half of <see cref="AskCopilotService"/>: the evidence packs for
/// <see cref="Raffa.Chat.Domain.AskIntent.Savings"/> (one contract's grounded levers) and
/// <see cref="Raffa.Chat.Domain.AskIntent.PortfolioSavingsTarget"/> (which contracts add up to a
/// target inside a window). Every number in these packs comes from
/// <see cref="SavingsLeverCalculator"/> / <see cref="PortfolioSavingsTargetCalculator"/> over
/// validated contract facts, benchmark bands and the market corpus; the model narrates them.
/// </summary>
internal sealed partial class AskCopilotService
{
    private const int LeverClauseQueriesTopK = 2;
    private const int MarketDealsTopK = 4;
    private const int PlaybookTopK = 3;
    private const int PortfolioTargetTopN = 5;
    private const int PortfolioTargetBeyondTopN = 3;
    private const int PortfolioTargetLeversPerCandidate = 2;
    private const int ClauseExcerptMaxLength = 320;
    private const string LeverProvenance = "deterministic savings calculator";

    // Three fixed retrieval probes over this contract's own pages: the clauses a lever plan
    // depends on that structured extraction may not have typed (price increases, notice /
    // termination mechanics, volume commitments).
    private static readonly string[] LeverClauseQueries =
    [
        "price increase uplift indexation annual adjustment",
        "renewal notice period termination auto-renewal",
        "volume commitment minimum quantity true-down reduce licences",
    ];

    private sealed record LeverEvidence(
        Contract360Result Contract360,
        string SupplierName,
        SavingsLeverPlan Plan,
        IReadOnlyList<PricedLine> PricedLines,
        IReadOnlyList<MarketDeal> Deals,
        string? MarketCategory);

    /// <summary>One contract's full lever evidence: Contract 360, benchmarked priced lines, the
    /// supplier's market deals and the calculator's plan. Null when the contract is not visible.</summary>
    private async Task<LeverEvidence?> ComputeLeverEvidenceAsync(
        PortfolioListItem item, SavingsGoal? goal, CancellationToken cancellationToken)
    {
        var contract360 = await contract360QueryService
            .GetByIdAsync(CurrentTenantId, new EntityId(item.ContractId), cancellationToken)
            .ConfigureAwait(false);

        if (contract360 is null)
        {
            return null;
        }

        var supplierName = await ResolveDisplayNameAsync(item, cancellationToken).ConfigureAwait(false);
        var asOfDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        var (benchmarkSupplierName, geography) = await ResolveBenchmarkKeyAsync(contract360.Header.SupplierId, cancellationToken)
            .ConfigureAwait(false);

        var pricedLines = await InsightsEndpointExtensions
            .ToPricedLines(contract360, benchmarkService, benchmarkSupplierName, geography, asOfDate, cancellationToken)
            .ConfigureAwait(false);

        var deals = await marketDealLookup
            .GetBySupplierAsync(benchmarkSupplierName ?? supplierName, cancellationToken)
            .ConfigureAwait(false);

        var renewal = InsightsEndpointExtensions.ComputeRenewal(contract360.Header, renewalEngine);

        var inputs = new SavingsLeverInputs(
            new EntityId(item.ContractId),
            supplierName,
            contract360.Overview.Currency,
            contract360.Header.AnnualSpend,
            contract360.Header.EndDate,
            renewal.CancellationDeadline ?? contract360.Header.CancellationDeadline,
            contract360.Header.AutoRenewal,
            contract360.Overview.RenewalTermMonths,
            pricedLines,
            contract360.Clauses.Select(c => new NegotiationClauseSnapshot(c.ClauseType, c.RawText)).ToList(),
            contract360.Overview.PaymentTerms,
            deals.Select(ToMarketDealSnapshot).ToList(),
            goal?.TargetAmount,
            goal?.TargetPercent,
            asOfDate);

        var plan = SavingsLeverCalculator.Compute(inputs);

        return new LeverEvidence(contract360, supplierName, plan, pricedLines, deals, deals.FirstOrDefault()?.Category);
    }

    /// <summary>
    /// The <see cref="Raffa.Chat.Domain.AskIntent.Savings"/> pack for one contract, in
    /// pack-budget priority order: the renewal fact, the target/coverage verdict, the grounded
    /// levers, the facts they cite, the ranked negotiation points, the supplier's market deals,
    /// any recorded savings opportunities, clause evidence retrieved from the contract's own
    /// pages, the playbook entries for the levers found, and the Savings/Renewals features.
    /// </summary>
    internal async Task<IReadOnlyList<PackItem>> BuildSavingsLeverPackAsync(
        TenantId tenantId,
        PortfolioListItem namedContractItem,
        SavingsGoal? goal,
        string actor,
        CancellationToken cancellationToken)
    {
        var evidence = await ComputeLeverEvidenceAsync(namedContractItem, goal, cancellationToken).ConfigureAwait(false);
        var supplierName = evidence?.SupplierName
            ?? await ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false);

        var items = new List<PackItem> { BuildContractFactItem(namedContractItem, supplierName) };

        if (evidence is null)
        {
            items.AddRange(await BuildRecordedSavingsItemsAsync(tenantId, namedContractItem, supplierName, cancellationToken).ConfigureAwait(false));
            return items;
        }

        var contractId = new EntityId(namedContractItem.ContractId);

        await PersistGeneratedOpportunitiesAsync(tenantId, namedContractItem, evidence, actor, cancellationToken).ConfigureAwait(false);

        items.Add(BuildSavingsTargetItem(namedContractItem.ContractId, supplierName, evidence.Plan));
        items.AddRange(BuildLeverItems(namedContractItem.ContractId, supplierName, evidence.Plan, keyPrefix: null));
        items.AddRange(BuildLeverGroundingItems(namedContractItem.ContractId, supplierName, evidence));

        items.AddRange(await BuildNegotiationPointsPackAsync(
                contractId, includeRenewalUrgency: false, persistTodos: false, string.Empty, cancellationToken)
            .ConfigureAwait(false));

        items.AddRange(evidence.Deals.Take(MarketDealsTopK).Select(BuildMarketDealItem));

        items.AddRange(await BuildRecordedSavingsItemsAsync(tenantId, namedContractItem, supplierName, cancellationToken).ConfigureAwait(false));

        items.AddRange(await BuildLeverClauseEvidenceAsync(tenantId, namedContractItem, evidence.Contract360, cancellationToken).ConfigureAwait(false));

        items.AddRange(NegotiationPlaybook
            .Select(NegotiationPlaybook.CategoryFrom(evidence.MarketCategory), evidence.Plan.Levers.Select(l => l.Type.ToString()), PlaybookTopK)
            .Select(NegotiationPlaybook.ToPackItem));

        foreach (var key in new[] { CapabilityCatalog.SavingsKey, CapabilityCatalog.RenewalsKey })
        {
            if (BuildFeatureCitationPackItem(key) is { } feature)
            {
                items.Add(feature);
            }
        }

        return DistinctByCitationKey(items);
    }

    /// <summary>The lever items (target verdict + levers) appended to a scoped renewal-strategy
    /// pack so "come affrontare il rinnovo" also carries the money.</summary>
    private async Task<IReadOnlyList<PackItem>> BuildLeverAddendumAsync(
        TenantId tenantId, PortfolioListItem namedContractItem, SavingsGoal? goal, string actor, CancellationToken cancellationToken)
    {
        var evidence = await ComputeLeverEvidenceAsync(namedContractItem, goal, cancellationToken).ConfigureAwait(false);
        if (evidence is null)
        {
            return [];
        }

        await PersistGeneratedOpportunitiesAsync(tenantId, namedContractItem, evidence, actor, cancellationToken).ConfigureAwait(false);

        var items = new List<PackItem>
        {
            BuildSavingsTargetItem(namedContractItem.ContractId, evidence.SupplierName, evidence.Plan),
        };
        items.AddRange(BuildLeverItems(namedContractItem.ContractId, evidence.SupplierName, evidence.Plan, keyPrefix: null));
        items.AddRange(evidence.Deals.Take(2).Select(BuildMarketDealItem));
        items.AddRange(NegotiationPlaybook
            .Select(NegotiationPlaybook.CategoryFrom(evidence.MarketCategory), evidence.Plan.Levers.Select(l => l.Type.ToString()), 2)
            .Select(NegotiationPlaybook.ToPackItem));
        return items;
    }

    /// <summary>
    /// The <see cref="Raffa.Chat.Domain.AskIntent.PortfolioSavingsTarget"/> pack: every visible
    /// contract's lever plan, ranked by <see cref="PortfolioSavingsTargetCalculator"/> into the
    /// candidates that can be acted on inside the window and those that cannot, with the
    /// cumulative coverage against the target.
    /// </summary>
    internal async Task<IReadOnlyList<PackItem>> BuildPortfolioSavingsTargetPackAsync(
        PortfolioPage portfolio,
        SavingsGoal? goal,
        CancellationToken cancellationToken)
    {
        if (portfolio.Items.Count == 0)
        {
            return [];
        }

        var asOfDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var evidenceByContract = new Dictionary<Guid, (PortfolioListItem Item, LeverEvidence Evidence)>();

        foreach (var item in portfolio.Items)
        {
            // Per-contract plans carry no target: the target belongs to the portfolio.
            var evidence = await ComputeLeverEvidenceAsync(item, goal: null, cancellationToken).ConfigureAwait(false);
            if (evidence is not null)
            {
                evidenceByContract[item.ContractId] = (item, evidence);
            }
        }

        var candidateInputs = evidenceByContract.Values
            .Select(x => new PortfolioSavingsCandidateInputs(
                new EntityId(x.Item.ContractId),
                x.Evidence.SupplierName,
                x.Evidence.Contract360.Overview.Currency,
                x.Evidence.Contract360.Header.AnnualSpend,
                x.Evidence.Plan.CancellationDeadline,
                x.Evidence.Contract360.Header.EndDate,
                x.Evidence.Contract360.Header.AutoRenewal,
                x.Evidence.Plan))
            .ToList();

        var plan = PortfolioSavingsTargetCalculator.Compute(candidateInputs, goal?.TargetAmount, goal?.WindowDays, asOfDate);

        var items = new List<PackItem>();

        var targetValues = new List<PackValue>
        {
            new("windowDays", plan.WindowDays?.ToString(CultureInfo.InvariantCulture) ?? "0", PackValueKind.Number),
            new("coverageLow", Amount(plan.CoverageLow), PackValueKind.Amount, plan.Currency),
            new("coverageHigh", Amount(plan.CoverageHigh), PackValueKind.Amount, plan.Currency),
            new("candidatesInWindow", plan.InWindow.Count.ToString(CultureInfo.InvariantCulture), PackValueKind.Number),
        };
        if (plan.TargetAmount is { } target)
        {
            targetValues.Insert(0, new PackValue("targetAmount", Amount(target), PackValueKind.Amount, plan.Currency));
        }

        items.Add(new PackItem(
            InsightsCitationKeys.Calc("portfolio-target"),
            PackCorpus.Calc,
            "Portfolio — saving target and coverage inside the window",
            FeasibilityLabel(plan.Feasibility),
            null, null,
            plan.Explanation,
            "/renewals", null, null,
            LeverProvenance,
            targetValues));

        var rank = 0;
        foreach (var candidate in plan.InWindow.Take(PortfolioTargetTopN))
        {
            rank++;
            var (item, evidence) = evidenceByContract[candidate.ContractId.Value];
            items.Add(BuildCandidateItem($"candidate[{rank}]", candidate, evidence, rankLabel: $"#{rank} inside the window"));
        }

        var beyondRank = 0;
        foreach (var candidate in plan.BeyondWindow.Take(PortfolioTargetBeyondTopN))
        {
            beyondRank++;
            var (item, evidence) = evidenceByContract[candidate.ContractId.Value];
            items.Add(BuildCandidateItem($"candidate-next[{beyondRank}]", candidate, evidence, rankLabel: "next period"));
        }

        // The best levers of the top candidates, so the answer can say *how*, not only *where*.
        foreach (var candidate in plan.InWindow.Take(3))
        {
            var (item, evidence) = evidenceByContract[candidate.ContractId.Value];
            var shortId = item.ContractId.ToString("N")[..8];
            var topLevers = evidence.Plan with
            {
                Levers = evidence.Plan.Levers
                    .OrderByDescending(l => l.EstimatedHigh ?? 0m)
                    .Take(PortfolioTargetLeversPerCandidate)
                    .ToList(),
            };
            items.AddRange(BuildLeverItems(item.ContractId, evidence.SupplierName, topLevers, keyPrefix: shortId));
        }

        foreach (var candidate in plan.InWindow.Take(2))
        {
            var (_, evidence) = evidenceByContract[candidate.ContractId.Value];
            var bestDeal = evidence.Deals.FirstOrDefault(d => d.DiscountAchievedPct is not null) ?? evidence.Deals.FirstOrDefault();
            if (bestDeal is not null)
            {
                items.Add(BuildMarketDealItem(bestDeal));
            }
        }

        var leverNames = plan.InWindow
            .SelectMany(c => c.TopLeverKeys)
            .Select(key => evidenceByContract.Values.SelectMany(x => x.Evidence.Plan.Levers).FirstOrDefault(l => l.Key == key)?.Type.ToString())
            .Where(name => name is not null)
            .Select(name => name!)
            .Distinct()
            .ToList();
        leverNames.Insert(0, PlaybookLever.NoticeTiming.ToString());

        items.AddRange(NegotiationPlaybook
            .Select(PlaybookCategory.Generic, leverNames, 2)
            .Select(NegotiationPlaybook.ToPackItem));

        foreach (var key in new[] { CapabilityCatalog.RenewalsKey, CapabilityCatalog.SavingsKey, CapabilityCatalog.PortfolioKey })
        {
            if (BuildFeatureCitationPackItem(key) is { } feature)
            {
                items.Add(feature);
            }
        }

        return DistinctByCitationKey(items);
    }

    /// <summary>
    /// Persist-all, like the negotiation to-dos: every quantified lever becomes (or refreshes) a
    /// generated savings opportunity keyed by the lever, so the Savings page fills from real use
    /// of Ask Raffa without anyone typing a number. Never touches a row a person already owns.
    /// A failure here never fails the turn.
    /// </summary>
    private async Task PersistGeneratedOpportunitiesAsync(
        TenantId tenantId, PortfolioListItem item, LeverEvidence evidence, string actor, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(actor) || evidence.Plan.AnnualSpend is not { } spend || spend <= 0m)
        {
            return;
        }

        var generated = evidence.Plan.Levers
            .Where(l => l.EstimatedHigh is > 0m)
            .Select(l => new GeneratedSavingsOpportunity(
                l.Key,
                $"{evidence.SupplierName} — {l.Label}",
                item.SupplierId is { } supplierId ? new EntityId(supplierId) : null,
                spend,
                evidence.Plan.Currency,
                l.EstimatedLow ?? 0m,
                l.EstimatedHigh!.Value,
                LeverConfidence(l.Type)))
            .ToList();

        if (generated.Count == 0)
        {
            return;
        }

        try
        {
            await savingsOpportunityService
                .UpsertGeneratedAsync(tenantId, new EntityId(item.ContractId), generated, actor, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Persisting is a side benefit of the turn, never its purpose: the answer still ships.
        }
    }

    private static double LeverConfidence(SavingsLeverType type) => type switch
    {
        SavingsLeverType.AboveBandRepricing => 0.8,
        SavingsLeverType.MarketDiscount => 0.7,
        SavingsLeverType.UpliftCap => 0.6,
        SavingsLeverType.MultiYearTerm => 0.5,
        _ => 0.4,
    };

    // ----- item builders -----

    private static PackItem BuildSavingsTargetItem(Guid contractId, string supplierName, SavingsLeverPlan plan)
    {
        var values = new List<PackValue>();
        if (plan.TargetAmount is { } target)
        {
            values.Add(new PackValue("targetAmount", Amount(target), PackValueKind.Amount, plan.Currency));
        }

        if (plan.TargetPercent is { } targetPercent)
        {
            values.Add(new PackValue("targetPercent", Pct(targetPercent), PackValueKind.Percentage));
        }

        if (plan.AnnualSpend is { } spend)
        {
            values.Add(new PackValue("annualSpend", Amount(spend), PackValueKind.Amount, plan.Currency));
        }

        values.Add(new PackValue("coverageLow", Amount(plan.CoverageLow), PackValueKind.Amount, plan.Currency));
        values.Add(new PackValue("coverageHigh", Amount(plan.CoverageHigh), PackValueKind.Amount, plan.Currency));

        if (plan.CancellationDeadline is { } deadline)
        {
            values.Add(new PackValue("cancellationDeadline", deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PackValueKind.Date));
        }

        if (plan.DaysToDeadline is { } days)
        {
            values.Add(new PackValue("daysToDeadline", days.ToString(CultureInfo.InvariantCulture), PackValueKind.Number));
        }

        return new PackItem(
            InsightsCitationKeys.Calc("savings-target"),
            PackCorpus.Calc,
            $"{supplierName} — saving target and lever coverage",
            FeasibilityLabel(plan.Feasibility),
            null, null,
            plan.Explanation,
            $"/contracts/{contractId}", null, null,
            LeverProvenance,
            values,
            contractId.ToString());
    }

    private static IEnumerable<PackItem> BuildLeverItems(Guid contractId, string supplierName, SavingsLeverPlan plan, string? keyPrefix)
    {
        foreach (var lever in plan.Levers)
        {
            var values = new List<PackValue>();
            if (lever.EstimatedLow is { } low)
            {
                values.Add(new PackValue("estimatedLow", Amount(low), PackValueKind.Amount, plan.Currency));
            }

            if (lever.EstimatedHigh is { } high)
            {
                values.Add(new PackValue("estimatedHigh", Amount(high), PackValueKind.Amount, plan.Currency));
            }

            if (lever.Percent is { } pct)
            {
                values.Add(new PackValue("percent", Pct(pct), PackValueKind.Percentage));
            }

            var key = keyPrefix is null ? $"lever[{lever.Key}]" : $"lever[{keyPrefix}:{lever.Key}]";
            var subtitle = lever.EstimatedHigh is { } h
                ? $"{lever.Type} · up to {plan.Currency} {Amount(h)} a year"
                : $"{lever.Type} · qualitative";

            yield return new PackItem(
                InsightsCitationKeys.Calc(key),
                PackCorpus.Calc,
                $"{supplierName} — {lever.Label}",
                subtitle,
                null, null,
                $"{lever.Rationale} Ask: {lever.WhatToAsk}",
                $"/contracts/{contractId}", null, null,
                LeverProvenance,
                values,
                contractId.ToString());
        }
    }

    /// <summary>The contract facts the levers cite that are not otherwise in the pack: the
    /// above-band priced lines (current price + band) and the price-increase clauses.</summary>
    private static IEnumerable<PackItem> BuildLeverGroundingItems(Guid contractId, string supplierName, LeverEvidence evidence)
    {
        var currency = evidence.Contract360.Overview.Currency;

        for (var i = 0; i < evidence.PricedLines.Count; i++)
        {
            var line = evidence.PricedLines[i];
            if (line.UnitPrice is not { } unitPrice || line.Benchmark is not { } band || unitPrice <= band.P50)
            {
                continue;
            }

            var values = new List<PackValue>
            {
                new("unitPrice", Amount2(unitPrice), PackValueKind.Amount, line.Currency ?? currency),
            };
            if (line.Quantity is { } quantity)
            {
                values.Add(new PackValue("quantity", quantity.ToString("0.##", CultureInfo.InvariantCulture), PackValueKind.Number));
            }

            yield return new PackItem(
                InsightsCitationKeys.Fact(new EntityId(contractId), $"priced-line[{i}].unitPrice"),
                PackCorpus.Tenant,
                $"{supplierName} · {line.Description} · current price",
                null, null, null,
                $"Current unit price {line.Currency ?? currency} {Amount2(unitPrice)}" +
                (line.Quantity is { } q ? $" for {q.ToString("0.##", CultureInfo.InvariantCulture)} units." : "."),
                $"/contracts/{contractId}", null, null,
                "validated contract",
                values,
                contractId.ToString());

            yield return BuildMarketPricedLineItem(supplierName, line);
        }

        var upliftCitations = evidence.Plan.Levers
            .Where(l => l.Type == SavingsLeverType.UpliftCap)
            .SelectMany(l => l.CitationKeys)
            .ToHashSet(StringComparer.Ordinal);

        for (var i = 0; i < evidence.Contract360.Clauses.Count; i++)
        {
            var key = InsightsCitationKeys.Fact(new EntityId(contractId), $"clause[{i}]");
            if (!upliftCitations.Contains(key))
            {
                continue;
            }

            var clause = evidence.Contract360.Clauses[i];
            var excerpt = clause.RawText.Length <= ClauseExcerptMaxLength
                ? clause.RawText
                : clause.RawText[..ClauseExcerptMaxLength] + "…";

            yield return new PackItem(
                key,
                PackCorpus.Tenant,
                $"{supplierName} · {clause.ClauseType} clause",
                clause.SourcePage is { } page ? $"p.{page}" : null,
                clause.SourcePage,
                clause.SourceSpan,
                excerpt,
                $"/contracts/{contractId}", null, null,
                "validated contract",
                [],
                contractId.ToString(),
                clause.SourceDocumentId?.Value.ToString());
        }
    }

    private static PackItem BuildMarketDealItem(MarketDeal deal)
    {
        var note = MarketNoteComposer.Compose(deal);
        var values = new List<PackValue>
        {
            new("p25", Amount2(deal.UnitPriceP25), PackValueKind.Amount, deal.Currency),
            new("p50", Amount2(deal.UnitPriceP50), PackValueKind.Amount, deal.Currency),
            new("p75", Amount2(deal.UnitPriceP75), PackValueKind.Amount, deal.Currency),
            new("termMonths", deal.TermMonths.ToString(CultureInfo.InvariantCulture), PackValueKind.Number),
            new("sampleSize", deal.SampleSize.ToString(CultureInfo.InvariantCulture), PackValueKind.Number),
        };

        if (deal.DiscountAchievedPct is { } discount)
        {
            values.Add(new PackValue("discountAchievedPct", Pct((decimal)discount), PackValueKind.Percentage));
        }

        if (deal.UpliftCapPct is { } uplift)
        {
            values.Add(new PackValue("upliftCapPct", Pct((decimal)uplift), PackValueKind.Percentage));
        }

        if (deal.NoticeDays is { } notice)
        {
            values.Add(new PackValue("noticeDays", notice.ToString(CultureInfo.InvariantCulture), PackValueKind.Number));
        }

        return new PackItem(
            InsightsCitationKeys.Market(deal.RecordId),
            PackCorpus.Market,
            note.Title,
            $"{deal.TermMonths} months · {deal.Geography} · {note.Provenance}",
            null, null,
            note.Snippet,
            null, null,
            deal.RecordId,
            note.Provenance,
            values);
    }

    private static PackItem BuildCandidateItem(string keyTail, PortfolioSavingsCandidate candidate, LeverEvidence evidence, string rankLabel)
    {
        var values = new List<PackValue>
        {
            new("coverageLow", Amount(candidate.CoverageLow), PackValueKind.Amount, candidate.Currency),
            new("coverageHigh", Amount(candidate.CoverageHigh), PackValueKind.Amount, candidate.Currency),
            new("runningCumulativeHigh", Amount(candidate.RunningCumulativeHigh), PackValueKind.Amount, candidate.Currency),
        };

        if (candidate.AnnualSpend is { } spend)
        {
            values.Add(new PackValue("annualSpend", Amount(spend), PackValueKind.Amount, candidate.Currency));
        }

        if (candidate.ActionDate is { } actionDate)
        {
            values.Add(new PackValue("actionDate", actionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), PackValueKind.Date));
        }

        if (candidate.DaysToAction is { } days)
        {
            values.Add(new PackValue("daysToAction", days.ToString(CultureInfo.InvariantCulture), PackValueKind.Number));
        }

        var leverSummary = string.Join("; ", evidence.Plan.Levers
            .Where(l => candidate.TopLeverKeys.Contains(l.Key, StringComparer.Ordinal))
            .Select(l => l.EstimatedHigh is { } h
                ? $"{l.Label}: up to {candidate.Currency} {Amount(h)}"
                : l.Label));

        var snippet = $"{candidate.SupplierName}: grounded levers worth {candidate.Currency} {Amount(candidate.CoverageLow)} to {candidate.Currency} {Amount(candidate.CoverageHigh)} a year" +
                      (candidate.AnnualSpend is { } s ? $" on an annual spend of {candidate.Currency} {Amount(s)}" : string.Empty) +
                      $". {candidate.WhyNow}" +
                      (leverSummary.Length > 0 ? $" Best levers — {leverSummary}." : string.Empty) +
                      (candidate.InWindow ? $" Cumulative with the candidates above: {candidate.Currency} {Amount(candidate.RunningCumulativeHigh)}." : string.Empty);

        return new PackItem(
            InsightsCitationKeys.Calc(keyTail),
            PackCorpus.Calc,
            $"{candidate.SupplierName} — {rankLabel}",
            candidate.InWindow ? "inside the window" : "beyond the window",
            null, null,
            snippet,
            $"/contracts/{candidate.ContractId.Value}", null, null,
            LeverProvenance,
            values,
            candidate.ContractId.Value.ToString());
    }

    private async Task<IReadOnlyList<PackItem>> BuildRecordedSavingsItemsAsync(
        TenantId tenantId, PortfolioListItem namedContractItem, string supplierName, CancellationToken cancellationToken)
    {
        IReadOnlyList<SavingsOpportunityResult> allSavings;
        try
        {
            allSavings = await savingsOpportunityService.ListAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Recorded opportunities are one evidence source among several: a Savings store that
            // cannot be reached must not turn a fully grounded lever pack into a 500.
            return [];
        }

        // Hand-recorded rows only: the generated ones (OpportunityKey set) are this turn's own
        // levers, already in the pack as calc items -- listing them twice would double-count.
        var contractSavings = allSavings
            .Where(o => o.ContractId?.Value == namedContractItem.ContractId && o.OpportunityKey is null)
            .ToList();

        return contractSavings.Select((opportunity, index) => new PackItem(
            $"fact:{namedContractItem.ContractId}:saving[{index}]",
            PackCorpus.Tenant,
            $"{supplierName} — {opportunity.Type}",
            $"{opportunity.Status} · {opportunity.ConfidenceLevel}",
            null, null,
            $"Recorded saving opportunity: {opportunity.EstimatedSavingsLow}–{opportunity.EstimatedSavingsHigh} {opportunity.Currency}" +
            (opportunity.Owner is { } owner ? $", owner {owner}." : "."),
            $"/savings", null, null,
            "recorded savings opportunity",
            [
                new PackValue("estimatedSavingsLow", opportunity.EstimatedSavingsLow.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, opportunity.Currency),
                new PackValue("estimatedSavingsHigh", opportunity.EstimatedSavingsHigh.ToString(CultureInfo.InvariantCulture), PackValueKind.Amount, opportunity.Currency),
            ],
            namedContractItem.ContractId.ToString())).ToList();
    }

    /// <summary>Clause evidence retrieved from this contract's own pages for the three lever
    /// probes; silently empty on a host whose embedding store cannot run the vector query.</summary>
    private async Task<IReadOnlyList<PackItem>> BuildLeverClauseEvidenceAsync(
        TenantId tenantId, PortfolioListItem namedContractItem, Contract360Result contract360, CancellationToken cancellationToken)
    {
        var items = new List<PackItem>();
        var contractId = new EntityId(namedContractItem.ContractId);

        foreach (var query in LeverClauseQueries)
        {
            Result<EmbeddingContractScopedSearchResult> searchResult;
            try
            {
                searchResult = await embeddingRetrievalService
                    .SearchByContractAsync(
                        new EmbeddingSearchQuery(tenantId, query, LeverClauseQueriesTopK, contractId, PeerTopK: 0),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return items;
            }

            if (searchResult.IsFailure)
            {
                continue;
            }

            foreach (var hit in searchResult.Value.ThisContract)
            {
                var clause = contract360.Clauses.FirstOrDefault(c => c.ClauseId == hit.SourceId);
                items.Add(BuildClausePackItem(hit, clause, namedContractItem.ContractId, isPeer: false));
            }
        }

        return items;
    }

    private static MarketDealSnapshot ToMarketDealSnapshot(MarketDeal deal) =>
        new(
            deal.RecordId,
            deal.Product,
            deal.Category,
            deal.Currency,
            deal.TermMonths,
            deal.UnitPriceP25,
            deal.UnitPriceP50,
            deal.UnitPriceP75,
            deal.DiscountAchievedPct,
            deal.UpliftCapPct,
            deal.NoticeDays,
            deal.PaymentTerms,
            deal.SampleSize,
            deal.ClosingPeriod);

    private static IReadOnlyList<PackItem> DistinctByCitationKey(IEnumerable<PackItem> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<PackItem>();
        foreach (var item in items)
        {
            if (seen.Add(item.CitationKey))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static string FeasibilityLabel(SavingsFeasibility feasibility) => feasibility switch
    {
        SavingsFeasibility.Reachable => "target reachable",
        SavingsFeasibility.Stretch => "target is a stretch",
        SavingsFeasibility.NotSupported => "target not supported by the evidence",
        _ => "no target named",
    };

    private static string Amount(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);

    private static string Amount2(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Pct(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero).ToString("0.##", CultureInfo.InvariantCulture);
}
