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
/// work runs on its own background thread started from <see cref="StartAsync"/>, every step is
/// isolated in its own try/catch (a context or service that cannot be built in some host, such as a
/// test double, is logged and skipped), and a time budget stops it from starting new steps once spent.
/// </para>
///
/// <para>
/// <b>It yields to real traffic.</b> At 0.25 vCPU the whole warm-up is several seconds of CPU, and
/// speculative work must not slow the very first requests it exists to speed up (measured: with
/// the warm-up at normal priority, a replica started to serve a waiting request answered its first
/// <c>/health</c> about 3 s later). Two things keep it out of their way. Before each step it waits
/// until no request is in flight (<see cref="StartupWarmupGate"/>). And on Linux its thread runs at
/// the lowest scheduling priority (nice 19, <c>setpriority</c> on the calling thread only -- which is
/// why it has a thread of its own, never a pool thread that would keep the low priority for real
/// work), so while a request is running the warm-up only takes the CPU the request leaves free, for
/// instance while it waits on the database. On a replica that starts idle (a deploy, a scale-out, a
/// warm minimum replica) neither applies and it runs straight through.
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

    private const int PrioProcess = 0;
    private const int LowestPriorityNice = 19;

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

    private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _budget;
    private bool _started;

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

            // A thread of its own, so a slow warm-up can never hold up Kestrel or another hosted
            // service, and so lowering its priority cannot leak to a pool thread.
            var thread = new Thread(() => RunOnDedicatedThread(budget))
            {
                IsBackground = true,
                Name = "startup-warmup",
            };
            _started = true;
            thread.Start();
        }
        catch (Exception exception)
        {
            // Never fail the host over an optimisation (bad config value, disposed provider, ...).
            _started = false;
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
            if (_started)
            {
                await _finished.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                _budget?.Dispose();
                _budget = null;
            }
        }
        catch (Exception exception)
        {
            // Includes the host's shutdown token firing; either way there is nothing to wait for.
            // The thread is a background thread and its budget is already cancelled, so it ends on
            // its own; the budget is then simply left for the finalizer.
            logger.LogDebug(exception, "Startup warm-up did not stop cleanly.");
        }
        finally
        {
            gate.StopTracking();
        }
    }

    private void RunOnDedicatedThread(CancellationToken budget)
    {
        try
        {
            LowerThreadPriority();
            Run(budget);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Startup warm-up stopped early.");
        }
        finally
        {
            gate.StopTracking();
            _finished.TrySetResult();
        }
    }

    // Linux only: nice 19 for the calling thread (setpriority with who = 0 addresses the calling
    // thread, not the process). Raising one's own niceness needs no privilege, so it works for the
    // non-root user the image runs as. Anything unexpected (musl, a sandbox, no libc.so.6) just
    // leaves the thread at normal priority.
    private void LowerThreadPriority()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            if (NativeMethods.SetPriority(PrioProcess, 0, LowestPriorityNice) != 0)
            {
                logger.LogDebug("Startup warm-up keeps its normal thread priority (setpriority failed).");
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            logger.LogDebug(exception, "Startup warm-up keeps its normal thread priority (libc not available).");
        }
    }

    private void Run(CancellationToken budget)
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
                if (!WaitForIdle(budget))
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
                if (!WaitForIdle(budget))
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
            if (WaitForIdle(budget))
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
    private bool WaitForIdle(CancellationToken budget)
    {
        while (!budget.IsCancellationRequested)
        {
            if (!gate.IsBusy)
            {
                return true;
            }

            // Wakes early when the budget is cancelled (budget expiry or host shutdown).
            budget.WaitHandle.WaitOne(IdlePollInterval);
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

    private static class NativeMethods
    {
        // glibc by its versioned name: the plain "libc" probe needs the libc6-dev linker script,
        // which the runtime images do not ship.
        [System.Runtime.InteropServices.DllImport("libc.so.6", EntryPoint = "setpriority")]
        public static extern int SetPriority(int which, int who, int priority);
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
