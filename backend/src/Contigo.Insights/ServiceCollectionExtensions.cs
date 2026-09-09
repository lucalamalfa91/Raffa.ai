using Contigo.Insights.Criticality;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Contigo.Insights;

/// <summary>
/// Composition-root wiring for the Insights module (ADR-002: "each module exposes an
/// AddXxx(IServiceCollection) extension method"; ADR-024 module-map delta —
/// <c>Contigo.Insights</c> → <c>[SharedKernel, Benchmark]</c>, pure calculators fed by
/// DTOs: the deterministic renewal-strategy pack (gap G-STRATEGY) and the portfolio
/// criticality score (gap G-CRITICALITY) — <c>reports/audit/ask-v2-gaps.md</c>).
///
/// Task E13/F01/US01/T01 (v2-scaffold) created this project and its allow-listed
/// references so the later e13 tasks that own those calculators land in an existing
/// module instead of also touching <c>Contigo.slnx</c> and the architecture allow-list.
///
/// <para>
/// Task E13/F07/US01/T01 (insights-calculators) is that first real caller: registers
/// <see cref="InsightsOptions"/> (config section <c>Insights:Criticality</c>, the same "bind
/// lazily from IConfiguration, property initializers supply the council-decided default" pattern
/// <c>Contigo.Renewals.Configuration.PriorityScoreWeightsOptions</c>'s own registration already
/// uses) and <see cref="CriticalityScoreCalculator"/> (which resolves that options singleton as a
/// constructor dependency). <c>Contigo.Insights.Negotiation.PricedLineNegotiationCalculator</c> and
/// <c>Contigo.Insights.Strategy.StrategyPackBuilder</c> are static classes with no configuration or
/// other dependency (mirrors <c>Contigo.Quotes.Application.Strategy.NegotiationStrategyCalculator</c>'s
/// own "pure calculator, no DI registration needed" shape) — nothing to register here for either.
/// No host calls <see cref="AddInsightsModule"/> yet: <c>Contigo.Api.InsightsEndpointExtensions</c>
/// (this same task) is a new, unmapped composition file — <c>Program.cs</c> wiring is a later
/// phase's task (F06/T01), the same "wiring lands with the first real caller" sequencing this
/// codebase's other modules already follow.
/// </para>
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Insights module's services into <paramref name="services"/> —
    /// <see cref="InsightsOptions"/> and <see cref="CriticalityScoreCalculator"/> (task
    /// E13/F07/US01/T01). Gives hosts and later e13 tasks a stable, already-wired call site —
    /// mirrors <c>Contigo.Renewals.Infrastructure.ServiceCollectionExtensions.AddRenewalsModule</c>.
    /// </summary>
    public static IServiceCollection AddInsightsModule(this IServiceCollection services)
    {
        // Task E13/F07/US01/T01: same "bind lazily from IConfiguration, property initializers
        // supply the council-decided default" pattern PriorityScoreWeightsOptions's own
        // registration already uses — every property here is a scalar decimal, so a plain
        // `new InsightsOptions()` + `section.Bind(options)` is safe as-is (no array-merge footgun).
        // Singleton: immutable after construction, shared by every CriticalityScoreCalculator
        // instance across every scope, same lifetime PriorityScoreWeightsOptions itself uses.
        services.TryAddSingleton(sp =>
        {
            var configuration = sp.GetRequiredService<IConfiguration>();
            var options = new InsightsOptions();
            configuration.GetSection(InsightsOptions.SectionName).Bind(options);
            return options;
        });

        // CriticalityScoreCalculator has no *required* constructor dependency (its one constructor
        // parameter defaults to null, resolved to the council-decided InsightsOptions internally —
        // see that class's own doc comment) — but the container always finds the InsightsOptions
        // singleton just registered above and injects it, so a real host's calculator is always the
        // configured one. Scoped, the same uniform per-request/job lifetime every other module's own
        // AddXxxModule already picks for a stateless calculator (see
        // Contigo.Renewals.Infrastructure.ServiceCollectionExtensions's own doc comment on this exact
        // choice).
        services.AddScoped<CriticalityScoreCalculator>();

        return services;
    }
}
