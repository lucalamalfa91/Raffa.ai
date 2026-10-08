using Raffa.Chat.Application.Feedback;
using Raffa.Documents.Contracts.Application;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// The host's <see cref="IFeedbackNameSource"/> (F4-T01): the supplier names of the caller's
/// tenant, read the same way Ask reads them (<see cref="PortfolioQueryService"/> then
/// <see cref="ISupplierNameLookup"/>), so the scrub of a feedback answer can remove a supplier
/// typed in lower case or in a form no capitalisation rule would catch before the text reaches a
/// public GitHub issue. A host adapter: <c>Raffa.Chat</c> cannot reference the modules that hold
/// suppliers and contracts (ADR-002), the composition root can.
/// </summary>
internal sealed class PortfolioFeedbackNameSource(
    PortfolioQueryService portfolioQueryService,
    ISupplierNameLookup supplierNameLookup) : IFeedbackNameSource
{
    public async Task<IReadOnlyList<string>> GetSupplierNamesAsync(TenantId tenantId, CancellationToken cancellationToken = default)
    {
        var portfolio = await portfolioQueryService
            .GetPortfolioAsync(tenantId, PortfolioFilter.None, new PortfolioPageRequest(1, PortfolioPageRequest.MaxPageSize), cancellationToken)
            .ConfigureAwait(false);

        var supplierIds = portfolio.Items
            .Where(item => item.SupplierId is not null)
            .Select(item => new EntityId(item.SupplierId!.Value))
            .Distinct()
            .ToList();

        if (supplierIds.Count == 0)
        {
            return [];
        }

        var names = await supplierNameLookup.GetNamesAsync(tenantId, supplierIds, cancellationToken).ConfigureAwait(false);
        return names.Values.Where(name => !string.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
