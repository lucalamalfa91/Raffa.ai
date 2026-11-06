using Raffa.AiFlows.Shared.ContractContext;
using System.Net;
using System.Text.Json;
using Raffa.Api.Tests.TestSupport;
using Raffa.Documents.Contracts.Domain;
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
        Assert.Equal("Completed", item.GetProperty("contractStatus").GetString());
        Assert.Equal("Completed", item.GetProperty("documentProcessingStatus").GetString());

        // Every row carries the same score object GET /api/renewals/{contractId}/priority returns,
        // so the Renewals screen reads one list instead of one priority call per row (the fan-out
        // that exhausted demo's 50-connection server).
        var priority = item.GetProperty("priority");
        Assert.Equal(JsonValueKind.Object, priority.ValueKind);
        Assert.Equal(contract.Id.Value.ToString(), priority.GetProperty("contractId").GetString());
        var totalScore = priority.GetProperty("totalScore").GetDecimal();
        Assert.InRange(totalScore, 0m, 100m);
        Assert.Equal(JsonValueKind.Object, priority.GetProperty("components").GetProperty("timeUrgency").ValueKind);
    }

    [Fact]
    public async Task A_needs_review_auto_renewing_contract_still_appears_with_its_portfolio_status()
    {
        var now = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();
        var salesforceId = EntityId.New();

        var factory = PortfolioEndpointTests.WithSupplierNames(
            _factory,
            new Dictionary<EntityId, string> { [salesforceId] = "Salesforce, Inc." });

        var contract = PortfolioEndpointTests.NewContract(
            tenantId,
            now,
            salesforceId,
            autoRenewal: true,
            endDate: DateOnly.FromDateTime(now.UtcDateTime).AddDays(60));
        contract.Status = "needs_review";
        await factory.SeedContractAsync(contract);
        await factory.SeedDocumentAsync(
            InMemoryAskEngineFactory.NewLinkedDocument(tenantId, contract.Id, DocumentProcessingStatus.NeedsReview));

        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/renewals");
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());

        Assert.Equal("Determined", item.GetProperty("status").GetString());
        Assert.Equal("needs_review", item.GetProperty("contractStatus").GetString());
        Assert.Equal("NeedsReview", item.GetProperty("documentProcessingStatus").GetString());
    }

    // Market position (task E21/F02/US01/T01, NW-22) -- read from the contract's stored per-line
    // comparison -- is covered by RenewalsMarketBandTests.
}
