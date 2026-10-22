using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Raffa.Documents.Contracts.Application;
using Raffa.Identity.Workspace.Infrastructure;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// The DbContext types this host registered, captured once at composition time by
/// <see cref="StartupWarmupServiceCollectionExtensions.AddStartupWarmup"/> so the warm-up needs no
/// hand-maintained list: a module added to <c>Program.cs</c> later is warmed automatically.
/// </summary>
internal sealed record StartupWarmupPlan(IReadOnlyList<Type> DbContextTypes);

/// <summary>
/// Counts the requests in flight while the warm-up is still running, so the warm-up can step aside
/// for real traffic (see <see cref="StartupWarmupService"/>). Tracking switches itself off when the
/// warm-up ends, after which the middleware is a single volatile read per request.
/// </summary>
internal sealed class StartupWarmupGate
{
    private int _inFlight;
    private volatile bool _tracking;

    public bool IsTracking => _tracking;

    public bool IsBusy => Volatile.Read(ref _inFlight) > 0;

    public void StartTracking() => _tracking = true;

    public void StopTracking() => _tracking = false;

    public void Enter() => Interlocked.Increment(ref _inFlight);

    public void Leave() => Interlocked.Decrement(ref _inFlight);
}

internal static class StartupWarmupServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="StartupWarmupService"/>. Call it after every module's <c>AddXxxModule</c>,
    /// because the DbContext types it warms are read from the service descriptors registered so far.
    /// </summary>
    public static IServiceCollection AddStartupWarmup(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // AddDbContext registers the context type itself (Scoped); Market's AddDbContextFactory
        // path adds a Scoped factory-created registration of the same shape. Keyed registrations
        // and open generics are not DbContexts we can resolve here.
        var dbContextTypes = services
            .Where(descriptor => !descriptor.IsKeyedService
                && !descriptor.ServiceType.IsGenericTypeDefinition
                && descriptor.ServiceType != typeof(DbContext)
                && typeof(DbContext).IsAssignableFrom(descriptor.ServiceType))
            .Select(descriptor => descriptor.ServiceType)
            .Distinct()
            .ToList();

        services.AddSingleton(new StartupWarmupPlan(dbContextTypes));
        services.AddSingleton<StartupWarmupGate>();
        services.AddHostedService<StartupWarmupService>();
        return services;
    }

    /// <summary>
    /// Lets <see cref="StartupWarmupService"/> see how many requests are in flight. Place it right
    /// after response compression; it adds nothing once the warm-up has finished.
    /// </summary>
    public static IApplicationBuilder UseStartupWarmupYielding(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var gate = app.ApplicationServices.GetRequiredService<StartupWarmupGate>();
        return app.Use((context, next) => gate.IsTracking ? Tracked(gate, context, next) : next(context));

        static async Task Tracked(StartupWarmupGate gate, HttpContext context, RequestDelegate next)
        {
            gate.Enter();
            try
            {
                await next(context).ConfigureAwait(false);
            }
            finally
            {
                gate.Leave();
            }
        }
    }
}

/// <summary>
/// Moves the first-request cost of a freshly started replica into startup, without touching the
/// database. On a scale-from-zero 0.25 vCPU replica the first request used to pay, serially, for
/// building every EF Core model (seconds of reflection and convention work per DbContext at that
/// CPU), compiling the dependency-injection call sites of the services it needs, and first use of
/// the JSON serializer; this does that work in the background right after start, so a request that
/// arrives later finds it done. If a request arrives mid-way it shares the work rather than
/// repeating it (EF builds a model once per context type and DI a call site once, whoever asks
/// first).
///
/// <para>
/// <b>What it deliberately does not do.</b> It never opens a connection or runs a query:
/// <see cref="DbContext.Model"/> builds and caches the model from the entity configurations alone,
/// and no service resolved here is invoked. It sets no ambient tenant or identity scope, so it
/// cannot create a path around the RLS claims. It never throws and never delays host start: the
/// work runs on a background task started from <see cref="StartAsync"/>, every step is isolated in
/// its own try/catch (a context or service that cannot be built in some host, such as a test
/// double, is logged and skipped), and a time budget stops it from starting new steps once spent.
/// </para>
///
/// <para>
/// <b>It yields to real traffic.</b> At 0.25 vCPU the whole warm-up is several seconds of CPU, and
/// speculative work must not slow the very first requests it exists to speed up. So before each
/// step it waits until no request is in flight (<see cref="StartupWarmupGate"/>): on a replica
/// started to serve a waiting request it mostly steps aside, on one that starts idle (deploy,
/// scale-out, a warm minimum replica) it runs straight through.
/// </para>
///
/// <para>
/// Configuration: <c>Startup:Warmup:Enabled</c> (default <see langword="true"/>) switches it off,
/// <c>Startup:Warmup:TimeoutSeconds</c> (default 20) sets the budget, waits included. Both are read
/// when the host starts, so a <c>WebApplicationFactory</c> override applies.
/// </para>
/// </summary>
internal sealed class StartupWarmupService(
    IServiceProvider services,
    IConfiguration configuration,
    StartupWarmupPlan plan,
    StartupWarmupGate gate,
    ILogger<StartupWarmupService> logger) : IHostedService
{
    internal const string EnabledKey = "Startup:Warmup:Enabled";
    internal const string TimeoutSecondsKey = "Startup:Warmup:TimeoutSeconds";
    internal const int DefaultTimeoutSeconds = 20;

    private static readonly TimeSpan IdlePollInterval = TimeSpan.FromMilliseconds(100);

    // The scoped services every SPA screen goes through on its first calls (the shell's workspace,
    // documents and portfolio reads, and the caller/tenant resolution in front of them). Resolving
    // one builds its dependency-injection call site and constructs it; constructing performs no I/O.
    private static readonly Type[] KeyServiceTypes =
    [
        typeof(ICallerContext),
        typeof(WorkspaceDirectoryService),
        typeof(PortfolioQueryService),
        typeof(DocumentQueryService),
        typeof(Contract360QueryService),
    ];

    private CancellationTokenSource? _budget;
    private Task _run = Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!configuration.GetValue(EnabledKey, true))
            {
                logger.LogInformation("Startup warm-up is disabled ({Key}=false).", EnabledKey);
                return Task.CompletedTask;
            }

            var seconds = Math.Clamp(configuration.GetValue(TimeoutSecondsKey, DefaultTimeoutSeconds), 1, 120);
            _budget = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            var budget = _budget.Token;

            gate.StartTracking();

            // Task.Run so a slow warm-up can never hold up Kestrel or another hosted service.
            _run = Task.Run(() => RunAsync(budget), CancellationToken.None);
        }
        catch (Exception exception)
        {
            // Never fail the host over an optimisation (bad config value, disposed provider, ...).
            gate.StopTracking();
            logger.LogWarning(exception, "Startup warm-up could not be started; continuing without it.");
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            _budget?.Cancel();
            await _run.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Includes the host's shutdown token firing; either way there is nothing to wait for.
            logger.LogDebug(exception, "Startup warm-up did not stop cleanly.");
        }
        finally
        {
            gate.StopTracking();
            _budget?.Dispose();
            _budget = null;
        }
    }

    private async Task RunAsync(CancellationToken budget)
    {
        var stopwatch = Stopwatch.StartNew();
        var models = 0;
        var resolved = 0;
        var skipped = 0;

        try
        {
            using var scope = services.CreateScope();
            var provider = scope.ServiceProvider;

            // 1) EF models, in registration order (identity/workspace and documents first: the
            // first screens need them). DbContext.Model builds and caches the model; it opens no
            // connection.
            foreach (var contextType in plan.DbContextTypes)
            {
                if (!await WaitForIdleAsync(budget).ConfigureAwait(false))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    var context = (DbContext)provider.GetRequiredService(contextType);
                    _ = context.Model;
                    models++;
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Startup warm-up skipped the model of {DbContext}.", contextType.Name);
                }
            }

            // 2) Key scoped services (DI call sites; construction only, no I/O).
            foreach (var serviceType in KeyServiceTypes)
            {
                if (!await WaitForIdleAsync(budget).ConfigureAwait(false))
                {
                    skipped++;
                    continue;
                }

                try
                {
                    if (provider.GetService(serviceType) is not null)
                    {
                        resolved++;
                    }
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Startup warm-up skipped resolving {Service}.", serviceType.Name);
                }
            }

            // 3) The serializer the endpoints answer with: first use pays for converter and
            // metadata construction. The host's own options instance is used, so the converters
            // warmed are the ones the responses will use.
            if (await WaitForIdleAsync(budget).ConfigureAwait(false))
            {
                try
                {
                    WarmJson(provider.GetService<IOptions<JsonOptions>>()?.Value.SerializerOptions ?? JsonSerializerOptions.Web);
                }
                catch (Exception exception)
                {
                    logger.LogWarning(exception, "Startup warm-up skipped the JSON serializer.");
                }
            }
            else
            {
                skipped++;
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Startup warm-up stopped early.");
            return;
        }
        finally
        {
            gate.StopTracking();
        }

        logger.LogInformation(
            "Startup warm-up finished in {ElapsedMs} ms: {Models} EF models, {Services} services, {Skipped} steps skipped (busy or out of time).",
            stopwatch.ElapsedMilliseconds,
            models,
            resolved,
            skipped);
    }

    /// <summary>
    /// Waits until no request is in flight. <see langword="false"/> when the time budget ran out
    /// (or the host is stopping) first, in which case the caller skips its step.
    /// </summary>
    private async Task<bool> WaitForIdleAsync(CancellationToken budget)
    {
        while (!budget.IsCancellationRequested)
        {
            if (!gate.IsBusy)
            {
                return true;
            }

            try
            {
                await Task.Delay(IdlePollInterval, budget).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return false;
            }
        }

        return false;
    }

    private static void WarmJson(JsonSerializerOptions options)
    {
        var sample = new WarmupPayload(
            Guid.NewGuid(),
            "warm-up",
            1234.56m,
            DateTimeOffset.UtcNow,
            DateOnly.FromDateTime(DateTime.UtcNow),
            WarmupKind.Sample,
            [new WarmupItem("a", 1), new WarmupItem("b", null)],
            new Dictionary<string, string?> { ["key"] = "value", ["none"] = null });

        var json = JsonSerializer.Serialize(sample, options);
        _ = JsonSerializer.Deserialize<WarmupPayload>(json, options);
        _ = JsonSerializer.SerializeToUtf8Bytes(new { ok = true, items = new[] { 1, 2, 3 } }, options);
    }

    private enum WarmupKind
    {
        Sample,
    }

    private sealed record WarmupItem(string Name, int? Count);

    private sealed record WarmupPayload(
        Guid Id,
        string Name,
        decimal Amount,
        DateTimeOffset At,
        DateOnly Day,
        WarmupKind Kind,
        IReadOnlyList<WarmupItem> Items,
        Dictionary<string, string?> Meta);
}
