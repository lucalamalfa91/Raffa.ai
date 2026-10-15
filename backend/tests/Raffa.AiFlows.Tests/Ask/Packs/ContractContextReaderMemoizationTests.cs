using Raffa.AiFlows.Ask.Orchestration;
using Raffa.AiFlows.Ask.Packs;
using Raffa.AiFlows.Shared.ContractContext;
using Raffa.AiFlows.Tests.TestSupport;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Market.Contracts;
using Raffa.Market.Retrieval;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.AiFlows.Tests.Ask.Packs;

/// <summary>
/// F1 Ask split PR-11 (F1-T01): with <see cref="AskTurnOptions.MemoizeTurnReads"/> on, a turn reads each
/// contract, display name, benchmark key, set of deals and priced lines once; <c>BeginTurn()</c> forgets
/// them. With the flag off (the default) the baseline in <see cref="ContractContextReaderTests"/> holds.
/// </summary>
public sealed class ContractContextReaderMemoizationTests
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

    /// <summary>A seeded contract has no priced lines, so the benchmark is never asked.</summary>
    private sealed class NoLinesBenchmark : Raffa.Benchmark.IBenchmarkService
    {
        public Task<Result<Raffa.Benchmark.Contracts.BenchmarkResult>> GetBenchmarkAsync(
            Raffa.Benchmark.Contracts.BenchmarkQuery query, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No line to price.");
    }

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

    private sealed record Rig(
        ContractContextReader Reader,
        CountingNames Names,
        CountingDeals Deals,
        CountingTenantContext Tenants,
        DocumentsContractsDbContext Db);

    private static Rig Create(string database, bool memoize)
    {
        var names = new CountingNames();
        var deals = new CountingDeals();
        var tenants = new CountingTenantContext();
        var db = InMemoryDocumentsDb.Create(database);
        var contracts = new Contract360QueryService(db, tenants);
        var benchmarkKey = new BenchmarkKeyResolution(names, null!, tenants);
        var reader = new ContractContextReader(
            contracts, names, benchmarkKey, new NoLinesBenchmark(), deals, tenants,
            turnOptions: new AskTurnOptions { MemoizeTurnReads = memoize });
        return new Rig(reader, names, deals, tenants, db);
    }

    private static async Task<Guid> SeedContractAsync(Rig rig)
    {
        var contract = new Contract
        {
            TenantId = Tenant,
            SupplierId = EntityId.New(),
            Type = ContractDocumentType.OrderForm,
            Status = "Completed",
            Currency = "EUR",
            AnnualSpend = 230000m,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        rig.Db.Contracts.Add(contract);
        await rig.Db.SaveChangesAsync();
        return contract.Id.Value;
    }

    // ---------------------------------------------------------------- Contract 360

    [Fact]
    public async Task Contract_360_is_read_once_per_turn_with_the_flag_on_and_the_second_call_returns_the_same_instance()
    {
        var rig = Create(nameof(Contract_360_is_read_once_per_turn_with_the_flag_on_and_the_second_call_returns_the_same_instance), memoize: true);
        var id = await SeedContractAsync(rig);

        using var scope = rig.Tenants.BeginScope(Tenant);
        var before = rig.Tenants.Scopes;
        var first = await rig.Reader.GetContract360Async(id, CancellationToken.None);
        var second = await rig.Reader.GetContract360Async(id, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, rig.Tenants.Scopes - before);
    }

    [Fact]
    public async Task A_contract_that_does_not_resolve_is_remembered_as_missing_for_the_turn()
    {
        var rig = Create(nameof(A_contract_that_does_not_resolve_is_remembered_as_missing_for_the_turn), memoize: true);

        using var scope = rig.Tenants.BeginScope(Tenant);
        var before = rig.Tenants.Scopes;
        var unknown = Guid.NewGuid();
        Assert.Null(await rig.Reader.GetContract360Async(unknown, CancellationToken.None));
        Assert.Null(await rig.Reader.GetContract360Async(unknown, CancellationToken.None));

        Assert.Equal(1, rig.Tenants.Scopes - before);
    }

    [Fact]
    public async Task With_the_flag_off_every_call_reads_as_before()
    {
        var rig = Create(nameof(With_the_flag_off_every_call_reads_as_before), memoize: false);
        var id = await SeedContractAsync(rig);
        var supplier = EntityId.New();
        rig.Names.Names[supplier] = "Salesforce";

        using var scope = rig.Tenants.BeginScope(Tenant);
        var before = rig.Tenants.Scopes;
        await rig.Reader.GetContract360Async(id, CancellationToken.None);
        await rig.Reader.GetContract360Async(id, CancellationToken.None);
        var contractReads = rig.Tenants.Scopes - before;
        await rig.Reader.ResolveDisplayNameAsync(supplier, ContractDocumentType.Msa, CancellationToken.None);
        await rig.Reader.ResolveDisplayNameAsync(supplier, ContractDocumentType.Msa, CancellationToken.None);
        var unnamedSupplier = EntityId.New(); // no name on file: the key resolves without touching the workspace store
        await rig.Reader.ResolveBenchmarkKeyAsync(unnamedSupplier, CancellationToken.None);
        await rig.Reader.ResolveBenchmarkKeyAsync(unnamedSupplier, CancellationToken.None);
        await rig.Reader.GetDealsBySupplierAsync("Salesforce", CancellationToken.None);
        await rig.Reader.GetDealsBySupplierAsync("Salesforce", CancellationToken.None);

        Assert.Equal(2, contractReads);
        Assert.Equal(4, rig.Names.GetNamesCalls);
        Assert.Equal(["Salesforce", "Salesforce"], rig.Deals.Suppliers);
    }

    // ---------------------------------------------------------------- names, benchmark key, deals

    [Fact]
    public async Task Display_name_and_benchmark_key_read_the_supplier_lookup_once_per_supplier_with_the_flag_on()
    {
        var rig = Create(nameof(Display_name_and_benchmark_key_read_the_supplier_lookup_once_per_supplier_with_the_flag_on), memoize: true);
        var supplier = EntityId.New();
        var other = EntityId.New();
        rig.Names.Names[supplier] = "Salesforce";

        using var scope = rig.Tenants.BeginScope(Tenant);
        Assert.Equal("Salesforce", await rig.Reader.ResolveDisplayNameAsync(supplier, ContractDocumentType.Msa, CancellationToken.None));
        Assert.Equal("Salesforce", await rig.Reader.ResolveDisplayNameAsync(supplier, ContractDocumentType.Msa, CancellationToken.None));
        Assert.Equal(1, rig.Names.GetNamesCalls);

        // Another supplier is another read; the contract type is part of the answer's fallback, so it is part of the key.
        Assert.Equal(ContractDocumentType.Nda.ToString(), await rig.Reader.ResolveDisplayNameAsync(other, ContractDocumentType.Nda, CancellationToken.None));
        Assert.Equal(ContractDocumentType.Msa.ToString(), await rig.Reader.ResolveDisplayNameAsync(other, ContractDocumentType.Msa, CancellationToken.None));
        Assert.Equal(3, rig.Names.GetNamesCalls);

        var unnamedSupplier = EntityId.New();
        var key = await rig.Reader.ResolveBenchmarkKeyAsync(unnamedSupplier, CancellationToken.None);
        var again = await rig.Reader.ResolveBenchmarkKeyAsync(unnamedSupplier, CancellationToken.None);
        Assert.Equal(key, again);
        Assert.Equal(4, rig.Names.GetNamesCalls);
    }

    [Fact]
    public async Task A_missing_supplier_id_is_never_cached_and_never_read()
    {
        var rig = Create(nameof(A_missing_supplier_id_is_never_cached_and_never_read), memoize: true);

        using var scope = rig.Tenants.BeginScope(Tenant);
        Assert.Equal(ContractDocumentType.Msa.ToString(), await rig.Reader.ResolveDisplayNameAsync(null, ContractDocumentType.Msa, CancellationToken.None));
        Assert.Equal((null, null), await rig.Reader.ResolveBenchmarkKeyAsync(null, CancellationToken.None));

        Assert.Equal(0, rig.Names.GetNamesCalls);
    }

    [Fact]
    public async Task Deals_are_read_once_per_supplier_name_with_the_flag_on()
    {
        var rig = Create(nameof(Deals_are_read_once_per_supplier_name_with_the_flag_on), memoize: true);

        var first = await rig.Reader.GetDealsBySupplierAsync("Salesforce", CancellationToken.None);
        var second = await rig.Reader.GetDealsBySupplierAsync("Salesforce", CancellationToken.None);
        await rig.Reader.GetDealsBySupplierAsync("ServiceNow", CancellationToken.None);

        Assert.Same(first, second);
        Assert.Equal(["Salesforce", "ServiceNow"], rig.Deals.Suppliers);
    }

    // ---------------------------------------------------------------- priced lines

    [Fact]
    public async Task Priced_lines_are_computed_once_per_contract_and_key_with_the_flag_on_and_every_time_with_it_off()
    {
        var on = Create(nameof(Priced_lines_are_computed_once_per_contract_and_key_with_the_flag_on_and_every_time_with_it_off) + "-on", memoize: true);
        var off = Create(nameof(Priced_lines_are_computed_once_per_contract_and_key_with_the_flag_on_and_every_time_with_it_off) + "-off", memoize: false);
        var asOf = new DateOnly(2026, 9, 9);

        foreach (var (rig, memoized) in new[] { (on, true), (off, false) })
        {
            var id = await SeedContractAsync(rig);
            using var scope = rig.Tenants.BeginScope(Tenant);
            var contract = await rig.Reader.GetContract360Async(id, CancellationToken.None);
            Assert.NotNull(contract);

            var first = await rig.Reader.ResolvePricedLinesAsync(contract, null, null, asOf, CancellationToken.None);
            var second = await rig.Reader.ResolvePricedLinesAsync(contract, null, null, asOf, CancellationToken.None);
            var otherDay = await rig.Reader.ResolvePricedLinesAsync(contract, null, null, asOf.AddDays(1), CancellationToken.None);

            Assert.Equal(memoized, ReferenceEquals(first, second));
            Assert.False(ReferenceEquals(first, otherDay));
        }
    }

    // ---------------------------------------------------------------- the turn boundary

    [Fact]
    public async Task BeginTurn_forgets_every_read_so_a_reused_scope_never_serves_a_previous_turn()
    {
        var rig = Create(nameof(BeginTurn_forgets_every_read_so_a_reused_scope_never_serves_a_previous_turn), memoize: true);
        var id = await SeedContractAsync(rig);
        var supplier = EntityId.New();
        rig.Names.Names[supplier] = "Salesforce";

        using var scope = rig.Tenants.BeginScope(Tenant);
        var before = rig.Tenants.Scopes;
        var first = await rig.Reader.GetContract360Async(id, CancellationToken.None);
        await rig.Reader.ResolveDisplayNameAsync(supplier, ContractDocumentType.Msa, CancellationToken.None);
        await rig.Reader.GetDealsBySupplierAsync("Salesforce", CancellationToken.None);

        rig.Reader.BeginTurn();

        var second = await rig.Reader.GetContract360Async(id, CancellationToken.None);
        await rig.Reader.ResolveDisplayNameAsync(supplier, ContractDocumentType.Msa, CancellationToken.None);
        await rig.Reader.GetDealsBySupplierAsync("Salesforce", CancellationToken.None);

        Assert.NotSame(first, second);
        Assert.Equal(2, rig.Tenants.Scopes - before);
        Assert.Equal(2, rig.Names.GetNamesCalls);
        Assert.Equal(2, rig.Deals.Suppliers.Count);
    }

    [Fact]
    public async Task A_contract_cached_for_one_tenant_is_never_served_to_another()
    {
        var rig = Create(nameof(A_contract_cached_for_one_tenant_is_never_served_to_another), memoize: true);
        var id = await SeedContractAsync(rig);

        using (rig.Tenants.BeginScope(Tenant))
        {
            Assert.NotNull(await rig.Reader.GetContract360Async(id, CancellationToken.None));
        }

        using (rig.Tenants.BeginScope(new TenantId(Guid.NewGuid())))
        {
            Assert.Null(await rig.Reader.GetContract360Async(id, CancellationToken.None));
        }
    }
}
