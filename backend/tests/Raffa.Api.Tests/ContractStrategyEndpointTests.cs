using System.Net;
using System.Text.Json;
using Raffa.Api.Tests.TestSupport;
using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E21/F03/US01/T01 (NW-62): <c>GET /api/contracts/{id}/strategy</c>
/// returns a representative priced-line band when the shared <c>BenchmarkKeyResolution</c> key
/// is complete and the adapter answers, the explicit insufficiency string when it abstains, and
/// 404s across tenants. Uses the same in-memory host + stub <see cref="IBenchmarkService"/>
/// shape <see cref="RenewalsEndpointTests"/> already established for the sibling market-position
/// wire, so this test does not need the Market database.
/// </summary>
public sealed class ContractStrategyEndpointTests : IClassFixture<RaffaApiFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ContractStrategyEndpointTests(RaffaApiFactory factory)
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
    public async Task When_key_resolves_and_adapter_answers_targets_carry_a_representative_band()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();
        var salesforceId = EntityId.New();

        var stubResult = new BenchmarkResult(
            Distribution: new BenchmarkDistribution(P25: 1000m, P50: 1200m, P75: 1500m),
            Metric: "per seat / year",
            Currency: "USD",
            Confidence: 0.9,
            Source: "stub-fixture",
            UpdatedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ComparisonDimensions: [BenchmarkComparisonDimension.Supplier, BenchmarkComparisonDimension.Geography],
            SampleSize: 15);

        var factory = PortfolioEndpointTests
            .WithSupplierNames(_factory, new Dictionary<EntityId, string> { [salesforceId] = "Salesforce, Inc." })
            .WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBenchmarkService>();
                services.AddSingleton<IBenchmarkService>(new StubBenchmarkService(stubResult));
            }));

        await SeedWorkspaceCountryAsync(factory, tenantId, "GB");

        var contract = PortfolioEndpointTests.NewContract(
            tenantId, now, salesforceId, autoRenewal: true,
            endDate: DateOnly.FromDateTime(now.UtcDateTime).AddDays(90));
        contract.CancellationDeadline = DateOnly.FromDateTime(now.UtcDateTime).AddDays(30);
        contract.RenewalTermMonths = 12;
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));
        await SeedLineItemAsync(factory, tenantId, contract.Id);

        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/contracts/{contract.Id.Value}/strategy");
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var target = Assert.Single(body.RootElement.GetProperty("targets").EnumerateArray());
        Assert.NotEqual(JsonValueKind.Null, target.GetProperty("openingTarget").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, target.GetProperty("acceptableRangeLow").ValueKind);

        var explanation = target.GetProperty("explanation").GetString();
        Assert.NotNull(explanation);
        Assert.Contains("representative", explanation, StringComparison.Ordinal);
        Assert.Contains("stub-fixture", explanation, StringComparison.Ordinal);
        Assert.Contains("n=15", explanation, StringComparison.Ordinal);
        Assert.Contains("2026-01-01", explanation, StringComparison.Ordinal);
        Assert.DoesNotContain("Not determined", explanation, StringComparison.Ordinal);

        Assert.NotEmpty(body.RootElement.GetProperty("whereYouCanPush").EnumerateArray());

        var whenYouMustMove = body.RootElement.GetProperty("whenYouMustMove");
        Assert.Equal(
            contract.CancellationDeadline!.Value.ToString("yyyy-MM-dd"),
            whenYouMustMove.GetProperty("cancellationDeadline").GetString());
        Assert.True(whenYouMustMove.TryGetProperty("daysLeft", out _));
        Assert.True(whenYouMustMove.TryGetProperty("passedDeadline", out _));
    }

    [Fact]
    public async Task When_key_resolver_abstains_targets_state_insufficient_market_data()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();
        var salesforceId = EntityId.New();

        var factory = PortfolioEndpointTests.WithSupplierNames(
            _factory,
            new Dictionary<EntityId, string> { [salesforceId] = "Salesforce, Inc." });

        // No WorkspaceTenant row → BenchmarkKeyResolution returns Incomplete → no adapter call.
        var contract = PortfolioEndpointTests.NewContract(
            tenantId, now, salesforceId, autoRenewal: true,
            endDate: DateOnly.FromDateTime(now.UtcDateTime).AddDays(90));
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));
        await SeedLineItemAsync(factory, tenantId, contract.Id);

        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/contracts/{contract.Id.Value}/strategy");
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var target = Assert.Single(body.RootElement.GetProperty("targets").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, target.GetProperty("openingTarget").ValueKind);
        Assert.Contains(
            "insufficient market data",
            target.GetProperty("explanation").GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            "representative",
            target.GetProperty("explanation").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task When_adapter_abstains_targets_state_insufficient_market_data()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();
        var salesforceId = EntityId.New();

        var stubResult = new BenchmarkResult(
            Distribution: null,
            Metric: "n/a",
            Currency: "USD",
            Confidence: 0d,
            Source: "stub-fixture",
            UpdatedAt: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            ComparisonDimensions: [],
            SampleSize: 3);

        var factory = PortfolioEndpointTests
            .WithSupplierNames(_factory, new Dictionary<EntityId, string> { [salesforceId] = "Salesforce, Inc." })
            .WithWebHostBuilder(b => b.ConfigureTestServices(services =>
            {
                services.RemoveAll<IBenchmarkService>();
                services.AddSingleton<IBenchmarkService>(new StubBenchmarkService(stubResult));
            }));

        await SeedWorkspaceCountryAsync(factory, tenantId, "GB");

        var contract = PortfolioEndpointTests.NewContract(
            tenantId, now, salesforceId, autoRenewal: true,
            endDate: DateOnly.FromDateTime(now.UtcDateTime).AddDays(90));
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));
        await SeedLineItemAsync(factory, tenantId, contract.Id);

        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/contracts/{contract.Id.Value}/strategy");
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var target = Assert.Single(body.RootElement.GetProperty("targets").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, target.GetProperty("openingTarget").ValueKind);
        Assert.Contains(
            "insufficient market data",
            target.GetProperty("explanation").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_contract_in_another_tenant_is_404()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var tenantA = TenantId.New();
        var tenantB = TenantId.New();
        var salesforceId = EntityId.New();

        var factory = PortfolioEndpointTests.WithSupplierNames(
            _factory,
            new Dictionary<EntityId, string> { [salesforceId] = "Salesforce, Inc." });

        var contract = PortfolioEndpointTests.NewContract(
            tenantA, now, salesforceId, autoRenewal: true,
            endDate: DateOnly.FromDateTime(now.UtcDateTime).AddDays(90));
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantA, contract.Id));

        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/contracts/{contract.Id.Value}/strategy");
        request.Headers.Add("X-Tenant-Id", tenantB.Value.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task SeedWorkspaceCountryAsync(
        WebApplicationFactory<Program> factory,
        TenantId tenantId,
        string country)
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

    private static async Task SeedLineItemAsync(
        WebApplicationFactory<Program> factory,
        TenantId tenantId,
        EntityId contractId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
        db.ContractLineItems.Add(new ContractLineItem
        {
            TenantId = tenantId,
            ContractId = contractId,
            Sku = "SKU-1",
            Description = "Sales Cloud Enterprise",
            Quantity = 100m,
            Unit = "seats",
            UnitPrice = 2300m,
            BillingPeriod = "Annual",
            Confidence = 0.9,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private sealed class StubBenchmarkService(BenchmarkResult result) : IBenchmarkService
    {
        public Task<Result<BenchmarkResult>> GetBenchmarkAsync(
            BenchmarkQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<BenchmarkResult>.Success(result));
    }
}
