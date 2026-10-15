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
/// The DocumentStatus intent's pack. Moved verbatim from
/// <see cref="Raffa.AiFlows.Ask.Orchestration.AskCopilotService"/> (F1 Ask split, PR-2).
/// </summary>
internal static class DocumentStatusPack
{
    internal static IReadOnlyList<PackItem> Build(
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
}
