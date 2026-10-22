using System.Net;
using System.Text.Json;
using Raffa.AiFlows.Shared.ContractContext;
using Raffa.Api.Tests.TestSupport;
using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Api.Tests;

/// <summary>
/// <c>GET /api/renewals</c> resolves the market position of every row. The page's benchmark keys are
/// read in batches (one supplier-name lookup, one workspace-country read) and identical benchmark
/// questions are asked once, instead of two queries and one benchmark call per row -- while every row
/// still gets exactly the position the per-row resolution gave. In-memory host, no database.
/// </summary>
public sealed class RenewalsMarketBandTests : IClassFixture<RaffaApiFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public RenewalsMarketBandTests(RaffaApiFactory factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:DocumentsContracts",
                "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
            builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");
        });
    }

    [Fact]
    public async Task Each_row_keeps_the_position_the_per_row_resolution_gave_and_the_page_is_resolved_in_batches()
    {
        var tenantId = TenantId.New();
        var salesforce = EntityId.New();
        var acme = EntityId.New();
        var dangling = EntityId.New();
        var names = new CountingNames(new Dictionary<EntityId, string>
        {
            [salesforce] = "Salesforce, Inc.",
            [acme] = "Acme Corp",
        });
        var benchmark = new BandBySupplier();
        var factory = HostWith(names, benchmark);
        await SeedWorkspaceCountryAsync(factory, tenantId, "GB");

        // 120 000 a year each (the NewContract default): inside Salesforce's band, below Acme's.
        var rows = new[]
        {
            (Id: await SeedAsync(factory, tenantId, salesforce, 0), Expected: "in line with market"),
            (Id: await SeedAsync(factory, tenantId, salesforce, 1), Expected: "in line with market"),
            (Id: await SeedAsync(factory, tenantId, acme, 2), Expected: "below market"),
            (Id: await SeedAsync(factory, tenantId, dangling, 3), Expected: "insufficient market data"),
            (Id: await SeedAsync(factory, tenantId, null, 4), Expected: "insufficient market data"),
        };

        var items = await GetRenewalsAsync(factory, tenantId);
        var nameLookups = names.Calls;
        var questions = benchmark.Queries;

        Assert.Equal(rows.Length, items.Count);
        foreach (var (id, expected) in rows)
        {
            var position = items[id].GetProperty("insightCard").GetProperty("recommendations").GetProperty("marketPosition").GetString();
            Assert.Contains(expected, position, StringComparison.Ordinal);
        }

        // The per-row resolution gives the same answers: this is what the endpoint did before.
        using (var scope = factory.Services.CreateScope())
        {
            var perRow = scope.ServiceProvider.GetRequiredService<BenchmarkKeyResolution>();
            var batch = await perRow.ResolveManyAsync(tenantId, [salesforce, acme, dangling]);
            foreach (var id in new[] { salesforce, acme, dangling })
            {
                Assert.Equal(await perRow.ResolveAsync(tenantId, id), batch[id]);
            }
        }

        // Two suppliers, so two benchmark questions for five rows (it was one per resolvable row), all
        // asked with the workspace country; the supplier names came in two calls (the page's own
        // lookup and the keys') instead of one plus one per linked row.
        Assert.Equal(["Acme Corp", "Salesforce, Inc."], questions.Select(q => q.Supplier).Order());
        Assert.All(questions, q => Assert.Equal("GB", q.Geography));
        Assert.Equal(2, nameLookups);
    }

    [Fact]
    public async Task Batch_keys_equal_the_per_row_keys_for_every_case_and_never_cross_tenants()
    {
        var withCountry = TenantId.New();
        var otherCountry = TenantId.New();
        var noCountry = TenantId.New();
        var supplier = EntityId.New();
        var unknown = EntityId.New();
        var factory = HostWith(
            new CountingNames(new Dictionary<EntityId, string> { [supplier] = "Salesforce, Inc." }),
            new BandBySupplier());
        await SeedWorkspaceCountryAsync(factory, withCountry, "GB");
        await SeedWorkspaceCountryAsync(factory, otherCountry, "FR");

        using var scope = factory.Services.CreateScope();
        var resolution = scope.ServiceProvider.GetRequiredService<BenchmarkKeyResolution>();

        foreach (var (tenant, country) in new[] { (withCountry, "GB"), (otherCountry, "FR"), (noCountry, (string?)null) })
        {
            var batch = await resolution.ResolveManyAsync(tenant, [supplier, unknown, supplier]);

            Assert.Equal(2, batch.Count);
            foreach (var id in new[] { supplier, unknown })
            {
                Assert.Equal(await resolution.ResolveAsync(tenant, id), batch[id]);
            }

            Assert.Equal(
                country is null
                    ? new BenchmarkKeyResult.Incomplete()
                    : new BenchmarkKeyResult.Complete("Salesforce, Inc.", country),
                batch[supplier]);
            Assert.Equal(new BenchmarkKeyResult.Incomplete(), batch[unknown]);
        }

        Assert.Empty(await resolution.ResolveManyAsync(withCountry, []));
    }

    private WebApplicationFactory<Program> HostWith(CountingNames names, IBenchmarkService benchmark) =>
        PortfolioEndpointTests
            .WithSupplierNames(_factory, new Dictionary<EntityId, string>())
            .WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISupplierNameLookup>();
                services.AddSingleton<ISupplierNameLookup>(names);
                services.RemoveAll<IBenchmarkService>();
                services.AddSingleton(benchmark);
            }));

    private static async Task<EntityId> SeedAsync(
        WebApplicationFactory<Program> factory, TenantId tenantId, EntityId? supplierId, int secondsAgo)
    {
        var contract = PortfolioEndpointTests.NewContract(
            tenantId, Now.AddSeconds(-secondsAgo), supplierId, autoRenewal: true,
            endDate: DateOnly.FromDateTime(Now.UtcDateTime).AddDays(90));
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));
        return contract.Id;
    }

    private static async Task<Dictionary<EntityId, JsonElement>> GetRenewalsAsync(
        WebApplicationFactory<Program> factory, TenantId tenantId)
    {
        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/renewals");
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("items").EnumerateArray()
            .ToDictionary(item => new EntityId(item.GetProperty("contractId").GetGuid()), item => item.Clone());
    }

    private static async Task SeedWorkspaceCountryAsync(
        WebApplicationFactory<Program> factory, TenantId tenantId, string country)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();
        db.Workspaces.Add(new WorkspaceTenant
        {
            Id = new EntityId(tenantId.Value),
            TenantId = tenantId,
            Name = "Test Workspace",
            CreatedAt = DateTimeOffset.UtcNow,
            Country = country,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>A bound that depends on the supplier, and a record of every question asked.</summary>
    private sealed class BandBySupplier : IBenchmarkService
    {
        private readonly object _gate = new();
        private readonly List<BenchmarkQuery> _queries = [];

        public IReadOnlyList<BenchmarkQuery> Queries
        {
            get
            {
                lock (_gate)
                {
                    return [.. _queries];
                }
            }
        }

        public Task<Result<BenchmarkResult>> GetBenchmarkAsync(BenchmarkQuery query, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                _queries.Add(query);
            }

            // Annual spend is 120 000: inside Salesforce's P25-P75, at or below Acme's P25.
            var distribution = query.Supplier == "Acme Corp"
                ? new BenchmarkDistribution(130_000m, 160_000m, 200_000m)
                : new BenchmarkDistribution(100_000m, 120_000m, 150_000m);
            return Task.FromResult(Result<BenchmarkResult>.Success(new BenchmarkResult(
                Distribution: distribution,
                Metric: "per seat / year",
                Currency: query.Currency,
                Confidence: 0.9,
                Source: "stub-fixture",
                UpdatedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                ComparisonDimensions: [BenchmarkComparisonDimension.Supplier, BenchmarkComparisonDimension.Geography],
                SampleSize: 15)));
        }
    }

    /// <summary>Resolves from a fixed map and counts the calls.</summary>
    private sealed class CountingNames(IReadOnlyDictionary<EntityId, string> namesById) : ISupplierNameLookup
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<IReadOnlyDictionary<EntityId, string>> GetNamesAsync(
            TenantId tenantId, IReadOnlyCollection<EntityId> supplierIds, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            IReadOnlyDictionary<EntityId, string> names = supplierIds
                .Where(namesById.ContainsKey)
                .Distinct()
                .ToDictionary(id => id, id => namesById[id]);
            return Task.FromResult(names);
        }

        public Task<EntityId?> FindByNormalizedNameAsync(TenantId tenantId, string normalizedName, CancellationToken cancellationToken) =>
            Task.FromResult<EntityId?>(null);
    }
}
