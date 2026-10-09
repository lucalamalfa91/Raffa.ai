using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.AiFlows.Ask.Answering;
using Raffa.AiFlows.Ask.Gate;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.Ask.Routing;
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

        return services;
    }
}
