using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Raffa.AiFlows.Ask.Answering;
using Raffa.AiFlows.Ask.Gate;
using Raffa.AiFlows.Ask.Interview;
using Raffa.AiFlows.Ask.Planning;
using Raffa.AiFlows.Ask.Routing;

namespace Raffa.AiFlows.Ask;

/// <summary>
/// Registrations of the Ask flow (F1): the query router and deterministic queries, the V2
/// gate/planner/answer engine and the interview planner. Called by
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

        // The V2 gate/planner/answer engine. Each of these is stateless (no database, no
        // per-request field).
        services.AddScoped<DomainGate>();
        services.AddScoped<IntentPlanner>();
        services.AddScoped<AnswerComposer>();

        // ADR-030: the interview (kill switch + bounds).
        services.TryAddSingleton(new InterviewOptions());
        services.AddScoped<InterviewPlanner>();

        return services;
    }
}
