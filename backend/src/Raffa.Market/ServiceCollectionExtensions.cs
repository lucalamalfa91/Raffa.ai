using Raffa.AiGateway;
using Raffa.Benchmark.Adapters;
using Raffa.Benchmark.Configuration;
using Raffa.Market.Benchmark;
using Raffa.Market.Infrastructure;
using Raffa.Market.Ingestion;
using Raffa.Market.Mock;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Market;

/// <summary>
/// Composition-root wiring for the Market module (ADR-002: "each module exposes an
/// AddXxx(IServiceCollection) extension method"; ADR-024 module-map delta —
/// <c>Raffa.Market</c> → <c>[SharedKernel, AiGateway, Benchmark]</c>).
///
/// Task E13/F01/US01/T01 (v2-scaffold) created this project with an empty
/// <see cref="AddMarketModule"/>. Task E13/F02/US01/T01 was the first to register anything: the
/// mock feed (<see cref="IMarketIntelligenceProvider"/> → <see cref="MockMarketIntelligenceProvider"/>),
/// its benchmark projection (<see cref="MarketFeedBenchmarkAdapter"/>, registered into the same
/// <c>IBenchmarkProviderAdapter</c> enumerable <c>Raffa.Benchmark.BenchmarkAdapterRegistry</c>
/// resolves), and its in-memory notes retrieval (<see cref="IMarketKnowledgeRetrieval"/> →
/// <see cref="InMemoryMarketKnowledgeRetrieval"/>) — all still registered, unconditionally, when
/// <paramref name="marketConnectionString"/> below is <see langword="null"/>.
///
/// Task E13/F02/US01/T02 (market-index) adds <paramref name="marketConnectionString"/> (task
/// objective: "DI swap inside <c>AddMarketModule(string? marketConnectionString)</c>"), the same
/// "optional trailing connection-string parameter, called with none by every existing test/caller"
/// shape <c>Raffa.Chat.Infrastructure.ServiceCollectionExtensions.AddChatModule</c> already
/// established for the identical reason: this module already has real, non-database callers (the
/// mock-feed/in-memory-retrieval registrations above) that must keep resolving with zero
/// configuration. Called with a connection string, this method instead registers
/// <see cref="MarketDbContext"/> and swaps in the DB-backed
/// <see cref="Retrieval.PgVectorMarketKnowledgeRetrieval"/> / DB-backed
/// <see cref="MarketFeedBenchmarkAdapter"/> constructor — a branch, not a <c>Replace</c> override,
/// so the "market-feed" <c>IBenchmarkProviderAdapter</c> slot is claimed by exactly one concrete
/// data source per call, never both (a second, unconditional <c>TryAddEnumerable</c> registration
/// under the same interface would make <c>Raffa.Benchmark.BenchmarkAdapterRegistry</c>'s own
/// constructor throw "Duplicate benchmark provider adapter name"). No host calls
/// <see cref="AddMarketModule"/> yet — task F06/T01 is expected to be the first caller, the same
/// "wiring lands with the first real caller" sequencing this codebase already uses for
/// <c>Raffa.Benchmark.ServiceCollectionExtensions.AddBenchmarkModule</c> and
/// <c>Raffa.Chat.Infrastructure.ServiceCollectionExtensions.AddChatModule</c>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Market module's services into <paramref name="services"/>. Called with
    /// <paramref name="marketConnectionString"/> <see langword="null"/> (every caller before task
    /// E13/F02/US01/T02, and every test that does not exercise the persisted store), registers
    /// exactly what it always has — the mock feed, in-memory notes retrieval, and the
    /// provider-backed benchmark adapter. Called with a real connection string, additionally wires
    /// <see cref="MarketDbContext"/> and swaps the notes-retrieval and benchmark-adapter
    /// registrations for their DB-backed equivalents (see the type doc comment).
    /// </summary>
    public static IServiceCollection AddMarketModule(
        this IServiceCollection services, string? marketConnectionString = null)
    {
        // TryAdd: any module (or the host) may call this defensively; only the first registration
        // wins (mirrors Raffa.Benchmark.ServiceCollectionExtensions.AddBenchmarkModule).
        services.TryAddSingleton<IClock, SystemClock>();

        services.TryAddSingleton<IMarketIntelligenceProvider, MockMarketIntelligenceProvider>();

        if (marketConnectionString is not null)
        {
            // Same defensive TryAdd every module that ends up needing the ambient tenant claim
            // already uses -- MarketIngestionService/PgVectorMarketKnowledgeRetrieval's own
            // "system tenant" AI Gateway logging scope is this module's first consumer of it (see
            // MarketIngestionService.SystemTenantId's own doc comment). Never wired into
            // MarketDbContext itself -- see that type's own doc comment for why.
            services.TryAddSingleton<ITenantContext, TenantContext>();

            // This module's own IAiGateway/AiGatewayModelOptions wiring -- only needed once real
            // ingestion/DB-backed retrieval can actually call IAiGateway.EmbedAsync, so this call
            // lives inside this branch rather than unconditionally at the top of this method (the
            // in-memory T01 path above never touches IAiGateway at all). Idempotent (TryAdd
            // throughout its own body), so calling it here is safe even when some other module in
            // the same host (for example Raffa.Documents.Contracts) already called it first.
            services.AddAiGatewayModule();

            // One registration, two shapes -- and the order matters.
            //
            // AddDbContextFactory registers BOTH the Singleton IDbContextFactory<MarketDbContext>
            // (which the Singleton DB-backed benchmark adapter below needs -- see that adapter's
            // own doc comment for why its lifetime is forced) AND a Singleton
            // DbContextOptions<MarketDbContext>. The Scoped MarketDbContext that
            // MarketIngestionService/MarketRecordQueryService take directly is then created from
            // that same factory.
            //
            // Calling AddDbContext as well would overwrite those options with a *Scoped*
            // registration, which the Singleton factory then consumes -- and the DI scope
            // validator refuses it: any ASP.NET Core host in Development (ValidateScopes on) fails
            // to start with "Cannot consume scoped service DbContextOptions<MarketDbContext> from
            // singleton IDbContextFactory<MarketDbContext>". That is exactly what happened the
            // first time Raffa.Api was given ConnectionStrings:Market: the API would not boot at
            // all. Every Market test constructs its own options directly, so only running the real
            // host surfaced it.
            services.AddDbContextFactory<MarketDbContext>(options =>
                MarketDbContextOptions.Configure(options, marketConnectionString));

            services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<MarketDbContext>>().CreateDbContext());

            // Scoped, not Singleton -- unlike the DB-backed IBenchmarkProviderAdapter below, this
            // type also depends on the Scoped IAiGateway (to embed the search query); see its own
            // doc comment for why nothing forces IMarketKnowledgeRetrieval to stay Singleton the
            // way BenchmarkAdapterRegistry forces IBenchmarkProviderAdapter to.
            services.Replace(ServiceDescriptor.Scoped<IMarketKnowledgeRetrieval, PgVectorMarketKnowledgeRetrieval>());

            // Two generic arguments, not one: TryAddEnumerable(ServiceDescriptor.Singleton<TService>(factory))
            // -- a factory registration with no distinct TImplementation -- unconditionally throws
            // "Implementation type cannot be 'X' because it is indistinguishable from other services
            // registered for 'X'" (Microsoft.Extensions.DependencyInjection's own defensive check: a
            // factory-only descriptor's ImplementationType equals its ServiceType, and TryAddEnumerable
            // requires a distinct one to de-duplicate against). Naming MarketFeedBenchmarkAdapter as the
            // second type argument gives the descriptor its own, distinct ImplementationType, the same
            // fix the else branch's own two-argument ServiceDescriptor.Singleton<IBenchmarkProviderAdapter,
            // MarketFeedBenchmarkAdapter>() below already gets for free (no factory needed there).
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IBenchmarkProviderAdapter, MarketFeedBenchmarkAdapter>(sp =>
                new MarketFeedBenchmarkAdapter(
                    sp.GetRequiredService<IDbContextFactory<MarketDbContext>>(),
                    sp.GetRequiredService<IClock>())));

            services.AddScoped<MarketIngestionService>();
            services.AddScoped<MarketRecordQueryService>();
        }
        else
        {
            services.TryAddSingleton<IMarketKnowledgeRetrieval, InMemoryMarketKnowledgeRetrieval>();

            // Registered into the same enumerable Raffa.Benchmark.BenchmarkAdapterRegistry's own
            // constructor consumes (TryAddEnumerable — see that type's own doc comment for why not
            // TryAddSingleton<IBenchmarkProviderAdapter, _>, which would silently no-op:
            // Raffa.Benchmark.Fixtures.FixtureBenchmarkAdapter already took that one registration
            // slot for IBenchmarkService, not this one, but IServiceCollection.TryAdd only checks
            // the *service type*, so a second TryAddSingleton<IBenchmarkProviderAdapter, _> would
            // already be occupied the moment more than one adapter exists in the container).
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IBenchmarkProviderAdapter, MarketFeedBenchmarkAdapter>());
        }

        MakeMarketFeedTheDefaultActiveAdapter(services);

        return services;
    }

    /// <summary>
    /// R-MKT-02: the mock feed "replaces the eight <c>FixtureBenchmarkAdapter</c> rows as the
    /// default provider" — <see cref="BenchmarkAdapterOptions.ActiveAdapter"/> must default to
    /// <see cref="MarketFeedBenchmarkAdapter.AdapterName"/> ("market-feed"), not
    /// <see cref="BenchmarkAdapterOptions.DefaultAdapterName"/> ("fixture"), <em>without editing
    /// <c>Raffa.Benchmark</c></em> (that module stays free of any Market-specific knowledge —
    /// ADR-002's dependency direction runs the other way) and regardless of whether a host calls
    /// <c>AddBenchmarkModule()</c> or <see cref="AddMarketModule"/> first.
    ///
    /// <b>Why not <c>IServiceCollection.PostConfigure&lt;BenchmarkAdapterOptions&gt;</c>:</b> that
    /// is the idiomatic, order-independent way to override a default in this exact situation — but
    /// only when the target is resolved through the <c>Microsoft.Extensions.Options</c>
    /// <c>IOptions&lt;T&gt;</c> indirection. <c>Raffa.Benchmark.ServiceCollectionExtensions
    /// .AddBenchmarkModule</c> does not use it: it registers <see cref="BenchmarkAdapterOptions"/>
    /// as a plain singleton, built once by a factory that constructs a new instance (defaulting
    /// <c>ActiveAdapter</c> to <c>"fixture"</c>) and calls <c>IConfiguration.Bind</c> over it —
    /// <c>Raffa.Benchmark.BenchmarkAdapterRegistry</c>'s constructor takes the concrete
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
