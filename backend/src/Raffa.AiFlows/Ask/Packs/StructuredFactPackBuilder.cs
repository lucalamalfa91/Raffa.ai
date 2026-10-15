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
/// The StructuredFact intent's pack (named-contract fact and excerpts, the legacy deterministic
/// queries, or the soonest-first portfolio snapshot). Moved verbatim from
/// <see cref="Raffa.AiFlows.Ask.Orchestration.AskCopilotService"/> (F1 Ask split, PR-2).
/// </summary>
public sealed class StructuredFactPackBuilder(ContractContextReader context, ClausePackBuilder clauses, IClock clock)
{
    // Reused, not re-implemented (task's own "extend / reuse" file list): both are pure/stateless
    // (Appendix C rule 6), so a private, non-DI instance is exactly how Raffa.Chat.Application
    // .Planning.IntentPlanner already reuses AskRaffaQueryRouter itself.
    private readonly AskRaffaQueryRouter _legacyRouter = new();
    private readonly DeterministicQueryPlanner _legacyPlanner = new();
    private readonly DeterministicQueryHandler _legacyHandler = new(clock);

    /// <summary>
    /// <paramref name="supplierNames"/> is the per-turn name map <see cref="AskAsync"/> already
    /// resolved for the whole portfolio, so every multi-contract item below can be titled
    /// "Salesforce · MSA" rather than "MSA · MSA" (R-SUP-04; closes the golden set's
    /// GAP-ASK-STRUCTURED-PACK-DROPS-SUPPLIER-NAME). The named-contract branch keeps its own
    /// single lookup, which is the same source of truth.
    /// </summary>
    internal async Task<IReadOnlyList<PackItem>> BuildStructuredFactPackAsync(
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
                await context.ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false));
            // Clause rows, not embedding search: InMemory EF cannot translate pgvector
            // CosineDistance, and a date/spend fact should quote the extracted clause when one
            // exists rather than 500 the whole Ask turn.
            var excerpts = await clauses.BuildNamedContractExcerptItemsAsync(namedContractItem, cancellationToken)
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
    /// <see cref="Raffa.AiFlows.Shared.Reply.CopilotReplyBuilder.BuildCitations"/> (copies a
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

            // BuildStructuredFactPackAsync only ever calls this helper inside its own
            // "result.Kind != DeterministicQueryKind.Unsupported" branch, so no other member can
            // reach here. A new DeterministicQueryKind member must get its own chrome-free copy
            // above: failing loudly beats a default that silently reintroduces developer trace text.
            _ => throw new UnreachableException(
                $"DescribeStructuredResult is only called for a supported deterministic query, not {result.Kind}."),
        };
}
