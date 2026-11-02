using Microsoft.EntityFrameworkCore;
using Raffa.Market.Contracts;
using Raffa.Market.Infrastructure;
using Raffa.SharedKernel;

namespace Raffa.Market.Network;

/// <summary>What the customer network currently supports: how many observations and workspaces, and the deals that clear the threshold.</summary>
public sealed record MarketNetworkSnapshot(int Observations, int Workspaces, int SuppliersPriced, IReadOnlyList<MarketDeal> Deals);

/// <summary>Where the customer network's current state comes from.</summary>
public interface IMarketNetworkSource
{
    Task<MarketNetworkSnapshot> BuildAsync(CancellationToken cancellationToken = default);
}

/// <summary>Reads the anonymised observations and builds the deals the shared market base can publish.</summary>
public sealed class MarketNetworkService(IDbContextFactory<MarketDbContext> dbFactory, MarketObservationOptions options, IClock clock) : IMarketNetworkSource
{
    public async Task<MarketNetworkSnapshot> BuildAsync(CancellationToken cancellationToken = default)
    {
        var since = clock.UtcNow.AddDays(-Math.Max(1, options.MaxAgeDays));
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.MarketObservations.AsNoTracking().Where(o => o.ObservedAt >= since).ToListAsync(cancellationToken).ConfigureAwait(false);
        var deals = MarketNetworkDealBuilder.Build(rows, options.MinWorkspaces);
        return new MarketNetworkSnapshot(
            rows.Count, rows.Select(r => r.WorkspaceKey).Distinct().Count(), rows.Where(r => r.UnitPrice is > 0).Select(r => r.SupplierKey).Distinct().Count(), deals);
    }
}
