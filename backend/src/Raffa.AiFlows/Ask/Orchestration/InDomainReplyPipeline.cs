using Raffa.AiFlows.Ask.Handlers;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Packs;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.WebResearch.Orchestration;
using Raffa.Chat.Application.Interview;
using Raffa.Chat.Application.Reply;
using Raffa.Chat.Domain;

namespace Raffa.AiFlows.Ask.Orchestration;

/// <summary>
/// The in-domain turn (R-ASK-03...08): planner, the pre-interview short-circuits, the interview,
/// the intent's handler, the agent phase and the answer phase. The former
/// <c>AskCopilotService.BuildInDomainReplyAsync</c>, with its <c>plan.Intent</c> switch and the
/// three intent-keyed short-circuits behind <see cref="IIntentHandler"/> (F1 Ask split, PR-5).
/// Returns the reply alongside whether the composer's guard pipeline intervened and whether the
/// fallback answer was used, exactly as the former method did.
/// </summary>
public sealed class InDomainReplyPipeline(
    InDomainTurnPlanner planner,
    IntentHandlerRegistry handlers,
    InterviewPlanner interviewPlanner,
    InterviewOptions interviewOptions,
    WebResearchFlow webResearchFlow,
    AgentPhase agentPhase,
    AnswerPhase answerPhase)
{
    internal async Task<(CopilotReply Reply, bool GuardIntervened, bool FallbackUsed)> BuildAsync(
        AskTurnScope scope,
        InDomainTurnTrace? trace,
        CancellationToken cancellationToken)
    {
        var turn = planner.Plan(scope, trace, out var refusal);
        if (turn is null)
        {
            return (refusal!, false, false);
        }

        var plan = turn.Plan;
        var hints = scope.Hints;

        // Web research and navigate/quote-route end the turn before the interview.
        if (IntentPolicy.IsPreInterviewShortCircuit(plan.Intent))
        {
            var early = await handlers.For(plan.Intent).HandleAsync(turn, cancellationToken).ConfigureAwait(false);
            if (early is IntentOutcome.Reply earlyReply)
            {
                return (earlyReply.Value, earlyReply.GuardIntervened, earlyReply.FallbackUsed);
            }
        }

        // ADR-030 — the interview. Decided here, after scope and supplier resolution and before
        // any pack is built, so an ambiguous turn retrieves nothing and calls no model. An answer
        // to an interview (hints.SuppressInterview) never triggers a second one, and neither does
        // a turn that directly follows one.
        if (interviewOptions.Enabled && !hints.SuppressInterview)
        {
            var interviewContext = new InterviewContext(
                HasScope: scope.ScopeContractId is not null || turn.ForcedContractItem is not null,
                NamedSupplierContractCount: turn.SupplierMatches.Count,
                PortfolioIsEmpty: scope.Portfolio.Items.Count == 0,
                PreviousRaffaTurnWasInterview: scope.PreviousRaffaTurnWasInterview,
                IsNoticeQuestion: IntentPlanner.IsNoticeQuestion(scope.Question));
            var signals = AmbiguityDetector.Detect(scope.Question, plan, interviewContext, interviewOptions);
            if (signals.Verdict == AmbiguityVerdict.Ambiguous)
            {
                // The web option rides the interpretation menu only when every ADR-030 gate is
                // open and the question is a procurement topic; picking it asks consent, never
                // searches.
                var webOffer = await webResearchFlow.BuildOfferAsync(scope.TenantId, scope.Question, cancellationToken).ConfigureAwait(false);
                var interview = interviewPlanner.Plan(
                    scope.Question, plan, signals, InterviewInputsFactory.BuildInterviewInputs(turn.SupplierMatches, scope.Portfolio, scope.SupplierNames), webOffer);
                if (interview is not null)
                {
                    return (InterviewReplyBuilder.Interview(interview), false, false);
                }
            }
        }

        // The intent's own pack, or a reply that needs no pack (the notice fallbacks, NW-94).
        var outcome = await handlers.For(plan.Intent).HandleAsync(turn, cancellationToken).ConfigureAwait(false);
        if (outcome is IntentOutcome.Reply reply)
        {
            return (reply.Value, reply.GuardIntervened, reply.FallbackUsed);
        }

        var packItems = ((IntentOutcome.Pack)outcome).Items;

        // NW-80 "never silently merge": prepended, not appended, so PackBudget.Apply (which always
        // keeps at least its first item) and FixtureAiGateway.AnswerFromPack (which cites only the
        // first few pack items) can never drop the one sentence telling the user which contract was
        // chosen among several.
        if (turn.DisambiguationItem is not null)
        {
            packItems = packItems.Prepend(turn.DisambiguationItem).ToList();
        }

        var agents = await agentPhase.RunAsync(
                scope.Question,
                plan.Goal,
                packItems,
                new AgentPhaseRequest(
                    IntentPolicy.SkipsDataCheck(plan.Intent) ? []
                    : turn.NamedContractItem is not null ? [turn.NamedContractItem]
                    : MarketDataCheck.ContractsForDataCheck(packItems, scope.Portfolio),
                    IntentPolicy.RunsMarketResearch(plan.Intent, scope.Portfolio),
                    IntentPolicy.IsCouncilIntent(plan.Intent, turn.NamedContractItem),
                    AppendCalendarItem: true),
                cancellationToken)
            .ConfigureAwait(false);

        return await answerPhase.ComposeAsync(turn, agents, cancellationToken).ConfigureAwait(false);
    }
}
