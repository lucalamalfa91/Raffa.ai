using Microsoft.Extensions.DependencyInjection;

namespace Contigo.Market;

/// <summary>
/// Composition-root wiring for the Market module (ADR-002: "each module exposes an
/// AddXxx(IServiceCollection) extension method"; ADR-024 module-map delta —
/// <c>Contigo.Market</c> → <c>[SharedKernel, AiGateway, Benchmark]</c> for the mock
/// market-intelligence feed, its ingestion job, the shared read-only
/// <c>market_embedding</c> index, and the benchmark projection — gap G-MARKET-FEED /
/// G-MARKET-INDEX, <c>reports/audit/ask-v2-gaps.md</c>).
///
/// Task E13/F01/US01/T01 (v2-scaffold) creates this project and its allow-listed
/// references so the later e13 tasks that own those capabilities land in an existing
/// module instead of also touching <c>Contigo.slnx</c> and the architecture allow-list.
/// <see cref="AddMarketModule"/> registers nothing yet — the same "wiring lands with the
/// first real caller" sequencing <c>Contigo.Benchmark.ServiceCollectionExtensions
/// .AddBenchmarkModule</c>'s own doc comment describes for <c>Contigo.Benchmark</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Market module's services into <paramref name="services"/>. Registers
    /// nothing today (see the type-level doc comment) but gives hosts and later e13 tasks
    /// a stable, already-wired call site — mirrors <c>Contigo.AiGateway
    /// .ServiceCollectionExtensions.AddAiGatewayModule</c> / <c>Contigo.Benchmark
    /// .ServiceCollectionExtensions.AddBenchmarkModule</c>.
    /// </summary>
    public static IServiceCollection AddMarketModule(this IServiceCollection services)
    {
        return services;
    }
}
