using System.Net;
using System.Text.Json;
using Raffa.AiFlows.Shared.ContractContext;
using Raffa.Api.Tests.TestSupport;
using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Renewals.Application;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Market;
using Raffa.SharedKernel.Suppliers;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Api.Tests;

/// <summary>
/// <c>GET /api/renewals</c> derives every row's market position from the contract's own stored
/// per-line market comparison (the <c>ContractLineItemMarketPrices</c> rows Contract 360 shows):
/// each matched line's UNIT price against its UNIT-price band, weighted by the line's annual cost.
/// The whole page is read in one go, nothing is re-priced from the board, no benchmark question is
/// asked and no workspace country is needed. In-memory host, no database.
/// </summary>
public sealed class RenewalsMarketBandTests : IClassFixture<RaffaApiFactory>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MarketDate = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

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
    public async Task Each_row_compares_its_matched_lines_unit_prices_to_their_stored_bands_without_repricing()
    {
        var tenantId = TenantId.New();
        var supplier = EntityId.New();
        var benchmark = new CountingBenchmark();
        var matcher = new CountingMatcher();
        var factory = HostWith(new CountingNames(new Dictionary<EntityId, string> { [supplier] = "Salesforce, Inc." }), benchmark, matcher);
        // Deliberately no workspace country: the stored comparison does not need one.

        // One exact line inside its band (annual spend 120 000 would have been far "above" a unit band).
        var inLine = await SeedAsync(factory, tenantId, supplier, 0, Line(unit: 100m, qty: 1200m, 80m, 100m, 120m));

        // A big line above its band and a small line below: weighted by annual cost, the big one wins.
        var weighted = await SeedAsync(factory, tenantId, supplier, 1,
            Line(unit: 150m, qty: 100m, 100m, 110m, 120m, sample: 12),
            Line(unit: 10m, qty: 1m, 20m, 25m, 30m, sample: 7, asOf: MarketDate.AddDays(-30)));

        // Only a similar product's band, compared long ago: still read as stored, labelled as approximate.
        var similar = await SeedAsync(factory, tenantId, supplier, 2,
            Line(unit: 50m, qty: 10m, 60m, 70m, 80m, kind: MarketMatchKind.Similar, stale: true));

        // Nothing matched / nothing stored / a band in another currency / no unit price: no position.
        var unmatched = await SeedAsync(factory, tenantId, supplier, 3, Line(unit: 100m, qty: 1m, 0m, 0m, 0m, matched: false));
        var unpriced = await SeedAsync(factory, tenantId, supplier, 4);
        var otherCurrency = await SeedAsync(factory, tenantId, supplier, 5, Line(unit: 100m, qty: 1m, 80m, 100m, 120m, currency: "EUR"));
        var noUnitPrice = await SeedAsync(factory, tenantId, null, 6, Line(unit: null, qty: 1m, 80m, 100m, 120m));

        var items = await GetRenewalsAsync(factory, tenantId);

        Assert.Equal(7, items.Count);
        Assert.Equal("in line with market — representative (source: market-feed; n=15; as of 2026-01-01)", PositionOf(items[inLine]));
        Assert.Equal("above market — representative (source: market-feed; n=7; as of 2025-12-02)", PositionOf(items[weighted]));
        Assert.Equal(
            "below market — representative (source: market-feed, incl. similar or bundled products; n=15; as of 2026-01-01)",
            PositionOf(items[similar]));
        foreach (var id in new[] { unmatched, unpriced, otherCurrency, noUnitPrice })
        {
            Assert.Equal("insufficient market data", PositionOf(items[id]));
        }

        // Read-only: no matcher call (no re-pricing), no benchmark question, and the stale row untouched.
        Assert.Equal(0, matcher.Calls);
        Assert.Equal(0, benchmark.Calls);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
        var staleRow = await db.ContractLineItemMarketPrices.AsNoTracking().SingleAsync(p => p.ContractId == similar);
        Assert.True(staleRow.CheckedAt < Now - LineItemMarketPriceService.RefreshAfter);
    }

    [Fact]
    public void The_band_weights_lines_by_annual_cost_and_keeps_the_weakest_provenance()
    {
        var older = MarketDate.AddDays(-10);

        // Volume from annual cost ÷ unit price when quantity is missing: 9 000 above vs 100 below.
        var band = RenewalsEndpointExtensions.ResolveMarketBand("USD",
        [
            Stored(unit: 90m, qty: null, annual: 9_000m, 50m, 60m, 70m, sample: 20, asOf: MarketDate),
            Stored(unit: 1m, qty: null, annual: 100m, 2m, 3m, 4m, sample: 4, asOf: older),
        ]);
        Assert.Equal(new ResolvedMarketBand("above market", "market-feed", 4, older), band);

        // At P25 exactly is "below", at P75 exactly is "above" (inclusive, as MarketAssessmentCalculator).
        Assert.Equal("below market", RenewalsEndpointExtensions.ResolveMarketBand("USD", [Stored(unit: 50m, qty: 3m, annual: null, 50m, 60m, 70m)])!.Position);
        Assert.Equal("above market", RenewalsEndpointExtensions.ResolveMarketBand("USD", [Stored(unit: 70m, qty: 3m, annual: null, 50m, 60m, 70m)])!.Position);

        // An inverted band is skipped, not trusted; with nothing usable left there is no position.
        Assert.Null(RenewalsEndpointExtensions.ResolveMarketBand("USD", [Stored(unit: 60m, qty: 1m, annual: null, 70m, 60m, 50m)]));
        Assert.Null(RenewalsEndpointExtensions.ResolveMarketBand("USD", []));

        // A bundle's summed band is labelled as approximate.
        Assert.Equal(
            RenewalsEndpointExtensions.StoredMarketApproximateSource,
            RenewalsEndpointExtensions.ResolveMarketBand("USD", [Stored(unit: 60m, qty: 1m, annual: null, 50m, 60m, 70m, kind: MarketMatchKind.Bundle)])!.Adapter);
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
            new CountingBenchmark());
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

    private WebApplicationFactory<Program> HostWith(CountingNames names, IBenchmarkService benchmark, IMarketPriceMatcher? matcher = null) =>
        PortfolioEndpointTests
            .WithSupplierNames(_factory, new Dictionary<EntityId, string>())
            .WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISupplierNameLookup>();
                services.AddSingleton<ISupplierNameLookup>(names);
                services.RemoveAll<IBenchmarkService>();
                services.AddSingleton(benchmark);
                if (matcher is not null)
                {
                    services.RemoveAll<IMarketPriceMatcher>();
                    services.AddSingleton(matcher);
                }
            }));

    private sealed record SeedLine(
        decimal? Unit, decimal Qty, decimal P25, decimal P50, decimal P75,
        bool Matched, MarketMatchKind Kind, string Currency, int Sample, DateTimeOffset AsOf, bool Stale);

    private static SeedLine Line(
        decimal? unit, decimal qty, decimal p25, decimal p50, decimal p75,
        bool matched = true, MarketMatchKind kind = MarketMatchKind.Exact, string currency = "USD",
        int sample = 15, DateTimeOffset? asOf = null, bool stale = false) =>
        new(unit, qty, p25, p50, p75, matched, kind, currency, sample, asOf ?? MarketDate, stale);

    private static StoredLineMarketComparison Stored(
        decimal? unit, decimal? qty, decimal? annual, decimal p25, decimal p50, decimal p75,
        int? sample = 10, DateTimeOffset? asOf = null, MarketMatchKind kind = MarketMatchKind.Exact) =>
        new(EntityId.New(), unit, qty, annual, "USD", p25, p50, p75, sample, kind, asOf ?? MarketDate);

    private static async Task<EntityId> SeedAsync(
        WebApplicationFactory<Program> factory, TenantId tenantId, EntityId? supplierId, int secondsAgo, params SeedLine[] lines)
    {
        var contract = PortfolioEndpointTests.NewContract(
            tenantId, Now.AddSeconds(-secondsAgo), supplierId, autoRenewal: true,
            endDate: DateOnly.FromDateTime(Now.UtcDateTime).AddDays(90));
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
        foreach (var seed in lines)
        {
            var line = new ContractLineItem
            {
                TenantId = tenantId,
                ContractId = contract.Id,
                Description = "Sales Cloud Unlimited named users",
                Quantity = seed.Qty,
                UnitPrice = seed.Unit,
                AnnualCost = seed.Unit * seed.Qty,
                CreatedAt = Now,
            };
            db.ContractLineItems.Add(line);
            db.ContractLineItemMarketPrices.Add(new ContractLineItemMarketPrice
            {
                TenantId = tenantId,
                ContractId = contract.Id,
                LineItemId = line.Id,
                CheckedAt = seed.Stale ? Now - LineItemMarketPriceService.RefreshAfter - TimeSpan.FromDays(3) : Now,
                RecordId = seed.Matched ? "MKT-1" : null,
                Product = seed.Matched ? "Sales Cloud Unlimited" : null,
                MatchKind = seed.Matched ? seed.Kind : null,
                Currency = seed.Matched ? seed.Currency : null,
                UnitPriceP25 = seed.Matched ? seed.P25 : null,
                UnitPriceP50 = seed.Matched ? seed.P50 : null,
                UnitPriceP75 = seed.Matched ? seed.P75 : null,
                SampleSize = seed.Matched ? seed.Sample : null,
                MarketUpdatedAt = seed.Matched ? seed.AsOf : null,
            });
        }

        await db.SaveChangesAsync();
        return contract.Id;
    }

    private static string? PositionOf(JsonElement item) =>
        item.GetProperty("insightCard").GetProperty("recommendations").GetProperty("marketPosition").GetString();

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

    /// <summary>Counts every benchmark question; the board must not ask any.</summary>
    private sealed class CountingBenchmark : IBenchmarkService
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<Result<BenchmarkResult>> GetBenchmarkAsync(BenchmarkQuery query, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(Result<BenchmarkResult>.Success(new BenchmarkResult(
                Distribution: new BenchmarkDistribution(1m, 2m, 3m),
                Metric: "per seat / year",
                Currency: query.Currency,
                Confidence: 0.9,
                Source: "stub-fixture",
                UpdatedAt: MarketDate,
                ComparisonDimensions: [BenchmarkComparisonDimension.Supplier],
                SampleSize: 15)));
        }
    }

    /// <summary>Counts every market match (a re-pricing); the board must not trigger any.</summary>
    private sealed class CountingMatcher : IMarketPriceMatcher
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public Task<IReadOnlyList<MarketPriceMatch?>> MatchAsync(
            MarketPriceContext context, IReadOnlyList<MarketPriceLine> lines, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<IReadOnlyList<MarketPriceMatch?>>([.. lines.Select(_ => (MarketPriceMatch?)null)]);
        }

        public Task<string?> GetCorpusVersionAsync(CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult<string?>("feed:1");
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
