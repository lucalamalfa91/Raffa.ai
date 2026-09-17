using System.Net;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Documents.Contracts.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Suppliers;
using Raffa.Suppliers.Products.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for task E02/F03/US01/T01 (us-01-portfolio-list-filters) that
/// `GET /api/contracts` is actually mapped in <c>Program.cs</c> and enforces its request-shape
/// guard clauses — mirrors <see cref="DocumentMetadataEndpointTests"/>'s own "not just a
/// placeholder" purpose. Most cases here only exercise branches that return before any database
/// call is made (the tenant-header check and the AC-2 filter parsing, plus task E02/F03/US01/T02's
/// page/pageSize parsing, all run before <c>PortfolioQueryService</c> is ever called), so — like
/// <see cref="DocumentMetadataEndpointTests"/> — those need no running Postgres. The success path
/// (real rows, real filtering, real tenant scoping, real paging) is proven by
/// <c>Raffa.Documents.Contracts.Tests.PortfolioQueryServiceTests</c> instead, against a real
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
public sealed class PortfolioEndpointTests : IClassFixture<RaffaApiFactory>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PortfolioEndpointTests(RaffaApiFactory factory)
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
        var salesforce = NewContract(tenantId, now, salesforceId);
        var unknown = NewContract(tenantId, now.AddSeconds(-1), unknownSupplierId);
        var unlinkedContract = NewContract(tenantId, now.AddSeconds(-2), supplierId: null);
        await factory.SeedContractAsync(salesforce);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, salesforce.Id));
        await factory.SeedContractAsync(unknown);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, unknown.Id));
        await factory.SeedContractAsync(unlinkedContract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, unlinkedContract.Id));

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

    // ----- category (task E24/F01/US01/T01, story us-01-portfolio-category-backend, closes
    // NW-23/OQ-w17-007) -----

    [Fact]
    public async Task Category_filter_restricts_to_contracts_whose_supplier_has_that_category()
    {
        var now = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();
        var saasSupplierId = EntityId.New();
        var hardwareSupplierId = EntityId.New();

        var factory = WithSupplierCategories(
            _factory,
            new Dictionary<EntityId, string> { [saasSupplierId] = "SaaS", [hardwareSupplierId] = "Hardware" });

        // Distinct CreatedAt values keep the portfolio's ORDER BY deterministic under the InMemory
        // provider, same reason Every_row_carries_the_supplier_name_next_to_its_id does this.
        var saasContract = NewContract(tenantId, now, saasSupplierId);
        var hardwareContract = NewContract(tenantId, now.AddSeconds(-1), hardwareSupplierId);
        var unlinkedContract = NewContract(tenantId, now.AddSeconds(-2), supplierId: null);
        await factory.SeedContractAsync(saasContract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, saasContract.Id));
        await factory.SeedContractAsync(hardwareContract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, hardwareContract.Id));
        await factory.SeedContractAsync(unlinkedContract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, unlinkedContract.Id));

        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/contracts?category=SaaS");
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        // AC-1: only the SaaS-category supplier's contract survives -- the Hardware-category
        // contract and the unlinked one (no supplier to resolve a category from at all) are both
        // narrowed away, never left in by accident. totalCount narrows with items (see
        // FilterByCategoryAsync's own doc comment for why).
        var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(saasSupplierId.Value.ToString(), item.GetProperty("supplierId").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Category_with_no_matching_supplier_returns_empty_not_fabricated()
    {
        var now = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();
        var saasSupplierId = EntityId.New();

        var factory = WithSupplierCategories(
            _factory, new Dictionary<EntityId, string> { [saasSupplierId] = "SaaS" });

        var saasContract = NewContract(tenantId, now, saasSupplierId);
        await factory.SeedContractAsync(saasContract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, saasContract.Id));

        var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/contracts?category=Nonexistent");
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        var response = await client.SendAsync(request);

        // AC-3: a category no supplier on this tenant carries at all still narrows to an empty
        // items array -- 200 with nothing in it, never a fabricated row and never a 404/500.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(0, body.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Blank_category_leaves_the_full_portfolio_unfiltered()
    {
        var now = new DateTimeOffset(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);
        var tenantId = TenantId.New();
        var saasSupplierId = EntityId.New();

        var factory = WithSupplierCategories(
            _factory, new Dictionary<EntityId, string> { [saasSupplierId] = "SaaS" });

        var saasContract = NewContract(tenantId, now, saasSupplierId);
        var unlinkedContract = NewContract(tenantId, now.AddSeconds(-1), supplierId: null);
        await factory.SeedContractAsync(saasContract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, saasContract.Id));
        await factory.SeedContractAsync(unlinkedContract);
        await factory.SeedDocumentAsync(InMemoryAskEngineFactory.NewLinkedDocument(tenantId, unlinkedContract.Id));

        var client = factory.CreateClient();
        // AC-2: an absent `category` -- not exercised here, see Every_row_carries_the_supplier_name_
        // next_to_its_id and the malformed-query theory above -- leaves the full portfolio; a blank
        // value must mean the identical thing (TryParseFilter trims and treats an empty result as
        // "not filtered", never as a literal empty-string match nothing can ever satisfy).
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/contracts?category=");
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("items").EnumerateArray().Count());
        Assert.Equal(2, body.RootElement.GetProperty("totalCount").GetInt32());
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

    /// <summary>Swaps in an InMemory portfolio store plus a <see cref="StubSupplierCategoryLookup"/>
    /// over <paramref name="categoriesById"/> -- same shape as <see cref="WithSupplierNames"/>
    /// above, for <see cref="ISupplierCategoryLookup"/> instead (task E24/F01/US01/T01, story
    /// us-01-portfolio-category-backend). Also stubs <see cref="ISupplierNameLookup"/> to an empty
    /// map: <c>GetPortfolioAsync</c> composes <c>supplierName</c> on every row unconditionally
    /// (regardless of whether a category filter was even supplied), so leaving that port on its
    /// real, Postgres-backed registration would make every test using this helper reach for a
    /// database this project never stands up -- an empty stub keeps that call inert without
    /// asserting anything about names, which is this helper's own tests' business.</summary>
    internal static WebApplicationFactory<Program> WithSupplierCategories(
        WebApplicationFactory<Program> factory, IReadOnlyDictionary<EntityId, string> categoriesById) =>
        factory
            .WithInMemoryAskEngine(new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance))
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ISupplierNameLookup>(new StubSupplierNameLookup(new Dictionary<EntityId, string>()));
                services.AddSingleton<ISupplierCategoryLookup>(new StubSupplierCategoryLookup(categoriesById));
            }));

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
