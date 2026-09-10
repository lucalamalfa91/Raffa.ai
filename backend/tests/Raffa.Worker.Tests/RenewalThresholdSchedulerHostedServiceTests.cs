using Raffa.Worker.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Raffa.Worker.Tests;

/// <summary>
/// Proves task E03/F02/US01/T01's own "daily scheduler" claim (parent story
/// us-01-threshold-scheduler: "a daily scheduler that fires renewal/cancellation threshold
/// events") actually runs as a real, recurring background loop inside the deployable Worker host —
/// not just that <c>RenewalThresholdScheduler</c> is registered and unused. Mirrors
/// <see cref="DeployableWorkerTests.Worker_consumes_a_queued_message_end_to_end"/>'s realtime-wait
/// style for the identical reason: a <see cref="BackgroundService"/>'s loop can only be observed by
/// actually letting it run for a bounded amount of wall-clock time.
///
/// Uses a millisecond-scale <see cref="RenewalThresholdSchedulerOptions.Interval"/> (the real
/// default is 24 hours, per that type's own doc comment, too long to wait out in a test) and a
/// counting fake <see cref="IActiveRenewalContractsSource"/> that always returns zero tenants — so
/// this test proves the timer/dispatch loop itself without touching the real, Postgres-backed
/// <c>IAuditWriter</c> <c>AddAuditModule</c> wires into this host (a contract that actually matched
/// a threshold would try to write there; that end-to-end behaviour is already proven, without a
/// database, at <c>Raffa.Renewals.Tests.RenewalThresholdSchedulerTests</c> via a
/// <c>RecordingAuditWriter</c>).
/// </summary>
public sealed class RenewalThresholdSchedulerHostedServiceTests
{
    // A syntactically valid Npgsql connection string satisfies AddDocumentsContractsModule's eager
    // UseNpgsql() parsing; nothing in this test ever opens a real connection (see class doc comment
    // above) — same approach as DeployableWorkerTests.BuildHost.
    private const string ConnectionString =
        "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true";

    [Fact]
    public async Task Ticks_repeatedly_on_the_configured_interval_and_queries_the_active_contracts_source()
    {
        var countingSource = new CountingActiveRenewalContractsSource();
        var builder = Host.CreateApplicationBuilder();

        // Pre-register before AddWorkerHost: TryAddSingleton inside AddWorkerHost means the first
        // registration wins (this file's own RenewalThresholdSchedulerOptions doc comment; mirrors
        // Raffa.Renewals.Tests.ServiceCollectionExtensionsTests's identical IClock-override
        // proof), swapping in a fast interval and an observable fake source.
        builder.Services.AddSingleton<IActiveRenewalContractsSource>(countingSource);
        builder.Services.AddSingleton(new RenewalThresholdSchedulerOptions { Interval = TimeSpan.FromMilliseconds(20) });
        // Task E03/F03/US01/T02 (renewal-action) added AddWorkerHost's third (Renewals) connection
        // string parameter — same never-connected-to string reused for all three, mirroring
        // DeployableWorkerTests.BuildHost's identical convention (see this file's own class doc
        // comment for why a syntactically valid string is enough: nothing here opens a real
        // connection).
        builder.Services.AddWorkerHost(ConnectionString, ConnectionString, ConnectionString);

        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (countingSource.CallCount < 3 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }

            Assert.True(countingSource.CallCount >= 3, $"Expected at least 3 ticks, observed {countingSource.CallCount}.");
        }
        finally
        {
            await host.StopAsync();
        }
    }

    private sealed class CountingActiveRenewalContractsSource : IActiveRenewalContractsSource
    {
        private int _callCount;

        public int CallCount => _callCount;

        public Task<IReadOnlyList<TenantRenewalContracts>> GetActiveContractsAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return Task.FromResult<IReadOnlyList<TenantRenewalContracts>>([]);
        }
    }
}
