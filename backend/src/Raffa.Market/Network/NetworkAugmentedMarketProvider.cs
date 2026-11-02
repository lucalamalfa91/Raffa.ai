using Microsoft.Extensions.Logging;
using Raffa.SharedKernel;

namespace Raffa.Market.Network;

/// <summary>
/// The market feed plus what the customer network supports: the ingestion job reads this one provider, so the
/// shared base grows with every customer's supplier replies without any other path into it. If the network
/// cannot be read the plain feed goes through, so a hiccup here never empties or stalls the base.
/// </summary>
public sealed class NetworkAugmentedMarketProvider(
    IMarketIntelligenceProvider inner,
    IMarketNetworkSource network,
    MarketObservationOptions options,
    ILogger<NetworkAugmentedMarketProvider>? logger = null) : IMarketIntelligenceProvider
{
    public async Task<Result<MarketFeedSnapshot>> GetDealsAsync(string? feedVersion = null, CancellationToken cancellationToken = default)
    {
        var feed = await inner.GetDealsAsync(feedVersion, cancellationToken).ConfigureAwait(false);
        if (feed.IsFailure || !options.Enabled)
        {
            return feed;
        }

        try
        {
            var snapshot = await network.BuildAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot.Deals.Count == 0)
            {
                return feed;
            }

            var taken = feed.Value.Deals.Select(d => d.RecordId).ToHashSet(StringComparer.Ordinal);
            var merged = feed.Value.Deals.Concat(snapshot.Deals.Where(d => !taken.Contains(d.RecordId))).ToList();
            return Result<MarketFeedSnapshot>.Success(new MarketFeedSnapshot(merged, feed.Value.FeedVersion));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger?.LogWarning(ex, "The customer network could not be read; the market feed goes through without it.");
            return feed;
        }
    }
}
