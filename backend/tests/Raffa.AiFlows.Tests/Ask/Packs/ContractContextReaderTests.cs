using Raffa.AiFlows.Ask.Packs;
using Raffa.AiFlows.Shared.ContractContext;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Market.Contracts;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;
using Raffa.AiFlows.Tests.TestSupport;

namespace Raffa.AiFlows.Tests.Ask.Packs;

/// <summary>
/// Pins the baseline read counts of <see cref="ContractContextReader"/> (F1 Ask split, PR-2): the
/// reader is not memoized yet, so every call must reach its source exactly once, as the code it
/// replaced did. A later PR (memoization) is what is allowed to lower these numbers.
/// </summary>
public sealed class ContractContextReaderTests
{
    private static readonly TenantId Tenant = new(Guid.NewGuid());

    private sealed class CountingNames : ISupplierNameLookup
    {
        public int GetNamesCalls { get; private set; }

        public Dictionary<EntityId, string> Names { get; } = [];

        public Task<IReadOnlyDictionary<EntityId, string>> GetNamesAsync(
            TenantId tenantId, IReadOnlyCollection<EntityId> supplierIds, CancellationToken cancellationToken)
        {
            GetNamesCalls++;
            return Task.FromResult<IReadOnlyDictionary<EntityId, string>>(Names);
        }

        public Task<EntityId?> FindByNormalizedNameAsync(
            TenantId tenantId, string normalizedName, CancellationToken cancellationToken) =>
            Task.FromResult<EntityId?>(null);
    }

    private sealed class CountingDeals : IMarketDealLookup
    {
        public List<string> Suppliers { get; } = [];

        public Task<IReadOnlyList<MarketDeal>> GetBySupplierAsync(string supplierName, CancellationToken cancellationToken = default)
        {
            Suppliers.Add(supplierName);
            return Task.FromResult<IReadOnlyList<MarketDeal>>([]);
        }
    }

    /// <summary>Counts the tenant scopes opened, which is how many times
    /// <see cref="Contract360QueryService.GetByIdAsync"/> ran.</summary>
    private sealed class CountingTenantContext : ITenantContext
    {
        private readonly TenantContext inner = new();

        public int Scopes { get; private set; }

        public TenantId? Current => inner.Current;

        public IDisposable BeginScope(TenantId tenantId)
        {
            Scopes++;
            return inner.BeginScope(tenantId);
        }
    }

    private static (ContractContextReader Reader, CountingNames Names, CountingDeals Deals, CountingTenantContext Tenants) Create(
        string database)
    {
        var names = new CountingNames();
        var deals = new CountingDeals();
        var tenants = new CountingTenantContext();
        var contracts = new Contract360QueryService(InMemoryDocumentsDb.Create(database), tenants);
        // The benchmark key resolver never reaches its workspace store while the supplier name is
        // unresolved, which is the only branch these tests drive.
        var benchmarkKey = new BenchmarkKeyResolution(names, null!, tenants);
        var reader = new ContractContextReader(contracts, names, benchmarkKey, null!, deals, tenants);
        return (reader, names, deals, tenants);
    }

    [Fact]
    public async Task Display_name_reads_the_supplier_lookup_once_per_call()
    {
        var (reader, names, _, tenants) = Create(nameof(Display_name_reads_the_supplier_lookup_once_per_call));
        var supplier = new EntityId(Guid.NewGuid());
        names.Names[supplier] = "Salesforce";

        using var scope = tenants.BeginScope(Tenant);
        var first = await reader.ResolveDisplayNameAsync(supplier, ContractDocumentType.Msa, CancellationToken.None);
        var second = await reader.ResolveDisplayNameAsync(supplier, ContractDocumentType.Msa, CancellationToken.None);

        Assert.Equal("Salesforce", first);
        Assert.Equal("Salesforce", second);
        Assert.Equal(2, names.GetNamesCalls);
    }

    [Fact]
    public async Task Display_name_without_a_supplier_id_falls_back_to_the_type_without_a_read()
    {
        var (reader, names, _, tenants) = Create(nameof(Display_name_without_a_supplier_id_falls_back_to_the_type_without_a_read));

        using var scope = tenants.BeginScope(Tenant);
        var name = await reader.ResolveDisplayNameAsync(null, ContractDocumentType.Msa, CancellationToken.None);

        Assert.Equal(ContractDocumentType.Msa.ToString(), name);
        Assert.Equal(0, names.GetNamesCalls);
    }

    [Fact]
    public async Task Display_name_of_a_portfolio_row_falls_back_to_the_type_when_the_lookup_has_no_name()
    {
        var (reader, names, _, tenants) = Create(nameof(Display_name_of_a_portfolio_row_falls_back_to_the_type_when_the_lookup_has_no_name));
        var item = new PortfolioListItem(
            Guid.NewGuid(), Guid.NewGuid(), ContractDocumentType.Nda, 1m, "EUR", null, null, null, null, false, "completed", null, null, null);

        using var scope = tenants.BeginScope(Tenant);
        var name = await reader.ResolveDisplayNameAsync(item, CancellationToken.None);

        Assert.Equal(ContractDocumentType.Nda.ToString(), name);
        Assert.Equal(1, names.GetNamesCalls);
    }

    [Fact]
    public async Task Benchmark_key_reads_the_supplier_lookup_once_and_is_incomplete_without_a_name()
    {
        var (reader, names, _, tenants) = Create(nameof(Benchmark_key_reads_the_supplier_lookup_once_and_is_incomplete_without_a_name));

        using var scope = tenants.BeginScope(Tenant);
        var key = await reader.ResolveBenchmarkKeyAsync(new EntityId(Guid.NewGuid()), CancellationToken.None);

        Assert.Equal((null, null), key);
        Assert.Equal(1, names.GetNamesCalls);
    }

    [Fact]
    public async Task Benchmark_key_without_a_supplier_id_makes_no_read()
    {
        var (reader, names, _, tenants) = Create(nameof(Benchmark_key_without_a_supplier_id_makes_no_read));

        using var scope = tenants.BeginScope(Tenant);
        var key = await reader.ResolveBenchmarkKeyAsync(null, CancellationToken.None);

        Assert.Equal((null, null), key);
        Assert.Equal(0, names.GetNamesCalls);
    }

    [Fact]
    public async Task Deals_read_the_market_lookup_once_per_call_with_the_supplier_as_given()
    {
        var (reader, _, deals, _) = Create(nameof(Deals_read_the_market_lookup_once_per_call_with_the_supplier_as_given));

        await reader.GetDealsBySupplierAsync("Salesforce", CancellationToken.None);
        await reader.GetDealsBySupplierAsync("Salesforce", CancellationToken.None);

        Assert.Equal(["Salesforce", "Salesforce"], deals.Suppliers);
    }

    [Fact]
    public async Task Contract_360_opens_one_tenant_scope_per_call_and_is_null_for_an_unknown_contract()
    {
        var (reader, _, _, tenants) = Create(nameof(Contract_360_opens_one_tenant_scope_per_call_and_is_null_for_an_unknown_contract));

        using var scope = tenants.BeginScope(Tenant);
        var before = tenants.Scopes;
        var first = await reader.GetContract360Async(Guid.NewGuid(), CancellationToken.None);
        var second = await reader.GetContract360Async(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(first);
        Assert.Null(second);
        Assert.Equal(2, tenants.Scopes - before);
    }
}
