using System.Collections.Concurrent;
using System.Data.Common;
using Raffa.Api.Infrastructure;
using Raffa.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Raffa.Api.Tests;

/// <summary>
/// Docker-free proof of <see cref="StartupWarmupService"/> (latency work, phase D): it builds the EF
/// models and resolves the key services in the background after start, never opens a database
/// connection, never throws or delays host start whatever fails, can be switched off, and steps
/// aside while a request is in flight. The Npgsql contexts point at a port nothing listens on, so
/// any accidental connection attempt would be both visible (the interceptor) and slow.
/// </summary>
public sealed class StartupWarmupTests
{
    private const string UnreachableConnection = "Host=127.0.0.1;Port=1;Database=warmup;Username=x;Password=x;Timeout=1";

    [Fact]
    public async Task Builds_every_registered_model_without_opening_a_connection()
    {
        var probe = new ConnectionProbe();
        await using var provider = BuildProvider(
            configuration: [],
            services =>
            {
                services.AddSingleton(probe);
                services.AddDbContext<FirstContext>((sp, o) => Configure(o, sp));
                services.AddDbContext<SecondContext>((sp, o) => Configure(o, sp));
                services.AddStartupWarmup();
            });

        await RunToCompletionAsync(provider);

        Assert.Equal(["FirstContext", "SecondContext"], probe.Built.OrderBy(name => name).ToArray());
        Assert.Equal(0, probe.ConnectionsOpened);
    }

    [Fact]
    public async Task Captures_the_dbcontext_types_registered_before_it_and_only_those()
    {
        var services = new ServiceCollection();
        services.AddDbContext<FirstContext>(o => o.UseNpgsql(UnreachableConnection));
        services.AddStartupWarmup();
        services.AddDbContext<SecondContext>(o => o.UseNpgsql(UnreachableConnection));

        await using var provider = services.BuildServiceProvider();
        var plan = provider.GetRequiredService<StartupWarmupPlan>();

        Assert.Equal([typeof(FirstContext)], plan.DbContextTypes.ToArray());
    }

    [Fact]
    public async Task A_context_that_cannot_be_built_is_logged_and_the_others_are_still_warmed()
    {
        var probe = new ConnectionProbe();
        var logs = new CapturingLoggerProvider();
        await using var provider = BuildProvider(
            configuration: [],
            services =>
            {
                services.AddSingleton(probe);
                services.AddDbContext<BrokenContext>(o => o.UseNpgsql(UnreachableConnection));
                services.AddDbContext<FirstContext>((sp, o) => Configure(o, sp));
                services.AddStartupWarmup();
            },
            logs);

        await RunToCompletionAsync(provider);

        Assert.Equal(["FirstContext"], probe.Built.ToArray());
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("BrokenContext", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, e => e.Level == LogLevel.Information && e.Message.Contains("finished", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Disabled_by_configuration_it_builds_nothing()
    {
        var probe = new ConnectionProbe();
        await using var provider = BuildProvider(
            configuration: new() { [StartupWarmupService.EnabledKey] = "false" },
            services =>
            {
                services.AddSingleton(probe);
                services.AddDbContext<FirstContext>((sp, o) => Configure(o, sp));
                services.AddStartupWarmup();
            });

        var gate = provider.GetRequiredService<StartupWarmupGate>();
        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
        }

        await Task.Delay(300);

        Assert.False(gate.IsTracking);
        Assert.Empty(probe.Built);

        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task A_malformed_setting_never_fails_host_start()
    {
        await using var provider = BuildProvider(
            configuration: new() { [StartupWarmupService.EnabledKey] = "not-a-bool" },
            services =>
            {
                services.AddDbContext<FirstContext>(o => o.UseNpgsql(UnreachableConnection));
                services.AddStartupWarmup();
            });

        foreach (var hosted in provider.GetServices<IHostedService>())
        {
            await hosted.StartAsync(CancellationToken.None);
            await hosted.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Start_returns_immediately_even_while_the_warm_up_is_still_waiting_for_traffic_to_clear()
    {
        var probe = new ConnectionProbe();
        await using var provider = BuildProvider(
            configuration: [],
            services =>
            {
                services.AddSingleton(probe);
                services.AddDbContext<FirstContext>((sp, o) => Configure(o, sp));
                services.AddStartupWarmup();
            });
        var gate = provider.GetRequiredService<StartupWarmupGate>();

        gate.Enter(); // a request is in flight
        var hosted = provider.GetServices<IHostedService>().OfType<StartupWarmupService>().Single();
        await hosted.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        // Busy: nothing is built while the request is in flight ...
        await Task.Delay(400);
        Assert.Empty(probe.Built);
        Assert.True(gate.IsTracking);

        // ... and the warm-up resumes as soon as it clears.
        gate.Leave();
        await WaitForAsync(() => !gate.IsTracking);
        Assert.Equal(["FirstContext"], probe.Built.ToArray());

        await hosted.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task The_time_budget_ends_a_warm_up_that_never_finds_the_host_idle()
    {
        var probe = new ConnectionProbe();
        await using var provider = BuildProvider(
            configuration: new() { [StartupWarmupService.TimeoutSecondsKey] = "1" },
            services =>
            {
                services.AddSingleton(probe);
                services.AddDbContext<FirstContext>((sp, o) => Configure(o, sp));
                services.AddStartupWarmup();
            });
        var gate = provider.GetRequiredService<StartupWarmupGate>();
        gate.Enter(); // never leaves
        var hosted = provider.GetServices<IHostedService>().OfType<StartupWarmupService>().Single();

        await hosted.StartAsync(CancellationToken.None);
        await WaitForAsync(() => !gate.IsTracking, TimeSpan.FromSeconds(10));

        Assert.Empty(probe.Built);
        await hosted.StopAsync(CancellationToken.None);
        gate.Leave();
    }

    [Fact]
    public async Task Stopping_the_host_cancels_a_warm_up_that_is_waiting()
    {
        await using var provider = BuildProvider(
            configuration: [],
            services =>
            {
                services.AddDbContext<FirstContext>(o => o.UseNpgsql(UnreachableConnection));
                services.AddStartupWarmup();
            });
        var gate = provider.GetRequiredService<StartupWarmupGate>();
        gate.Enter();
        var hosted = provider.GetServices<IHostedService>().OfType<StartupWarmupService>().Single();
        await hosted.StartAsync(CancellationToken.None);

        await hosted.StopAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(gate.IsTracking);
        gate.Leave();
    }

    [Fact]
    public async Task The_real_host_warms_up_without_a_single_warning()
    {
        // The whole composition root, as the API tests build it: every module's DbContext (Npgsql
        // pointed at a dead port, or InMemory where a test swapped it), the real key services and
        // the real JSON options. The warm-up must finish and must not have had to skip anything.
        var logs = new CapturingLoggerProvider();
        using var factory = new RaffaApiFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs));
        });

        using var client = factory.CreateClient(); // starts the host, and with it the warm-up
        var gate = factory.Services.GetRequiredService<StartupWarmupGate>();

        await WaitForAsync(() => !gate.IsTracking, TimeSpan.FromSeconds(60));

        var warmupEntries = logs.Entries.Where(e => e.Category.Contains(nameof(StartupWarmupService), StringComparison.Ordinal)).ToList();
        Assert.DoesNotContain(warmupEntries, e => e.Level >= LogLevel.Warning);
        Assert.Contains(warmupEntries, e => e.Message.Contains("finished", StringComparison.Ordinal));
        Assert.Contains(warmupEntries, e => e.Message.Contains("0 steps skipped", StringComparison.Ordinal));
    }

    private static ServiceProvider BuildProvider(
        Dictionary<string, string?> configuration,
        Action<IServiceCollection> register,
        CapturingLoggerProvider? logs = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (logs is not null)
            {
                logging.AddProvider(logs);
            }
        });
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(configuration).Build());
        register(services);
        return services.BuildServiceProvider();
    }

    // EF caches the internal service provider -- and with it every built model -- per options shape
    // for the whole process, so without this the second test to touch a context type would find its
    // model already built and OnModelCreating would never run for the probe to see.
    private static void Configure(DbContextOptionsBuilder options, IServiceProvider sp) =>
        options
            .UseNpgsql(UnreachableConnection)
            .EnableServiceProviderCaching(false)
            .ConfigureWarnings(warnings => warnings.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning))
            .AddInterceptors(sp.GetRequiredService<ConnectionProbe>());

    private static async Task RunToCompletionAsync(ServiceProvider provider)
    {
        var gate = provider.GetRequiredService<StartupWarmupGate>();
        var hosted = provider.GetServices<IHostedService>().OfType<StartupWarmupService>().Single();

        await hosted.StartAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        await WaitForAsync(() => !gate.IsTracking, TimeSpan.FromSeconds(30));
        await hosted.StopAsync(CancellationToken.None);
    }

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Timed out waiting for the warm-up.");
            await Task.Delay(25);
        }
    }

    /// <summary>Records which models were built and whether anything ever tried to open a connection.</summary>
    private sealed class ConnectionProbe : DbConnectionInterceptor
    {
        private int _opened;

        public ConcurrentQueue<string> BuiltQueue { get; } = new();

        public IReadOnlyCollection<string> Built => BuiltQueue.ToArray();

        public int ConnectionsOpened => Volatile.Read(ref _opened);

        public void ModelBuilt(string context) => BuiltQueue.Enqueue(context);

        public override InterceptionResult ConnectionOpening(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
        {
            Interlocked.Increment(ref _opened);
            return result;
        }

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _opened);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FirstContext(DbContextOptions<FirstContext> options, ConnectionProbe probe) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            probe.ModelBuilt(nameof(FirstContext));
            modelBuilder.Entity<Item>();
        }
    }

    private sealed class SecondContext(DbContextOptions<SecondContext> options, ConnectionProbe probe) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            probe.ModelBuilt(nameof(SecondContext));
            modelBuilder.Entity<Item>();
        }
    }

    private sealed class BrokenContext : DbContext
    {
        public BrokenContext(DbContextOptions<BrokenContext> options)
            : base(options)
        {
            throw new InvalidOperationException("cannot be constructed in this host");
        }
    }

    private sealed class Item
    {
        public int Id { get; set; }
    }

    private sealed record LogEntry(string Category, LogLevel Level, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyCollection<LogEntry> Entries => _entries.ToArray();

        public ILogger CreateLogger(string categoryName) => new Capture(categoryName, _entries);

        public void Dispose()
        {
        }

        private sealed class Capture(string category, ConcurrentQueue<LogEntry> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                sink.Enqueue(new LogEntry(category, logLevel, formatter(state, exception)));
        }
    }
}
