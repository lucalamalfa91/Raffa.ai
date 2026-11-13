using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// Every morning (from <see cref="FxReferenceRateOptions.RefreshHour"/>, local to
/// <see cref="FxReferenceRateOptions.TimeZone"/>) downloads the ECB reference rates and stores them in
/// <c>fx_reference_rate</c>, so no Admin has to type exchange rates by hand. The loop wakes every
/// <see cref="FxReferenceRateOptions.CheckIntervalMinutes"/>: a download is due when none succeeded yet
/// today (local) and the hour has come — or right away when nothing is stored at all. So a restart, a
/// missed morning or a failed call is caught up at the next wake, and several replicas cost at most a
/// few idempotent upserts. A failure is logged and never stops the host.
/// </summary>
internal sealed class FxReferenceRateRefreshService(
    IServiceScopeFactory scopes,
    FxReferenceRateOptions options,
    IClock clock,
    ILogger<FxReferenceRateRefreshService> logger) : BackgroundService
{
    private static readonly TimeSpan FirstDelay = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Enabled)
        {
            return;
        }

        try
        {
            await Task.Delay(FirstDelay, stoppingToken).ConfigureAwait(false);
            var interval = TimeSpan.FromMinutes(Math.Max(1, options.CheckIntervalMinutes));
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunIfDueAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "The ECB reference rates could not be refreshed; retrying at the next check.");
                }

                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    /// <summary>Downloads and stores the rates when due; returns whether it did.</summary>
    internal async Task<bool> RunIfDueAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<FxReferenceRateStore>();
        if (!store.IsAvailable)
        {
            return false;
        }

        var latest = await store.GetLatestAsync(cancellationToken).ConfigureAwait(false);
        if (!IsDue(latest.FetchedAt, clock.UtcNow, ZoneOf(options.TimeZone), options.RefreshHour))
        {
            return false;
        }

        var client = scope.ServiceProvider.GetRequiredService<EcbReferenceRateClient>();
        var rates = await client.FetchAsync(cancellationToken).ConfigureAwait(false);
        await store.SaveAsync(rates.RateDate, rates.PerEur, EcbReferenceRateClient.Source, clock.UtcNow, cancellationToken)
            .ConfigureAwait(false);
        logger.LogInformation(
            "Stored {Count} ECB reference rates of {RateDate}.", rates.PerEur.Count, rates.RateDate.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }

    /// <summary>
    /// Due when nothing was ever fetched, or when the last fetch was before today's refresh hour (local)
    /// and that hour has come.
    /// </summary>
    internal static bool IsDue(DateTimeOffset? lastFetchedAt, DateTimeOffset now, TimeZoneInfo zone, int refreshHour)
    {
        if (lastFetchedAt is null)
        {
            return true;
        }

        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var hour = Math.Clamp(refreshHour, 0, 23);
        var todayLocal = new DateTime(localNow.Year, localNow.Month, localNow.Day, hour, 0, 0, DateTimeKind.Unspecified);
        var refreshAt = new DateTimeOffset(todayLocal, zone.GetUtcOffset(todayLocal));
        if (now < refreshAt)
        {
            // Before this morning's hour: yesterday's slot is the one that counts.
            refreshAt = refreshAt.AddDays(-1);
        }

        return lastFetchedAt.Value < refreshAt;
    }

    private static TimeZoneInfo ZoneOf(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
