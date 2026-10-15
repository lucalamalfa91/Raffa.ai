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

namespace Raffa.AiFlows.Ask.Orchestration;

/// <summary>
/// The answer half of an in-domain turn: proposal and lead, the composer, decline-to-interview, the
/// honest abstains and the resolved actions. Moved verbatim from <see cref="AskCopilotService"/>
/// (F1 Ask split, PR-4).
/// </summary>
public sealed class AnswerPhase(
    AnswerComposer answerComposer,
    InterviewPlanner interviewPlanner,
    InterviewOptions interviewOptions,
    WebResearchFlow webResearchFlow,
    CapabilityRouting capabilityRouting,
    AbstainRecovery abstainRecovery,
    ContractContextReader context)
{
    internal async Task<(CopilotReply Reply, bool GuardIntervened, bool FallbackUsed)> ComposeAsync(
        InDomainTurn turn,
        AgentPhaseResult agents,
        CancellationToken cancellationToken)
    {
        var tenantId = turn.Scope.TenantId;
        var question = turn.Scope.Question;
        var recentTurns = turn.Scope.RecentTurns;
        var portfolio = turn.Scope.Portfolio;
        var hints = turn.Scope.Hints;
        var previousRaffaTurnWasInterview = turn.Scope.PreviousRaffaTurnWasInterview;
        var plan = turn.Plan;
        var namedContractItem = turn.NamedContractItem;
        var routingContext = turn.RoutingContext;
        var boundedPack = agents.BoundedPack;
        var dataChecks = agents.DataChecks;

        // Task E31/F03/US01/T01 (q3-persist; NW-97; ADR-024 w19 cl. 21/ADR-028): exactly the
        // condition the switch above already used to pick the live Q3 composition
        // (BuildRenewalStrategyWithEvidenceAsync, persistTodos: true) -- re-read, never
        // re-derived, so the server-injected Renewals action below can never fire for a turn that
        // did not just upsert. See the injected-action block near this method's own return.
        var isQ3PersistTurn = IntentPolicy.IsQ3PersistTurn(plan.Intent, namedContractItem);

        // Every turn that cannot end in a grounded answer still ends in a proposal (persona v2.4:
        // Ask never answers "I don't have data I trust enough"): the screen where this kind of
        // answer lives, two questions that route to a real answer (ADR-024 "every abstain has a
        // clickable next step"), and HelpfulFallbackAnswer's way forward for this kind of question.
        var recoveryActions = abstainRecovery.ResolveAbstainRecoveryActions(portfolio, routingContext, plan.Intent);
        var recoveryFollowUps = AbstainRecovery.AbstainFollowUps(question, portfolio, plan.Intent);
        // The deterministic proposal opens, like the model must, with the honest gaps the market data
        // check found and what the market says in their place (step 1 of the flow above).
        var italianQuestion = HelpfulFallbackAnswer.IsItalian(question);
        var lead = MarketSafetyNet.Lead(dataChecks, italianQuestion);
        if (lead is null && plan.Intent == AskIntent.Clause && namedContractItem is not null && boundedPack.Count == 0)
        {
            // Honest first: the clause is not on file for this contract.
            var clauseSupplier = await context.ResolveDisplayNameAsync(namedContractItem, cancellationToken).ConfigureAwait(false);
            lead = MarketSafetyNet.ClauseNotFoundLead(clauseSupplier, italianQuestion);
        }

        var proposal = (lead ?? string.Empty) +
            HelpfulFallbackAnswer.Proposal(question, AbstainRecovery.ProposalArea(question, plan.Intent), emptyWorkspace: portfolio.Items.Count == 0);

        if (boundedPack.Count == 0)
        {
            // ADR-027 §D8 (task E16/F02/US03/T01): no number here. `portfolio.Items.Count` is the
            // UNFILTERED portfolio -- every bootstrap shell a still-processing document created --
            // so rendering it as "N validated contract(s)" asserted a fabricated fact to the user.
            // The one definition of "validated" is ADR-026 §D2's CountValidatedContractsAsync; the
            // pre-question off-state already renders that number from the server, and the
            // proposal needs none.
            return (new CopilotReply(
                ReplyKind.Abstain, proposal, [], recoveryActions, ReplyProvenance.NoModelCall([]), recoveryFollowUps), false, false);
        }

        // ADR-030 stage 3: a decline the model explains as an ambiguous question is kept (not
        // regenerated) so the interpretation menu below can offer the readings instead.
        bool KeepDecline(string? reason) =>
            interviewOptions.Enabled
            && interviewOptions.ConvertAmbiguousAbstain
            && !hints.SuppressInterview
            && !previousRaffaTurnWasInterview
            && AmbiguousAbstainDetector.IsAmbiguous(reason);

        var composed = await answerComposer.AnswerAsync(question, boundedPack, recentTurns, KeepDecline, cancellationToken)
            .ConfigureAwait(false);

        if (composed.IsFailure)
        {
            return (new CopilotReply(
                ReplyKind.Abstain,
                HelpfulFallbackAnswer.ServiceBusy(question),
                [], recoveryActions, ReplyProvenance.NoModelCall([]), recoveryFollowUps), false, false);
        }

        var result = composed.Value.Result;

        // ADR-030 stage 3: the model itself says the question was ambiguous -- offer the
        // interpretation menu (deterministic options, never the model's) instead of the abstain
        // block with its dead "Open Ask Raffa" button.
        if (!result.CanDetermine && !composed.Value.GuardIntervened && KeepDecline(result.AbstainReason))
        {
            var webOffer = await webResearchFlow.BuildOfferAsync(tenantId, question, cancellationToken).ConfigureAwait(false);
            var interview = interviewPlanner.PlanInterpretationMenu(question, plan, webOffer);
            if (interview is not null)
            {
                return (InterviewReplyBuilder.Interview(interview), false, false);
            }
        }

        // The composed-from-the-pack answer is only as relevant as the pack. A structured question
        // the deterministic planner could not handle gets a stand-in snapshot of the soonest-ending
        // contracts (no calc:structured-query item) -- quoting that back would answer a question
        // that was not asked (GAP-ASK-UNANSWERABLE-STILL-ANSWERED), so that one case stays an
        // honest abstain.
        if (composed.Value.FallbackUsed && plan.Intent == AskIntent.StructuredFact && namedContractItem is null &&
            !boundedPack.Any(item => item.CitationKey == InsightsCitationKeys.Calc("structured-query")))
        {
            return (
                new CopilotReply(
                    ReplyKind.Abstain,
                    proposal,
                    [],
                    recoveryActions,
                    new ReplyProvenance([], result.Metadata.ModelId, result.Metadata.PromptVersion, result.Metadata.InputHash),
                    recoveryFollowUps),
                true,
                false);
        }

        // Still a decline after the regenerate-once pass (or one kept for an interview that did
        // not materialise): never shown as "cannot determine" -- the user gets the proposal.
        if (!result.CanDetermine)
        {
            return (
                new CopilotReply(
                    ReplyKind.Abstain,
                    proposal,
                    [],
                    recoveryActions,
                    new ReplyProvenance([], result.Metadata.ModelId, result.Metadata.PromptVersion, result.Metadata.InputHash),
                    recoveryFollowUps),
                composed.Value.GuardIntervened,
                composed.Value.FallbackUsed);
        }

        // Model action keys were already normalized to catalog keys by AnswerComposer
        // (ActionKeyNormalizer); a key whose route needs an id this turn does not carry (Contract 360
        // on a portfolio-wide turn) is dropped here rather than failing CapabilityRouting.ForKey.
        var actionKeys = ActionKeyNormalizer.Routable(result.ActionKeys, routingContext);
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

        // A savings answer always leads somewhere: the Savings opportunities it is about and the
        // Renewals deadlines that time them, first (the first action is the primary button) --
        // server-resolved from the catalog, never from the model's own keys, merged with them the
        // same Distinct() way as the Q3 injection above.
        if (result.CanDetermine && IntentPolicy.InjectsSavingsActions(plan.Intent))
        {
            var injectedSavingsActions = capabilityRouting.ResolveActions(
                [CapabilityIntent.Savings, CapabilityIntent.Deadline], routingContext);
            resolvedActions = injectedSavingsActions.Concat(resolvedActions).Distinct().ToList();
        }

        return (
            CopilotReplyBuilder.FromGuardedResult(result, boundedPack, resolvedActions, recoveryActions, recoveryFollowUps),
            composed.Value.GuardIntervened,
            composed.Value.FallbackUsed);
    }
}
