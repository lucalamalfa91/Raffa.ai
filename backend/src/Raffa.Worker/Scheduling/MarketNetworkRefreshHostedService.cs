using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Raffa.Market.Ingestion;
using Raffa.Market.Network;
using Raffa.SharedKernel;

namespace Raffa.Worker.Scheduling;

/// <summary>
/// The invisible half of the shared market base: every <c>Market:Observations:RefreshHours</c> the base is
/// refreshed from the market feed plus what the customer network supports (the ingestion job reads both
/// through one provider, idempotent: unchanged records cost nothing). Each run leaves one audit row
/// ("market.network.refreshed", system tenant) with what the network held and what was published, so the flow
/// can be monitored without ever showing in the product. A failure is logged and audited, then retried at the
/// next interval — it never stops the worker.
/// </summary>
internal sealed class MarketNetworkRefreshHostedService(
    IServiceScopeFactory scopes,
    IServiceProvider services,
    IClock clock,
    ILogger<MarketNetworkRefreshHostedService> logger) : BackgroundService
{
    public const string RefreshedAction = "market.network.refreshed";

    private static readonly TimeSpan FirstDelay = TimeSpan.FromMinutes(2);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Only a worker that has the market store (ConnectionStrings:Market) registers the options: no store, nothing to refresh.
        var options = services.GetService<MarketObservationOptions>();
        if (options is null || !options.Enabled || options.RefreshHours <= 0)
        {
            return;
        }

        try
        {
            await Task.Delay(FirstDelay, stoppingToken).ConfigureAwait(false);
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromHours(options.RefreshHours), stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var network = scope.ServiceProvider.GetService<MarketNetworkService>();
        var ingestion = scope.ServiceProvider.GetService<MarketIngestionService>();
        var audit = scope.ServiceProvider.GetService<IAuditWriter>();
        if (network is null || ingestion is null)
        {
            return;
        }

        object detail;
        try
        {
            var snapshot = await network.BuildAsync(cancellationToken).ConfigureAwait(false);
            var result = await ingestion.IngestAsync(feedVersion: null, cancellationToken).ConfigureAwait(false);
            detail = result.IsSuccess
                ? new
                {
                    ok = true,
                    observations = snapshot.Observations,
                    workspaces = snapshot.Workspaces,
                    suppliersPriced = snapshot.SuppliersPriced,
                    networkDeals = snapshot.Deals.Count,
                    inserted = result.Value.Inserted,
                    updated = result.Value.Updated,
                    unchanged = result.Value.Unchanged,
                    removed = result.Value.Removed,
                }
                : new { ok = false, error = result.Error, observations = snapshot.Observations, workspaces = snapshot.Workspaces, suppliersPriced = snapshot.SuppliersPriced, networkDeals = snapshot.Deals.Count, inserted = 0, updated = 0, unchanged = 0, removed = 0 };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "The market network refresh failed; it will be retried at the next interval.");
            detail = new { ok = false, error = ex.GetType().Name, observations = 0, workspaces = 0, suppliersPriced = 0, networkDeals = 0, inserted = 0, updated = 0, unchanged = 0, removed = 0 };
        }

        if (audit is not null)
        {
            try
            {
                await audit.WriteAsync(
                        new AuditEntry(MarketIngestionService.SystemTenantId, "system", RefreshedAction, "market_record", "network", clock.UtcNow, JsonSerializer.Serialize(detail, Json)),
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "The market network refresh could not be audited.");
            }
        }
    }
}
