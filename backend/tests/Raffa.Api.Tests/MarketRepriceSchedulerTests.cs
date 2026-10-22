using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Raffa.Api.Infrastructure;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Jobs;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Api.Tests;

/// <summary>
/// <see cref="MarketRepriceScheduler"/>: what a workspace entry may queue, how often, and when the
/// <see cref="JobKinds.MarketReprice"/> job is really published. No database: the "is anything due" question
/// is the <see cref="IMarketRepriceProbe"/> seam.
/// </summary>
public sealed class MarketRepriceSchedulerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 22, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_transport_that_is_not_a_durable_queue_queues_nothing()
    {
        var (scheduler, _, _) = Create(durable: false);

        Assert.False(scheduler.Request(TenantId.New()));
    }

    [Fact]
    public void Switching_the_pre_warm_off_queues_nothing()
    {
        var (scheduler, _, _) = Create(options: new MarketPricingOptions { PrewarmOnWorkspaceEntry = false });

        Assert.False(scheduler.Request(TenantId.New()));
    }

    [Fact]
    public void A_workspace_is_considered_at_most_once_per_interval_and_workspaces_are_independent()
    {
        var (scheduler, _, clock) = Create(options: new MarketPricingOptions { PrewarmMinIntervalMinutes = 10 });
        var tenant = TenantId.New();

        Assert.True(scheduler.Request(tenant));
        Assert.False(scheduler.Request(tenant));
        Assert.True(scheduler.Request(TenantId.New()));

        clock.Now = T0.AddMinutes(9);
        Assert.False(scheduler.Request(tenant));

        clock.Now = T0.AddMinutes(10);
        Assert.True(scheduler.Request(tenant));
    }

    [Fact]
    public async Task Dispatch_publishes_the_job_for_the_workspace_only_when_a_contract_is_due()
    {
        var tenant = TenantId.New();
        var (due, publisherDue, _) = Create(contractsDue: true);
        var (idle, publisherIdle, _) = Create(contractsDue: false);

        Assert.True(await due.DispatchAsync(tenant, CancellationToken.None));
        Assert.False(await idle.DispatchAsync(tenant, CancellationToken.None));

        var message = Assert.Single(publisherDue.Messages);
        Assert.Equal(JobKinds.MarketReprice, message.Kind);
        Assert.Equal(tenant.Value, message.TenantId);
        Assert.Equal(tenant.Value, message.ResourceId);
        Assert.NotEqual(Guid.Empty, message.JobId);
        Assert.Equal(JobRequested.CurrentSchemaVersion, message.SchemaVersion);
        Assert.Empty(publisherIdle.Messages);
    }

    [Fact]
    public async Task A_request_is_turned_into_a_published_job_by_the_background_loop()
    {
        var (scheduler, publisher, _) = Create(contractsDue: true);
        var tenant = TenantId.New();

        await scheduler.StartAsync(CancellationToken.None);
        try
        {
            Assert.True(scheduler.Request(tenant));

            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (publisher.Messages.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(25);
            }

            Assert.Equal(tenant.Value, Assert.Single(publisher.Messages).TenantId);
        }
        finally
        {
            await scheduler.StopAsync(CancellationToken.None);
        }
    }

    private static (MarketRepriceScheduler Scheduler, CapturingPublisher Publisher, TestClock Clock) Create(
        bool durable = true, bool contractsDue = true, MarketPricingOptions? options = null)
    {
        var publisher = new CapturingPublisher(durable);
        var clock = new TestClock { Now = T0 };
        var services = new ServiceCollection();
        services.AddScoped<IMarketRepriceProbe>(_ => new FixedProbe(contractsDue));
        var provider = services.BuildServiceProvider();
        var scheduler = new MarketRepriceScheduler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            publisher,
            options ?? new MarketPricingOptions(),
            clock,
            NullLogger<MarketRepriceScheduler>.Instance);
        return (scheduler, publisher, clock);
    }

    private sealed class TestClock : IClock
    {
        public DateTimeOffset Now { get; set; }

        public DateTimeOffset UtcNow => Now;
    }

    private sealed class FixedProbe(bool due) : IMarketRepriceProbe
    {
        public Task<bool> HasDueContractsAsync(TenantId tenantId, CancellationToken cancellationToken) => Task.FromResult(due);
    }

    private sealed class CapturingPublisher(bool durable) : IJobQueuePublisher
    {
        private readonly List<JobRequested> _messages = [];

        public bool IsDurable => durable;

        public IReadOnlyList<JobRequested> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public Task PublishAsync(JobRequested message, CancellationToken cancellationToken = default)
        {
            lock (_messages)
            {
                _messages.Add(message);
            }

            return Task.CompletedTask;
        }
    }
}
