using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.AiFlows.Ask.Answering;
using Raffa.AiFlows.Ask.Gate;
using Raffa.AiFlows.Ask.Handlers;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Ask.Packs;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.Ask.Reply;
using Raffa.AiFlows.Ask.Routing;
using Raffa.AiFlows.Shared.ContractContext;
using Raffa.AiFlows.Shared.Guards;
using Raffa.AiFlows.Shared.Pack;
using Raffa.AiFlows.Shared.Routing;

namespace Raffa.AiFlows.Ask;

/// <summary>
/// Registrations of the Ask flow (F1): the query router and deterministic queries, the V2
/// gate/planner/answer engine, the interview planner and the shared guards, routing and pack
/// budget. Called by
/// <see cref="AiFlowsServiceCollectionExtensions.AddAiFlows"/>.
/// </summary>
internal static class AskFlowServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Ask flow's services, all Scoped: one uniform per-request/job lifetime, the safe
    /// one for services that depend on a Scoped <c>IAuditWriter</c>. The interview options use
    /// <c>TryAdd</c> (ADR-030) so a value a host registered before <c>AddAiFlows</c> wins.
    /// </summary>
    internal static IServiceCollection AddAskFlow(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<AskRaffaQueryRouter>();
        services.AddScoped<DeterministicQueryPlanner>();
        services.AddScoped<DeterministicQueryHandler>();

        services.AddScoped<AbstainGuard>();
        services.AddScoped<CapabilityRouting>();

        // The V2 gate/planner/answer engine. Each of these is stateless (no database, no
        // per-request field).
        services.AddScoped<DomainGate>();
        services.AddScoped<IntentPlanner>();
        services.AddScoped<AnswerComposer>();

        // ADR-030: the interview (kill switch + bounds).
        services.TryAddSingleton(new InterviewOptions());
        services.AddScoped<InterviewPlanner>();

        // TryAdd: an always-usable default (PackBudget.DefaultMaxTokens) with no IConfiguration
        // dependency at all. Raffa.Api.Program registers the configuration-bound PackBudget
        // (Chat:PackTokenBudget) *before* calling AddAiFlows when a value is present:
        // TryAddSingleton's "first registration wins" then makes the configured value the one that
        // actually resolves, this default only when no configuration overrides it.
        services.TryAddSingleton(new PackBudget());

        // F1 Ask turn options (Chat:AskTurn): every default reproduces today. The host binds the
        // section before AddAiFlows; TryAdd keeps that value.
        services.TryAddSingleton(new AskTurnOptions());

        // The Ask pack-composition root: one Scoped instance per request/job shares that scope's
        // own DbContext-backed services instead of a second, independently-tracked copy.
        services.AddScoped<AskCopilotService>();
        services.AddScoped<GateReplyBuilder>();
        services.AddScoped<AbstainRecovery>();
        services.AddScoped<AskTurnAudit>();
        services.AddScoped<CapabilityCheckCoordinator>();
        services.AddScoped<NoticeFallbackReplies>();

        // Pack builders and the per-turn contract reads they share (F1 Ask split, PR-2).
        services.AddScoped<ContractContextReader>();
        services.AddScoped<StructuredFactPackBuilder>();
        services.AddScoped<ClausePackBuilder>();
        services.AddScoped<MarketComparePackBuilder>();
        services.AddScoped<NegotiationPointsPackBuilder>();
        services.AddScoped<RenewalStrategyPackBuilder>();
        services.AddScoped<PortfolioStrategyPackBuilder>();
        services.AddScoped<SavingsPackBuilder>();
        services.AddScoped<MarketDataCheck>();

        // Pipeline phases and the capability-gap turn (F1 Ask split, PR-4).
        services.AddScoped<InDomainTurnPlanner>();
        services.AddScoped<AgentPhase>();
        services.AddScoped<AnswerPhase>();
        services.AddScoped<CapabilityGapTurnHandler>();

        // One handler per intent (F1-T08). TryAddEnumerable: a host calling AddAiFlows twice does not
        // register a handler twice (the registry would refuse the duplicate intent).
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntentHandler, StructuredFactHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntentHandler, ClauseHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntentHandler, MarketCompareHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntentHandler, RenewalStrategyHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntentHandler, PortfolioStrategyHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntentHandler, PortfolioSavingsTargetHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntentHandler, SavingsHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntentHandler, DocumentStatusHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntentHandler, NavigateHandler>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IIntentHandler, WebResearchHandler>());
        services.AddScoped<DefaultIntentHandler>();
        services.AddScoped<IntentHandlerRegistry>();
        services.AddScoped<InDomainReplyPipeline>();

        // The (supplier name, geography) key a BenchmarkQuery requires: resolved once per request
        // and shared by Ask's priced-line bands and the renewals/strategy endpoints. Scoped: reads
        // IdentityWorkspaceDbContext (Scoped) and ISupplierNameLookup (Singleton).
        services.AddScoped<BenchmarkKeyResolution>();

        return services;
    }
}
