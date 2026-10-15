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

namespace Raffa.AiFlows.Ask.Reply;

/// <summary>
/// The five server-decided notice replies (a notice question never reaches the answer composer).
/// Moved verbatim from <see cref="Raffa.AiFlows.Ask.Orchestration.AskCopilotService"/> (F1 Ask split, PR-2).
/// </summary>
public sealed class NoticeFallbackReplies(ContractContextReader context, CapabilityRouting capabilityRouting)
{
    /// <summary>
    /// Task E30/F02/US01/T01 (NW-94; parent story us-01-notice-fallbacks AC-1/AC-2/AC-3, "a scoped
    /// notice turn never asks 'which supplier'"): the five server-decided outcomes for a notice
    /// question, called directly from <see cref="AskCopilotService.BuildInDomainReplyAsync"/>'s own short-circuit
    /// (see that method's own comment at the call site) -- this method uses
    /// <see cref="NoticeFacts.BuildNoticeFactItem"/>/<see cref="NoticeFacts.BuildMatchingClauseItem"/> directly so it gets each
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
    /// <see cref="NoticeFacts.BuildNoticeFactItem"/>'s own primary branch already reads it. <c>hasSpan</c> is
    /// the matching clause resolving to a real document page --
    /// <see cref="ClausePackBuilder.ResolveTenantClauseLinks"/>'s tier 1 -- read straight off the already-built
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
    /// named, never the generic <see cref="AbstainRecovery.ResolveAbstainRecoveryActions"/> hint.</item>
    /// </list>
    /// A contract resolved by <see cref="ResolveNamedContractItem"/> but since vanished from
    /// <see cref="Contract360QueryService"/> (deleted mid-call -- a rare race) falls straight
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
    /// <see cref="AskCopilotService.BuildInDomainReplyAsync"/>'s own pack-building path already applies, so this
    /// short-circuit cannot silently narrate the wrong one of several same-name contracts either.
    /// Never folded into the case-4 abstain: an abstain already says "cannot determine", and this
    /// keeps that reply's shape identical to every other abstain in this file (no citations).
    /// </para>
    /// </summary>
    /// <param name="question">The user's question -- read only for its language, so the cases
    /// with no grounded date (4 and 5) answer with a way forward in that language.</param>
    /// <param name="namedContractItem">The turn's own resolved contract (scoped id, or a
    /// name/soonest-deadline match) -- <see langword="null"/> for case 5.</param>
    /// <param name="disambiguationItem">Echoes <see cref="AskCopilotService.BuildInDomainReplyAsync"/>'s own
    /// same-named local of this call -- see this method's own "Disambiguation is never dropped"
    /// paragraph above.</param>
    /// <param name="routingContext">The turn's own already-resolved <see cref="RoutingContext"/>
    /// (built once by <see cref="AskCopilotService.BuildInDomainReplyAsync"/>, before this method is ever called) --
    /// its <see cref="RoutingContext.ContractId"/> already matches <paramref name="namedContractItem"/>
    /// exactly, so every action below resolves through the identical routing facts the rest of this
    /// turn uses.</param>
    internal async Task<CopilotReply> BuildNoticeFallbackReplyAsync(
        string question,
        PortfolioListItem? namedContractItem,
        PackItem? disambiguationItem,
        RoutingContext routingContext,
        CancellationToken cancellationToken)
    {
        if (namedContractItem is null)
        {
            return BuildUnscopedNoticeAbstain(question, routingContext);
        }

        var contract360 = await context.GetContract360Async(namedContractItem.ContractId, cancellationToken).ConfigureAwait(false);

        var supplierName = await context.ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false);

        // Case 1/2/4's shared action -- "360 Review" (task text) -- always resolves for a scoped
        // turn: ContractDetailKey's own CapabilityAvailability.Always (CapabilityCatalog.cs) never
        // routes through the NeedsValidatedContract/Upload replacement, unlike PortfolioKey below.
        var reviewActions = capabilityRouting.ResolveActions(
            [CapabilityIntent.HowTo(CapabilityCatalog.ContractDetailKey)], routingContext);

        if (contract360 is null)
        {
            return BuildNoGroundableNoticeAbstain(question, supplierName, reviewActions);
        }

        var factItem = NoticeFacts.BuildNoticeFactItem(namedContractItem, contract360.Renewal, supplierName);
        var clauseItem = NoticeFacts.BuildMatchingClauseItem(contract360, namedContractItem.ContractId);
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

        // No date on file: say so, and give the notice comparable customers have as the market's
        // estimate (persona v2.5's safety net), alongside how to pin the real date down.
        var typicalNoticeDays = await TypicalNoticeDaysAsync(contract360, supplierName, cancellationToken).ConfigureAwait(false);
        return BuildNoGroundableNoticeAbstain(question, supplierName, reviewActions, typicalNoticeDays);
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
    /// unless a citation genuinely backs the decline"). Written as a way forward -- how to pin the
    /// date down now (<see cref="HelpfulFallbackAnswer.NoticeDateMissing"/>) -- never a bare "could
    /// not find".</summary>
    private static CopilotReply BuildNoGroundableNoticeAbstain(
        string question, string supplierName, IReadOnlyList<CopilotAction> reviewActions, int? typicalNoticeDays = null) =>
        new(
            ReplyKind.Abstain,
            HelpfulFallbackAnswer.NoticeDateMissing(question, supplierName, typicalNoticeDays),
            [],
            reviewActions,
            ReplyProvenance.NoModelCall([]),
            []);

    /// <summary>Case 5 (NW-94): an unscoped notice question has no "this contract" to answer about.
    /// Abstains with a Portfolio recovery instead of guessing one -- the honest counterpart, for the
    /// unscoped turn, to this story's own "a scoped notice turn never asks 'which supplier'": never
    /// guess, just offer the two ways to the exact date (open the contract, or name the supplier --
    /// <see cref="HelpfulFallbackAnswer.NoticeWithoutContract"/>) and point at the one screen that
    /// lets the caller pick. <see cref="CapabilityCatalog.PortfolioKey"/>'s own <c>NeedsValidatedContract</c>
    /// availability (CapabilityCatalog.cs) already replaces this with the Documents upload action
    /// for a zero-validated-contract tenant (<see cref="CapabilityRouting"/>'s own "Availability
    /// replacement" rule) -- correct here too: Portfolio is exactly as unusable as the notice
    /// question itself would be for that tenant.</summary>
    private CopilotReply BuildUnscopedNoticeAbstain(string question, RoutingContext routingContext) =>
        new(
            ReplyKind.Abstain,
            HelpfulFallbackAnswer.NoticeWithoutContract(question),
            [],
            capabilityRouting.ResolveActions([CapabilityIntent.HowTo(CapabilityCatalog.PortfolioKey)], routingContext),
            ReplyProvenance.NoModelCall([]),
            []);

    /// <summary>The typical notice period comparable customers of this contract's supplier have,
    /// for the deterministic "no notice date on file" reply; <see langword="null"/> when the market
    /// records none.</summary>
    private async Task<int?> TypicalNoticeDaysAsync(Contract360Result contract360, string supplierName, CancellationToken cancellationToken)
    {
        var (benchmarkSupplierName, geography) = await context.ResolveBenchmarkKeyAsync(contract360.Header.SupplierId, cancellationToken)
            .ConfigureAwait(false);
        var allDeals = await context.GetDealsBySupplierAsync(benchmarkSupplierName ?? supplierName, cancellationToken).ConfigureAwait(false);

        return MarketSafetyNet.TypicalNoticeDays(AskCopilotService.NarrowDealsToContract(allDeals, contract360.Overview.Currency, geography));
    }
}
