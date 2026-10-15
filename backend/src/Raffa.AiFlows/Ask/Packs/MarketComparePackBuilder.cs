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
/// The MarketCompare intent's pack. Moved verbatim from
/// <see cref="Raffa.AiFlows.Ask.Orchestration.AskCopilotService"/> (F1 Ask split, PR-2).
/// </summary>
public sealed class MarketComparePackBuilder(
    ContractContextReader context,
    IMarketKnowledgeRetrieval marketKnowledgeRetrieval,
    IClock clock)
{
    private const int MarketNotesTopK = 3;

    /// <summary>Also read by <c>AskCopilotService.BuildRenewalStrategyPackAsync</c> until that
    /// builder moves too (PR-3).</summary>
    internal const int MaxPricedLinesForBenchmark = 2;

    /// <summary>
    /// <c>internal</c>, not <c>private</c> — the same test-reachability precedent
    /// <see cref="ClausePackBuilder.ResolveTenantClauseLinks"/> already establishes (<c>Raffa.Api.Tests</c>' own
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

        var contract360 = await context.GetContract360Async(namedContractItem.ContractId, cancellationToken).ConfigureAwait(false);

        if (contract360 is null)
        {
            return [];
        }

        var supplierName = await context.ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false);
        var asOfDate = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

        // Same (supplier name, geography) key and the same async ToPricedLines overload
        // BuildRenewalStrategyPackAsync/GET /api/contracts/{id}/strategy use (ADR-024 w17 clause 7;
        // task E28/F01/US01/T01, NW-82) -- one resolution, three consumers, never GoverningLaw as a
        // geography stand-in (see this type's own doc comment).
        var (benchmarkSupplierName, geography) = await context.ResolveBenchmarkKeyAsync(contract360.Header.SupplierId, cancellationToken)
            .ConfigureAwait(false);

        var pricedLines = await context.ResolvePricedLinesAsync(contract360, benchmarkSupplierName, geography, asOfDate, cancellationToken)
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
}
