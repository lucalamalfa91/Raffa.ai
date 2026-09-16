using System.Net;
using System.Text.Json;
using Raffa.Api.Tests.TestSupport;
using Raffa.Benchmark;
using Raffa.Benchmark.Contracts;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E03/F03/US01/T01 (us-01-renewal-dashboard-api) that `GET /api/renewals`
/// is actually mapped in <c>Program.cs</c> and enforces its request-shape guard clause, and for task
/// E03/F01/US02/T02 (priority-explainability) that `GET /api/renewals/{contractId}/priority` does
/// the same — mirrors <see cref="PortfolioEndpointTests"/>'s own "not just a placeholder" purpose.
/// Only exercises branches that return before any database call is made (the tenant-header check,
/// and for the priority route the route-id parse, both run before
/// <c>Raffa.Documents.Contracts.Application.Contract360QueryService</c>/<c>PortfolioQueryService</c>
/// are ever called), so — like <see cref="PortfolioEndpointTests"/>/<see cref="Contract360EndpointTests"/>
/// — this needs no running Postgres. The success path (real rows, real pipeline/insight-card
/// construction, real priority-score composition) is proven at the plain-unit-test level instead —
/// <c>Raffa.Renewals.Tests.RenewalPipelineBuilderTests</c> for the dashboard,
/// <c>Raffa.Renewals.Tests.PriorityScoreCalculatorTests</c> for the priority score itself — per
/// this task's own "Tests required" level (unit, no database).
///
/// <para>
/// <b>Task E13/F03/US01/T02</b> (requirements R-SUP-04): <c>supplierName</c> — on the row and on
/// the §9.3 insight card a user actually reads — is joined on by this endpoint, since neither
/// <c>Raffa.Renewals</c> nor <c>Raffa.Documents.Contracts</c> may reference the Suppliers
/// module (ADR-002). Only a host-level test sees that composition, so it is proven here through
/// <see cref="PortfolioEndpointTests.WithSupplierNames"/>.
/// </para>
/// </summary>
public sealed class RenewalsEndpointTests : IClassFixture<RaffaApiFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RenewalsEndpointTests(RaffaApiFactory factory)
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
    public async Task Missing_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/renewals");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/renewals");
        request.Headers.Add("X-Tenant-Id", "not-a-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ----- GET /api/renewals/{contractId}/priority (task E03/F01/US02/T02) -----

    [Fact]
    public async Task Priority_missing_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/renewals/{Guid.NewGuid()}/priority");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Priority_invalid_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/renewals/{Guid.NewGuid()}/priority");
        request.Headers.Add("X-Tenant-Id", "not-a-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Priority_invalid_contract_id_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/renewals/not-a-guid/priority");
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ----- supplierName (task E13/F03/US01/T02, requirements R-SUP-04) -----

    [Fact]
    public async Task Every_pipeline_row_and_its_insight_card_carry_the_supplier_name()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();
        var salesforceId = EntityId.New();

        var factory = PortfolioEndpointTests.WithSupplierNames(
            _factory,
            new Dictionary<EntityId, string> { [salesforceId] = "Salesforce, Inc." });

        // Only auto-renewing contracts reach this endpoint at all (the filter GetRenewalsAsync
        // pushes into PortfolioQueryService), so the row needs both AutoRenewal and an EndDate.
        var contract = PortfolioEndpointTests.NewContract(
            tenantId,
            now,
            salesforceId,
            autoRenewal: true,
            endDate: DateOnly.FromDateTime(now.UtcDateTime).AddDays(60));
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/renewals");
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal(salesforceId.Value.ToString(), item.GetProperty("supplierId").GetString());
        Assert.Equal("Salesforce, Inc.", item.GetProperty("supplierName").GetString());
        Assert.Equal(
            "Salesforce, Inc.",
            item.GetProperty("insightCard").GetProperty("facts").GetProperty("supplierName").GetString());
    }

    // ----- Market position (task E21/F02/US01/T01, NW-22) -----

    /// <summary>
    /// When the benchmark key resolver cannot resolve the key (no workspace country in this test —
    /// the IdentityWorkspaceDbContext is seeded with no rows), the band is null and the builder
    /// emits <c>"insufficient market data"</c>. Proves the abstention string is on the wire
    /// instead of null (AC-3: never null when a supplier id is present but the adapter abstains).
    /// </summary>
    [Fact]
    public async Task When_key_resolver_abstains_market_position_is_insufficient_market_data()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();
        var salesforceId = EntityId.New();

        var factory = PortfolioEndpointTests.WithSupplierNames(
            _factory,
            new Dictionary<EntityId, string> { [salesforceId] = "Salesforce, Inc." });

        // No WorkspaceTenant row seeded → BenchmarkKeyResolution.ResolveAsync returns Incomplete
        // (country is null) → band is null → MarketPosition = "insufficient market data".
        var contract = PortfolioEndpointTests.NewContract(
            tenantId, now, salesforceId, autoRenewal: true,
            endDate: DateOnly.FromDateTime(now.UtcDateTime).AddDays(90));
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/renewals");
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());

        var marketPosition = item
            .GetProperty("insightCard")
            .GetProperty("recommendations")
            .GetProperty("marketPosition")
            .GetString();

        Assert.Equal("insufficient market data", marketPosition);
    }

    /// <summary>
    /// When the key resolves (supplier name + workspace country both present) and the benchmark
    /// adapter returns a distribution, <c>marketPosition</c> is a non-null representative string
    /// that contains the position label, "representative", the adapter source and the as-of date
    /// (AC-1, AC-2). Uses a stub <see cref="IBenchmarkService"/> so the test runs without the
    /// Market database. Never asserts the exact full string — only the required semantic pieces —
    /// so the format can evolve without breaking this proof.
    /// </summary>
    [Fact]
    public async Task When_key_resolves_and_adapter_answers_market_position_is_representative_string()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();
        var salesforceId = EntityId.New();

        // Stub IBenchmarkService to return a distribution so the endpoint produces a position
        // string. AnnualSpend on the seeded contract is 120 000 (NewContract default). With
        // P25=100 000 and P75=150 000, AnnualSpend falls between them → "in line with market".
        var stubResult = new BenchmarkResult(
            Distribution: new BenchmarkDistribution(P25: 100_000m, P50: 120_000m, P75: 150_000m),
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

        // Seed workspace with a country so BenchmarkKeyResolution resolves a Complete key.
        await SeedWorkspaceCountryAsync(factory, tenantId, "GB");

        var contract = PortfolioEndpointTests.NewContract(
            tenantId, now, salesforceId, autoRenewal: true,
            endDate: DateOnly.FromDateTime(now.UtcDateTime).AddDays(90));
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id));

        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/renewals");
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());

        var marketPosition = item
            .GetProperty("insightCard")
            .GetProperty("recommendations")
            .GetProperty("marketPosition")
            .GetString();

        Assert.NotNull(marketPosition);
        Assert.Contains("in line with market", marketPosition, StringComparison.Ordinal);
        Assert.Contains("representative", marketPosition, StringComparison.Ordinal);
        Assert.Contains("stub-fixture", marketPosition, StringComparison.Ordinal);
        Assert.Contains("n=15", marketPosition, StringComparison.Ordinal);
        Assert.Contains("2026-01-01", marketPosition, StringComparison.Ordinal);
        Assert.DoesNotContain("Not determined", marketPosition, StringComparison.Ordinal);
    }

    /// <summary>
    /// When the key resolves but the adapter abstains (no published distribution —
    /// <see cref="BenchmarkResult.HasSufficientData"/> is false), the wire value is
    /// <c>"insufficient market data"</c>, not null (AC-3). Distinct from the incomplete-key
    /// path above: here supplier name and workspace country are both present.
    /// </summary>
    [Fact]
    public async Task When_adapter_abstains_market_position_is_insufficient_market_data()
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

        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/renewals");
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());

        var marketPosition = item
            .GetProperty("insightCard")
            .GetProperty("recommendations")
            .GetProperty("marketPosition")
            .GetString();

        Assert.Equal("insufficient market data", marketPosition);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a <see cref="WorkspaceTenant"/> row into the in-memory
    /// <see cref="IdentityWorkspaceDbContext"/> so <c>BenchmarkKeyResolution.ResolveAsync</c>
    /// can find the workspace country. The same "resolve the service from the host's own
    /// container, bypass HTTP" pattern <see cref="InMemoryAskEngineFactory.SeedContractAsync"/>
    /// already uses.
    /// </summary>
    private static async Task SeedWorkspaceCountryAsync(
        WebApplicationFactory<Program> factory,
        TenantId tenantId,
        string country)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();
        db.Workspaces.Add(new WorkspaceTenant
        {
            // In V1 workspace == tenant: Id == TenantId invariant (WorkspaceTenant doc comment).
            Id = new EntityId(tenantId.Value),
            TenantId = tenantId,
            Name = "Test Workspace",
            CreatedAt = DateTimeOffset.UtcNow,
            Country = country,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Minimal stub for <see cref="IBenchmarkService"/>: returns the same fixed
    /// <see cref="BenchmarkResult"/> for every query, so the endpoint test controls the
    /// adapter's answer without the Market database.
    /// </summary>
    private sealed class StubBenchmarkService(BenchmarkResult result) : IBenchmarkService
    {
        public Task<Result<BenchmarkResult>> GetBenchmarkAsync(
            BenchmarkQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(Result<BenchmarkResult>.Success(result));
    }
}
