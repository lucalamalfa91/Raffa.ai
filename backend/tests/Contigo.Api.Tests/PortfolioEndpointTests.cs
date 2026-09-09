using System.Net;
using System.Text.Json;
using Contigo.AiGateway.Configuration;
using Contigo.AiGateway.Fixtures;
using Contigo.Api.Tests.TestSupport;
using Contigo.Documents.Contracts.Domain;
using Contigo.SharedKernel;
using Contigo.SharedKernel.Suppliers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Contigo.Api.Tests;

/// <summary>
/// Host-level proof for task E02/F03/US01/T01 (us-01-portfolio-list-filters) that
/// `GET /api/contracts` is actually mapped in <c>Program.cs</c> and enforces its request-shape
/// guard clauses — mirrors <see cref="DocumentMetadataEndpointTests"/>'s own "not just a
/// placeholder" purpose. Most cases here only exercise branches that return before any database
/// call is made (the tenant-header check and the AC-2 filter parsing, plus task E02/F03/US01/T02's
/// page/pageSize parsing, all run before <c>PortfolioQueryService</c> is ever called), so — like
/// <see cref="DocumentMetadataEndpointTests"/> — those need no running Postgres. The success path
/// (real rows, real filtering, real tenant scoping, real paging) is proven by
/// <c>Contigo.Documents.Contracts.Tests.PortfolioQueryServiceTests</c> instead, against a real
/// Postgres+RLS Testcontainer.
///
/// <para>
/// <b>Task E13/F03/US01/T02</b> (requirements R-SUP-04, ADR-024 "never a bare SupplierId guid"):
/// <c>supplierName</c> on every row is a composition this endpoint makes and no service-level test
/// can see — <c>PortfolioListItem</c> deliberately carries only the id (ADR-002) — so it is proven
/// here, over real HTTP, via <see cref="InMemoryAskEngineFactory.WithInMemoryAskEngine"/> and a
/// <see cref="StubSupplierNameLookup"/>, the same way this project's other review-pass additions
/// reach a success path without a Testcontainer.
/// </para>
/// </summary>
public sealed class PortfolioEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PortfolioEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "ConnectionStrings:DocumentsContracts",
                "Host=localhost;Port=5432;Database=contigo_dev;Username=contigo;Password=contigo;Include Error Detail=true");
            builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");
        });
    }

    [Fact]
    public async Task Missing_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/api/contracts");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/contracts");
        request.Headers.Add("X-Tenant-Id", "not-a-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("supplierId=not-a-guid")]
    [InlineData("risk=not-a-severity")]
    [InlineData("autoRenewal=not-a-bool")]
    [InlineData("minAnnualSpend=not-a-number")]
    [InlineData("maxAnnualSpend=not-a-number")]
    [InlineData("renewalFrom=not-a-date")]
    [InlineData("renewalTo=not-a-date")]
    [InlineData("page=0")]
    [InlineData("page=-1")]
    [InlineData("page=not-a-number")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=not-a-number")]
    public async Task Malformed_filter_or_page_query_parameter_returns_400(string queryString)
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/contracts?{queryString}");
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ----- supplierName (task E13/F03/US01/T02, requirements R-SUP-04) -----

    [Fact]
    public async Task Every_row_carries_the_supplier_name_next_to_its_id()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();
        var salesforceId = EntityId.New();
        var unknownSupplierId = EntityId.New();

        var factory = WithSupplierNames(
            _factory,
            new Dictionary<EntityId, string> { [salesforceId] = "Salesforce, Inc." });

        // Distinct CreatedAt values keep the portfolio's ORDER BY deterministic under the InMemory
        // provider — same reason ChatEndpointTests' own seeded contracts never tie.
        await factory.SeedContractAsync(NewContract(tenantId, now, salesforceId));
        await factory.SeedContractAsync(NewContract(tenantId, now.AddSeconds(-1), unknownSupplierId));
        await factory.SeedContractAsync(NewContract(tenantId, now.AddSeconds(-2), supplierId: null));

        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/contracts");
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);

        var linked = Assert.Single(items, i => i.GetProperty("supplierId").GetString() == salesforceId.Value.ToString());
        Assert.Equal("Salesforce, Inc.", linked.GetProperty("supplierName").GetString());

        // An id the lookup cannot resolve keeps its id and reports no name — the port's own "never
        // a fabricated placeholder" contract, surfaced honestly rather than as an empty string.
        var dangling = Assert.Single(
            items, i => i.GetProperty("supplierId").GetString() == unknownSupplierId.Value.ToString());
        Assert.Equal(JsonValueKind.Null, dangling.GetProperty("supplierName").ValueKind);

        var unlinked = Assert.Single(items, i => i.GetProperty("supplierId").ValueKind == JsonValueKind.Null);
        Assert.Equal(JsonValueKind.Null, unlinked.GetProperty("supplierName").ValueKind);
    }

    /// <summary>Swaps in an InMemory portfolio store plus a <see cref="StubSupplierNameLookup"/>
    /// over <paramref name="namesById"/>. <see cref="ServiceCollectionServiceExtensions.AddSingleton{T}(IServiceCollection, T)"/>
    /// appended after <c>Program.cs</c>'s own <c>AddSuppliersProductsModule</c> registration wins
    /// for a single <c>GetRequiredService</c> call — the same "last registration wins" shape
    /// <see cref="InMemoryAskEngineFactory"/> already relies on for <c>IAiGateway</c>. Shared with
    /// <see cref="Contract360EndpointTests"/>/<see cref="RenewalsEndpointTests"/>, which assert the
    /// identical composition on their own responses.</summary>
    internal static WebApplicationFactory<Program> WithSupplierNames(
        WebApplicationFactory<Program> factory, IReadOnlyDictionary<EntityId, string> namesById) =>
        factory
            .WithInMemoryAskEngine(new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance))
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
                services.AddSingleton<ISupplierNameLookup>(new StubSupplierNameLookup(namesById))));

    /// <summary>A minimal portfolio row: enough columns for the list (and, with
    /// <paramref name="autoRenewal"/>, for the renewal pipeline) without asserting anything about
    /// fields this task does not touch.</summary>
    internal static Contract NewContract(
        TenantId tenantId,
        DateTimeOffset createdAt,
        EntityId? supplierId,
        bool autoRenewal = false,
        DateOnly? endDate = null) =>
        new()
        {
            TenantId = tenantId,
            SupplierId = supplierId,
            Type = ContractDocumentType.Msa,
            Status = "Completed",
            Currency = "USD",
            AnnualSpend = 120000m,
            AutoRenewal = autoRenewal,
            EndDate = endDate,
            CreatedAt = createdAt,
        };
}
