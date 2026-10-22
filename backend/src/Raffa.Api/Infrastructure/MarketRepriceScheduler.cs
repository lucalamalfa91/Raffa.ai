using System.Collections.Concurrent;
using System.Threading.Channels;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Jobs;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// Queues, when someone enters a workspace, a <see cref="JobKinds.MarketReprice"/> job for that workspace
/// so the Worker re-prices its due contracts before anyone opens one (see
/// <see cref="LineItemMarketPriceService"/>'s remarks). The Worker cannot enumerate tenants, so the
/// workspace-entry call (<c>GET /api/workspaces</c>, which only returns workspaces the caller belongs to)
/// is the moment a tenant becomes known to the API.
///
/// <para>
/// <see cref="Request"/> is what the endpoint calls: it only decides and enqueues in memory, never
/// touches the database or the bus, never throws and never waits, so it adds nothing to the response.
/// The background loop then checks, in a scope of its own and under the tenant's scope, whether any
/// contract is actually due, and publishes the job only if so. A workspace is considered at most once per
/// <see cref="MarketPricingOptions.PrewarmMinIntervalMinutes"/> per process (the shell calls the endpoint
/// on every navigation), and nothing happens at all when the transport is not a durable queue (a dev box
/// or a test host), when no matcher is composed in, or when
/// <see cref="MarketPricingOptions.PrewarmOnWorkspaceEntry"/> is off.
/// </para>
/// </summary>
internal sealed class MarketRepriceScheduler(
    IServiceScopeFactory scopeFactory,
    IJobQueuePublisher publisher,
    MarketPricingOptions options,
    IClock clock,
    ILogger<MarketRepriceScheduler> logger) : BackgroundService
{
    private readonly Channel<TenantId> _pending = Channel.CreateBounded<TenantId>(
        new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _lastConsidered = new();

    /// <summary>True when the workspace was queued for a check; false when throttled, dropped or switched off.</summary>
    public bool Request(TenantId tenantId)
    {
        try
        {
            if (!options.PrewarmOnWorkspaceEntry || !publisher.IsDurable)
            {
                return false;
            }

            var now = clock.UtcNow;
            var minimum = TimeSpan.FromMinutes(Math.Max(0, options.PrewarmMinIntervalMinutes));
            while (true)
            {
                if (!_lastConsidered.TryGetValue(tenantId.Value, out var last))
                {
                    if (_lastConsidered.TryAdd(tenantId.Value, now))
                    {
                        break;
                    }

                    continue;
                }

                if (now - last < minimum)
                {
                    return false;
                }

                if (_lastConsidered.TryUpdate(tenantId.Value, now, last))
                {
                    break;
                }
            }

            return _pending.Writer.TryWrite(tenantId);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Market pre-warm request for tenant {TenantId} was not queued.", tenantId);
            return false;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var tenantId in _pending.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await DispatchAsync(tenantId, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // Best effort: the next workspace entry, or a read, does the same work.
                    logger.LogWarning(exception, "Market pre-warm check for tenant {TenantId} failed.", tenantId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host stopping.
        }
    }

    /// <summary>Publishes the job for <paramref name="tenantId"/> if it has a contract due; returns whether it did.</summary>
    internal async Task<bool> DispatchAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var probe = scope.ServiceProvider.GetRequiredService<IMarketRepriceProbe>();

        if (!await probe.HasDueContractsAsync(tenantId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        // Resource = the workspace; a fresh job id per request (there is no job row to claim).
        await publisher
            .PublishAsync(
                new JobRequested(JobKinds.MarketReprice, tenantId.Value, tenantId.Value, Guid.NewGuid(), JobRequested.CurrentSchemaVersion),
                cancellationToken)
            .ConfigureAwait(false);
        return true;
    }
}

/// <summary>Answers "does this workspace have a contract whose market comparison is due?" (a seam so the scheduler is testable without a database).</summary>
internal interface IMarketRepriceProbe
{
    Task<bool> HasDueContractsAsync(TenantId tenantId, CancellationToken cancellationToken);
}

internal sealed class LineItemMarketRepriceProbe(
    LineItemMarketPriceService pricing, ITenantContext tenantContext) : IMarketRepriceProbe
{
    public async Task<bool> HasDueContractsAsync(TenantId tenantId, CancellationToken cancellationToken)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);
        var due = await pricing.FindContractsNeedingRefreshAsync(tenantId, 1, cancellationToken).ConfigureAwait(false);
        return due.Count > 0;
    }
}
