using Contigo.Benchmark.Adapters;
using Contigo.Benchmark.Configuration;
using Contigo.Market.Benchmark;
using Contigo.Market.Mock;
using Contigo.Market.Retrieval;
using Contigo.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Contigo.Market;

/// <summary>
/// Composition-root wiring for the Market module (ADR-002: "each module exposes an
/// AddXxx(IServiceCollection) extension method"; ADR-024 module-map delta —
/// <c>Contigo.Market</c> → <c>[SharedKernel, AiGateway, Benchmark]</c>).
///
/// Task E13/F01/US01/T01 (v2-scaffold) created this project with an empty
/// <see cref="AddMarketModule"/>. This task (E13/F02/US01/T01) is the first to register
/// anything: the mock feed (<see cref="IMarketIntelligenceProvider"/> →
/// <see cref="MockMarketIntelligenceProvider"/>), its benchmark projection
/// (<see cref="MarketFeedBenchmarkAdapter"/>, registered into the same
/// <c>IBenchmarkProviderAdapter</c> enumerable <c>Contigo.Benchmark.BenchmarkAdapterRegistry</c>
/// resolves), and its in-memory notes retrieval (<see cref="IMarketKnowledgeRetrieval"/> →
/// <see cref="InMemoryMarketKnowledgeRetrieval"/>). No host calls <see cref="AddMarketModule"/>
/// yet — task F06/T01 is expected to be the first caller, the same "wiring lands with the first
/// real caller" sequencing this codebase already uses for
/// <c>Contigo.Benchmark.ServiceCollectionExtensions.AddBenchmarkModule</c> and
/// <c>Contigo.Chat.Infrastructure.ServiceCollectionExtensions.AddChatModule</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Market module's services into <paramref name="services"/>.
    /// </summary>
    public static IServiceCollection AddMarketModule(this IServiceCollection services)
    {
        // TryAdd: any module (or the host) may call this defensively; only the first registration
        // wins (mirrors Contigo.Benchmark.ServiceCollectionExtensions.AddBenchmarkModule).
        services.TryAddSingleton<IClock, SystemClock>();

        services.TryAddSingleton<IMarketIntelligenceProvider, MockMarketIntelligenceProvider>();
        services.TryAddSingleton<IMarketKnowledgeRetrieval, InMemoryMarketKnowledgeRetrieval>();

        // Registered into the same enumerable Contigo.Benchmark.BenchmarkAdapterRegistry's own
        // constructor consumes (TryAddEnumerable — see that type's own doc comment for why not
        // TryAddSingleton<IBenchmarkProviderAdapter, _>, which would silently no-op:
        // Contigo.Benchmark.Fixtures.FixtureBenchmarkAdapter already took that one registration
        // slot for IBenchmarkService, not this one, but IServiceCollection.TryAdd only checks the
        // *service type*, so a second TryAddSingleton<IBenchmarkProviderAdapter, _> would already
        // be occupied the moment more than one adapter exists in the container).
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IBenchmarkProviderAdapter, MarketFeedBenchmarkAdapter>());

        MakeMarketFeedTheDefaultActiveAdapter(services);

        return services;
    }

    /// <summary>
    /// R-MKT-02: the mock feed "replaces the eight <c>FixtureBenchmarkAdapter</c> rows as the
    /// default provider" — <see cref="BenchmarkAdapterOptions.ActiveAdapter"/> must default to
    /// <see cref="MarketFeedBenchmarkAdapter.AdapterName"/> ("market-feed"), not
    /// <see cref="BenchmarkAdapterOptions.DefaultAdapterName"/> ("fixture"), <em>without editing
    /// <c>Contigo.Benchmark</c></em> (that module stays free of any Market-specific knowledge —
    /// ADR-002's dependency direction runs the other way) and regardless of whether a host calls
    /// <c>AddBenchmarkModule()</c> or <see cref="AddMarketModule"/> first.
    ///
    /// <b>Why not <c>IServiceCollection.PostConfigure&lt;BenchmarkAdapterOptions&gt;</c>:</b> that
    /// is the idiomatic, order-independent way to override a default in this exact situation — but
    /// only when the target is resolved through the <c>Microsoft.Extensions.Options</c>
    /// <c>IOptions&lt;T&gt;</c> indirection. <c>Contigo.Benchmark.ServiceCollectionExtensions
    /// .AddBenchmarkModule</c> does not use it: it registers <see cref="BenchmarkAdapterOptions"/>
    /// as a plain singleton, built once by a factory that constructs a new instance (defaulting
    /// <c>ActiveAdapter</c> to <c>"fixture"</c>) and calls <c>IConfiguration.Bind</c> over it —
    /// <c>Contigo.Benchmark.BenchmarkAdapterRegistry</c>'s constructor takes the concrete
    /// <see cref="BenchmarkAdapterOptions"/> type directly, never <c>IOptions&lt;BenchmarkAdapterOptions&gt;</c>.
    /// Nothing in this composition root ever resolves <c>IOptions&lt;BenchmarkAdapterOptions&gt;</c>,
    /// so <c>PostConfigure</c> would register an <c>IPostConfigureOptions&lt;T&gt;</c> that nothing
    /// ever consumes — a silent no-op, not the override this method needs to actually take effect.
    ///
    /// <b>What actually works here, order-independent:</b> <see cref="ServiceCollectionDescriptorExtensions.Replace"/>
    /// unconditionally removes any existing <see cref="BenchmarkAdapterOptions"/> registration and
    /// installs this one — unlike <c>TryAddSingleton</c>, which only registers when the service
    /// type is not yet registered. The factory below is otherwise byte-for-byte identical to
    /// <c>AddBenchmarkModule</c>'s own (same configuration section, same <c>Bind</c> call), so a
    /// deployment can still override <c>ActiveAdapter</c> back with an explicit
    /// <c>"Benchmark:Adapter:ActiveAdapter"</c> configuration value — only the starting default
    /// differs. Whichever module's <c>AddXxxModule</c> the host calls second "wins" the
    /// registration slot, but the outcome is identical either way: if <c>AddBenchmarkModule</c>
    /// runs first, its own <c>TryAddSingleton</c> registers the "fixture"-defaulting factory, and
    /// this method's <c>Replace</c> then unconditionally overwrites it; if <see cref="AddMarketModule"/>
    /// runs first, this method's <c>Replace</c> installs the "market-feed"-defaulting factory, and
    /// <c>AddBenchmarkModule</c>'s later <c>TryAddSingleton</c> is a no-op because a registration
    /// already exists. Either order, "market-feed" ends up the resolved default.
    /// </summary>
    private static void MakeMarketFeedTheDefaultActiveAdapter(IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton(sp =>
        {
            var options = new BenchmarkAdapterOptions { ActiveAdapter = MarketFeedBenchmarkAdapter.AdapterName };
            sp.GetRequiredService<IConfiguration>()
                .GetSection(BenchmarkAdapterOptions.SectionName)
                .Bind(options);
            return options;
        }));
    }
}
