using Raffa.Benchmark.Adapters;
using Raffa.Benchmark.Configuration;
using Microsoft.Extensions.Configuration;
using Raffa.Benchmark.Fixtures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Benchmark;

/// <summary>
/// Composition-root wiring for the Benchmark Service module (ADR-002: "each module exposes an
/// AddXxx(IServiceCollection) extension method" — mirrors
/// <c>Raffa.AiGateway.ServiceCollectionExtensions.AddAiGatewayModule</c>). Task E04/F01/US01/T02
/// ("adapter registry") is the first caller that needs <see cref="IBenchmarkService"/> resolvable
/// from a DI container — task E04/F01/US01/T01 defined the interface with no implementation or
/// registration at all. Registers <see cref="BenchmarkAdapterRegistry"/> as
/// <see cref="IBenchmarkService"/> and binds <see cref="BenchmarkAdapterOptions"/> from
/// configuration.
///
/// Task E04/F01/US02/T01 (story us-02-fixture-adapter) added the first concrete adapter,
/// <see cref="FixtureBenchmarkAdapter"/> — but that task's own wave-spec phase ran in parallel with
/// this registry (neither depends on the other), so it could not register the adapter it had just
/// written here; the class existed and was directly unit-testable, but unreachable through this
/// module's own public entry point. Task E04/F01/US02/T02 (fixture-confidence) closes that gap: now
/// that <see cref="FixtureBenchmarkAdapter"/> implements <see cref="IBenchmarkProviderAdapter"/> (see
/// that class's own doc comment), this method registers it into
/// <see cref="BenchmarkAdapterRegistry"/>'s adapter collection, so a host that calls
/// <see cref="AddBenchmarkModule"/> gets a real, resolvable <see cref="IBenchmarkService"/> whose
/// default configuration (<see cref="BenchmarkAdapterOptions.DefaultAdapterName"/>) actually returns
/// fixture-backed results — while an unrecognized configured adapter name still fails honestly rather
/// than fabricating one (the same "resolvable but answers honestly" contract
/// <see cref="BenchmarkAdapterRegistry"/>'s own doc comment describes).
///
/// No host calls <see cref="AddBenchmarkModule"/> yet: <c>Raffa.Savings</c> — the first intended
/// consumer per <c>Raffa.ArchitectureTests.DependencyDirectionTests.AllowedReferences</c>' allowed-
/// reference map (Renewals, Savings and Quotes may all reference <see cref="Raffa.Benchmark"/>) —
/// has no DI wiring of its own yet in this wave. Same "wiring lands with the first real caller"
/// sequencing <c>Raffa.Chat.Infrastructure.ServiceCollectionExtensions</c>'s own doc comment
/// describes for <c>AddChatModule</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBenchmarkModule(this IServiceCollection services)
    {
        // TryAdd: any module (or the host) may call this defensively; only the first registration
        // wins (mirrors Raffa.AiGateway.ServiceCollectionExtensions.AddAiGatewayModule).
        services.TryAddSingleton(sp =>
        {
            var options = new BenchmarkAdapterOptions();
            sp.GetRequiredService<IConfiguration>()
                .GetSection(BenchmarkAdapterOptions.SectionName)
                .Bind(options);
            return options;
        });

        // IEnumerable<IBenchmarkProviderAdapter> resolves to an empty sequence when nothing has
        // registered one yet (standard Microsoft.Extensions.DependencyInjection behaviour for an
        // unregistered IEnumerable<T> — never throws), so BenchmarkAdapterRegistry itself always
        // constructs successfully even before any adapter exists.
        services.TryAddSingleton<IBenchmarkService, BenchmarkAdapterRegistry>();

        // Task E04/F01/US02/T02: FixtureBenchmarkAdapter is an IBenchmarkProviderAdapter (named
        // "fixture", matching BenchmarkAdapterOptions.DefaultAdapterName), registered into the same
        // enumerable BenchmarkAdapterRegistry's own constructor consumes. TryAddEnumerable — not
        // TryAddSingleton<IBenchmarkService, _>, which would silently no-op here: IServiceCollection
        // .TryAdd only checks whether a registration for the *service type* already exists, and
        // IBenchmarkService already got one two lines above, regardless of implementation type. ADR-001:
        // the fixture adapter is the only Benchmark Service implementation for the first `demo` — never
        // a paid external API. Swapping in a later, council-justified provider-backed adapter is a
        // second registration under its own Name plus a BenchmarkAdapterOptions.ActiveAdapter config
        // change; domain code depends on IBenchmarkService only and never sees this registration
        // (us-01-benchmark-interface AC-2).
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBenchmarkProviderAdapter, FixtureBenchmarkAdapter>());

        return services;
    }
}
