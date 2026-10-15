using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Ask.Packs;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.Ask.Reply;
using Raffa.AiFlows.WebResearch.Orchestration;
using Raffa.Chat.Domain;

namespace Raffa.AiFlows.Ask.Handlers;

// The switch arms of the former in-domain pipeline, one class per intent, bodies verbatim
// (F1 Ask split, PR-5). Each reads what it needs off the InDomainTurn.

internal sealed class StructuredFactHandler(StructuredFactPackBuilder builder, NoticeFallbackReplies notice) : IIntentHandler
{
    public IReadOnlyList<AskIntent> Intents { get; } = [AskIntent.StructuredFact];

    public async Task<IntentOutcome> HandleAsync(InDomainTurn turn, CancellationToken cancellationToken)
    {
        // Task E30/F02/US01/T01 (NW-94): the five notice fallbacks are fully server-decided, so a
        // notice question never reaches the composer or the AI gateway.
        if (IntentPlanner.IsNoticeQuestion(turn.Question))
        {
            return new IntentOutcome.Reply(
                await notice.BuildNoticeFallbackReplyAsync(
                        turn.Question, turn.NamedContractItem, turn.DisambiguationItem, turn.RoutingContext, cancellationToken)
                    .ConfigureAwait(false));
        }

        return new IntentOutcome.Pack(
            await builder.BuildStructuredFactPackAsync(
                    turn.Question, turn.NamedContractItem, turn.Scope.Portfolio, turn.Scope.SupplierNames, cancellationToken)
                .ConfigureAwait(false));
    }
}

internal sealed class ClauseHandler(ClausePackBuilder builder) : IIntentHandler
{
    public IReadOnlyList<AskIntent> Intents { get; } = [AskIntent.Clause];

    public async Task<IntentOutcome> HandleAsync(InDomainTurn turn, CancellationToken cancellationToken) =>
        new IntentOutcome.Pack(
            await builder.BuildClausePackAsync(turn.Scope.TenantId, turn.Question, turn.NamedContractItem, cancellationToken)
                .ConfigureAwait(false));
}

internal sealed class MarketCompareHandler(MarketComparePackBuilder builder) : IIntentHandler
{
    public IReadOnlyList<AskIntent> Intents { get; } = [AskIntent.MarketCompare];

    public async Task<IntentOutcome> HandleAsync(InDomainTurn turn, CancellationToken cancellationToken) =>
        new IntentOutcome.Pack(
            await builder.BuildMarketComparePackAsync(turn.NamedContractItem, cancellationToken).ConfigureAwait(false));
}

internal sealed class RenewalStrategyHandler(RenewalStrategyPackBuilder renewal, PortfolioStrategyPackBuilder portfolio) : IIntentHandler
{
    public IReadOnlyList<AskIntent> Intents { get; } = [AskIntent.RenewalStrategy];

    public async Task<IntentOutcome> HandleAsync(InDomainTurn turn, CancellationToken cancellationToken) =>
        new IntentOutcome.Pack(
            turn.NamedContractItem is not null
                ? await renewal.BuildRenewalStrategyWithEvidenceAsync(
                        turn.Scope.TenantId, turn.Question, turn.NamedContractItem, turn.Scope.Actor, cancellationToken, turn.Plan.Goal)
                    .ConfigureAwait(false)
                : await portfolio.BuildPortfolioStrategyPackAsync(turn.Scope.Portfolio, turn.Scope.SupplierNames, cancellationToken)
                    .ConfigureAwait(false));
}

internal sealed class PortfolioStrategyHandler(PortfolioStrategyPackBuilder builder) : IIntentHandler
{
    public IReadOnlyList<AskIntent> Intents { get; } = [AskIntent.PortfolioStrategy];

    public async Task<IntentOutcome> HandleAsync(InDomainTurn turn, CancellationToken cancellationToken) =>
        new IntentOutcome.Pack(
            await builder.BuildPortfolioStrategyPackAsync(turn.Scope.Portfolio, turn.Scope.SupplierNames, cancellationToken)
                .ConfigureAwait(false));
}

internal sealed class PortfolioSavingsTargetHandler(SavingsPackBuilder builder) : IIntentHandler
{
    public IReadOnlyList<AskIntent> Intents { get; } = [AskIntent.PortfolioSavingsTarget];

    public async Task<IntentOutcome> HandleAsync(InDomainTurn turn, CancellationToken cancellationToken) =>
        new IntentOutcome.Pack(
            await builder.BuildPortfolioSavingsTargetPackAsync(turn.Scope.Portfolio, turn.Plan.Goal, cancellationToken)
                .ConfigureAwait(false));
}

internal sealed class SavingsHandler(SavingsPackBuilder savings, PortfolioStrategyPackBuilder portfolio) : IIntentHandler
{
    public IReadOnlyList<AskIntent> Intents { get; } = [AskIntent.Savings];

    public async Task<IntentOutcome> HandleAsync(InDomainTurn turn, CancellationToken cancellationToken) =>
        new IntentOutcome.Pack(
            turn.NamedContractItem is not null
                ? await savings.BuildSavingsLeverPackAsync(
                        turn.Scope.TenantId, turn.NamedContractItem, turn.Plan.Goal, turn.Scope.Actor, cancellationToken)
                    .ConfigureAwait(false)
                : await portfolio.BuildPortfolioStrategyPackAsync(turn.Scope.Portfolio, turn.Scope.SupplierNames, cancellationToken)
                    .ConfigureAwait(false));
}

internal sealed class DocumentStatusHandler : IIntentHandler
{
    public IReadOnlyList<AskIntent> Intents { get; } = [AskIntent.DocumentStatus];

    public Task<IntentOutcome> HandleAsync(InDomainTurn turn, CancellationToken cancellationToken) =>
        Task.FromResult<IntentOutcome>(
            new IntentOutcome.Pack(DocumentStatusPack.Build(turn.Scope.Portfolio, turn.Scope.SupplierNames)));
}

internal sealed class NavigateHandler(GateReplyBuilder gateReplies) : IIntentHandler
{
    public IReadOnlyList<AskIntent> Intents { get; } = [AskIntent.Navigate, AskIntent.QuoteRoute];

    public Task<IntentOutcome> HandleAsync(InDomainTurn turn, CancellationToken cancellationToken) =>
        Task.FromResult<IntentOutcome>(
            new IntentOutcome.Reply(gateReplies.BuildRoutingOnlyReply(turn.Plan, turn.RoutingContext)));
}

internal sealed class WebResearchHandler(WebResearchFlow webResearchFlow, AbstainRecovery abstainRecovery) : IIntentHandler
{
    public IReadOnlyList<AskIntent> Intents { get; } = [AskIntent.WebResearch];

    public async Task<IntentOutcome> HandleAsync(InDomainTurn turn, CancellationToken cancellationToken)
    {
        var scope = turn.Scope;

        // ADR-030 -- web research. A consumed consent runs the research role here, before any pack
        // is built (nothing of the tenant's data is in scope of that call, by construction); an
        // explicit or forced WebResearch intent gets the consent question, or a redirect naming
        // the closed gate. There is no path from here to the search without a consumed consent.
        if (scope.Hints.AuthorizedWebResearch is { } authorizedWebResearch)
        {
            var reply = await webResearchFlow.RunAsync(
                    scope.TenantId, scope.Question, authorizedWebResearch, scope.Portfolio, turn.RoutingContext,
                    scope.Actor, abstainRecovery.Resolver, cancellationToken)
                .ConfigureAwait(false);
            return new IntentOutcome.Reply(reply.Reply, reply.GuardIntervened, false);
        }

        return new IntentOutcome.Reply(
            await webResearchFlow.BuildEntryReplyAsync(
                    scope.TenantId, scope.Question, scope.Portfolio, turn.RoutingContext,
                    scope.Actor, abstainRecovery.Resolver, cancellationToken)
                .ConfigureAwait(false));
    }
}
