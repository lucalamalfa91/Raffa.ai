using Contigo.SharedKernel;
using Contigo.SharedKernel.Suppliers;

namespace Contigo.Api.Tests.TestSupport;

/// <summary>
/// In-memory <see cref="ISupplierNameLookup"/> for task E13/F03/US01/T02's read-model assertions
/// (requirements R-SUP-04: "everywhere a supplier is shown, the name is used"). The real
/// implementation lives in <c>Contigo.Suppliers.Products</c> behind its own Postgres DbContext —
/// registered by <c>Program.cs</c>, and therefore pointed at a connection string these tests never
/// stand up (the same reason <see cref="InMemoryAskEngineFactory"/> swaps the two DbContexts it
/// needs). Substituting the port itself keeps the assertion about what
/// <c>PortfolioEndpointExtensions</c>/<c>ContractsEndpointExtensions</c>/<c>RenewalsEndpointExtensions</c>
/// do with a resolved name, which is this task's own composition; the port's real, RLS-scoped
/// behaviour is proven by <c>Contigo.Suppliers.Products.Tests</c> and
/// <c>Contigo.IntegrationTests.SupplierCrossTenantIsolationTests</c>.
///
/// <para>
/// Honours the port's own "an id that does not resolve is simply absent from the result — never a
/// fabricated placeholder name" contract, so a test can seed a contract pointing at an unknown
/// supplier and still assert the endpoint reports a <see langword="null"/> name rather than
/// inventing one.
/// </para>
/// </summary>
internal sealed class StubSupplierNameLookup(IReadOnlyDictionary<EntityId, string> namesById) : ISupplierNameLookup
{
    public Task<IReadOnlyDictionary<EntityId, string>> GetNamesAsync(
        TenantId tenantId, IReadOnlyCollection<EntityId> supplierIds, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<EntityId, string> names = supplierIds
            .Where(namesById.ContainsKey)
            .Distinct()
            .ToDictionary(id => id, id => namesById[id]);

        return Task.FromResult(names);
    }
}
