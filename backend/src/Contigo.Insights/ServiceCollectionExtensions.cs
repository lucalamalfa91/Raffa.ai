using Microsoft.Extensions.DependencyInjection;

namespace Contigo.Insights;

/// <summary>
/// Composition-root wiring for the Insights module (ADR-002: "each module exposes an
/// AddXxx(IServiceCollection) extension method"; ADR-024 module-map delta —
/// <c>Contigo.Insights</c> → <c>[SharedKernel, Benchmark]</c>, pure calculators fed by
/// DTOs: the deterministic renewal-strategy pack (gap G-STRATEGY) and the portfolio
/// criticality score (gap G-CRITICALITY) — <c>reports/audit/ask-v2-gaps.md</c>).
///
/// Task E13/F01/US01/T01 (v2-scaffold) creates this project and its allow-listed
/// references so the later e13 tasks that own those calculators land in an existing
/// module instead of also touching <c>Contigo.slnx</c> and the architecture allow-list.
/// <see cref="AddInsightsModule"/> registers nothing yet — the same "wiring lands with the
/// first real caller" sequencing <c>Contigo.Benchmark.ServiceCollectionExtensions
/// .AddBenchmarkModule</c>'s own doc comment describes for <c>Contigo.Benchmark</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Insights module's services into <paramref name="services"/>. Registers
    /// nothing today (see the type-level doc comment) but gives hosts and later e13 tasks
    /// a stable, already-wired call site — mirrors <c>Contigo.AiGateway
    /// .ServiceCollectionExtensions.AddAiGatewayModule</c> / <c>Contigo.Benchmark
    /// .ServiceCollectionExtensions.AddBenchmarkModule</c>.
    /// </summary>
    public static IServiceCollection AddInsightsModule(this IServiceCollection services)
    {
        return services;
    }
}
