using System.Net;
using System.Text.Json;
using Raffa.Api.Tests.TestSupport;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E02/F03/US02/T01 (us-02-contract-360-aggregate) that
/// `GET /api/contracts/{id}` is actually mapped in <c>Program.cs</c> (via
/// <c>ContractsEndpointExtensions</c>) and enforces its request-shape guard clauses — mirrors
/// <see cref="PortfolioEndpointTests"/>/<see cref="ContractCorrectionEndpointTests"/>'s own "not
/// just a placeholder" purpose. Most cases here only exercise branches that return before any
/// database call is made (the tenant-header check and the route-id parse both run before
/// <c>Contract360QueryService</c> is ever called), so — like those two — those need no running
/// Postgres. The success path (real rows, real tab assembly, real tenant scoping) is proven by
/// <c>Raffa.Documents.Contracts.Tests.Contract360QueryServiceTests</c> instead, against a real
/// Postgres+RLS Testcontainer.
///
/// <para>
/// <b>Task E13/F03/US01/T02</b> (requirements R-SUP-04): the header's <c>supplierName</c> is joined
/// on by this endpoint, not by <c>Contract360QueryService</c> (ADR-002 keeps
/// <c>Contract360Header</c> carrying only the id), so only a host-level test can see it — proven
/// here through <see cref="PortfolioEndpointTests.WithSupplierNames"/>, the same shared in-memory
/// harness the sibling portfolio/renewals assertions use.
/// </para>
/// </summary>
public sealed class Contract360EndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public Contract360EndpointTests(WebApplicationFactory<Program> factory)
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

        var response = await client.GetAsync($"/api/contracts/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_tenant_header_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/contracts/{Guid.NewGuid()}");
        request.Headers.Add("X-Tenant-Id", "not-a-guid");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_contract_id_returns_400()
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/contracts/not-a-guid");
        request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ----- supplierName (task E13/F03/US01/T02, requirements R-SUP-04) -----

    [Fact]
    public async Task The_360_header_carries_the_supplier_name_next_to_its_id()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();
        var salesforceId = EntityId.New();

        var factory = PortfolioEndpointTests.WithSupplierNames(
            _factory,
            new Dictionary<EntityId, string> { [salesforceId] = "Salesforce, Inc." });

        var contract = PortfolioEndpointTests.NewContract(tenantId, now, salesforceId);
        await factory.SeedContractAsync(contract);

        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/contracts/{contract.Id.Value}");
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var header = body.RootElement.GetProperty("header");
        Assert.Equal(salesforceId.Value.ToString(), header.GetProperty("supplierId").GetString());
        Assert.Equal("Salesforce, Inc.", header.GetProperty("supplierName").GetString());
    }

    [Fact]
    public async Task A_contract_with_no_supplier_reports_a_null_name_rather_than_omitting_the_field()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();

        var factory = PortfolioEndpointTests.WithSupplierNames(_factory, new Dictionary<EntityId, string>());

        var contract = PortfolioEndpointTests.NewContract(tenantId, now, supplierId: null);
        await factory.SeedContractAsync(contract);

        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/contracts/{contract.Id.Value}");
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var header = body.RootElement.GetProperty("header");

        // Present-and-null, not absent: the web client renders one shape for every contract
        // (the same reason the 360's empty tabs are arrays rather than omitted keys).
        Assert.Equal(JsonValueKind.Null, header.GetProperty("supplierName").ValueKind);
    }
}
