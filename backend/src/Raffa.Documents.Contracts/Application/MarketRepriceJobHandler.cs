using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Jobs;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Documents.Contracts.Application;

/// <summary>
/// Handles <see cref="JobKinds.MarketReprice"/>: re-prices one tenant's contracts whose stored market
/// comparison is due, so the next read of any of them finds the finished comparison instead of waiting
/// for it (see <see cref="LineItemMarketPriceService"/>'s remarks and <see cref="MarketPricingOptions"/>).
///
/// <para>
/// It runs the very code a read would have run (<see cref="LineItemMarketPriceService.RefreshIfStaleAsync"/>),
/// so what it stores is exactly what a read would have stored, only earlier. That also makes it safe to run
/// twice, or alongside a read or another handler: whoever comes second finds nothing left to price. There is
/// no durable job row to claim, so it never answers <see cref="JobHandleOutcome.JobNotFound"/> or
/// <see cref="JobHandleOutcome.ClaimLost"/>. One contract failing is logged and skipped; the others carry on,
/// and the skipped one is simply still due for the next pre-warm or read.
/// </para>
/// </summary>
public sealed class MarketRepriceJobHandler(
    LineItemMarketPriceService pricing,
    ITenantContext tenantContext,
    MarketPricingOptions options,
    ILogger<MarketRepriceJobHandler>? logger = null) : IJobHandler
{
    private readonly ILogger _logger = logger ?? NullLogger<MarketRepriceJobHandler>.Instance;

    public string Kind => JobKinds.MarketReprice;

    public async Task<JobHandleOutcome> HandleAsync(
        JobRequested message, long deliveryCount, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var tenantId = new TenantId(message.TenantId);
        using var tenantScope = tenantContext.BeginScope(tenantId);

        var due = await pricing
            .FindContractsNeedingRefreshAsync(tenantId, options.MaxContractsPerJob, cancellationToken)
            .ConfigureAwait(false);

        var refreshed = 0;
        foreach (var contractId in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await pricing.RefreshIfStaleAsync(tenantId, contractId, cancellationToken).ConfigureAwait(false);
                refreshed++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(
                    exception, "Market pre-warm of contract {ContractId} (tenant {TenantId}) failed; it stays due.", contractId, tenantId);
            }
        }

        _logger.LogInformation(
            "Market pre-warm for tenant {TenantId}: {Refreshed} of {Due} due contract(s) re-priced.", tenantId, refreshed, due.Count);
        return JobHandleOutcome.Handled;
    }
}
