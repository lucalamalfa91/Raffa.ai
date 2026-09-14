using System.Net;
using System.Text.Json;
using Raffa.Api.Tests.TestSupport;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// Task E16/F02/US03/T01 (wave w15, ADR-027 §D9): <c>GET /api/contracts/{id}</c> carries one
/// top-level <c>readiness</c> object and <c>GET /api/contracts</c> a tenant-wide
/// <c>processingDocumentCount</c>, so a screen is <em>told</em> whether an empty tab tree means
/// "extraction found nothing" or "extraction has not run" instead of inferring it from arrays
/// (the inference ADR-012 forbids). <c>state</c> derives from the one definition of
/// <em>validated</em> the wave keeps (§D8: a linked document reached <c>Completed</c>), so every
/// case below seeds documents at explicit statuses against a seeded contract — the same
/// <see cref="InMemoryAskEngineFactory.SeedContractAsync"/> shape
/// <see cref="Contract360EndpointTests"/> already uses — and reads the wire.
/// </summary>
public sealed class ContractReadinessTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public ContractReadinessTests(WebApplicationFactory<Program> factory)
    {
        _factory = PortfolioEndpointTests.WithSupplierNames(
            factory.WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    "ConnectionStrings:DocumentsContracts",
                    "Host=localhost;Port=5432;Database=raffa_dev;Username=raffa;Password=raffa;Include Error Detail=true");
                builder.UseSetting("ConnectionStrings:Storage", "UseDevelopmentStorage=true");
            }),
            new Dictionary<EntityId, string>());
    }

    [Fact]
    public async Task A_contract_with_no_linked_document_is_unavailable()
    {
        var tenantId = TenantId.New();
        var contract = PortfolioEndpointTests.NewContract(tenantId, Now, supplierId: null);
        await _factory.SeedContractAsync(contract);

        using var body = await GetContractAsync(contract.Id, tenantId);

        AssertReadiness(body.RootElement, state: "unavailable", stage: null, documentCount: 0, completedDocumentCount: 0);
    }

    [Fact]
    public async Task A_contract_whose_only_document_is_still_uploaded_is_processing_at_the_uploading_stage()
    {
        var tenantId = TenantId.New();
        var contract = PortfolioEndpointTests.NewContract(tenantId, Now, supplierId: null);
        await _factory.SeedContractAsync(contract);
        await SeedDocumentsAsync(NewDocument(tenantId, contract.Id, DocumentProcessingStatus.Uploaded, "msa.pdf"));

        using var body = await GetContractAsync(contract.Id, tenantId);

        // No extraction job has started yet, so the stage is honestly "Uploading" — the Documents
        // list's own first stage string, never a guessed midpoint (DocumentProcessingStageMap).
        AssertReadiness(body.RootElement, state: "processing", stage: "Uploading", documentCount: 1, completedDocumentCount: 0);
    }

    [Fact]
    public async Task One_completed_document_makes_the_contract_ready_even_while_another_is_processing()
    {
        var tenantId = TenantId.New();
        var contract = PortfolioEndpointTests.NewContract(tenantId, Now, supplierId: null);
        await _factory.SeedContractAsync(contract);
        await SeedDocumentsAsync(
            NewDocument(tenantId, contract.Id, DocumentProcessingStatus.Completed, "msa.pdf"),
            NewDocument(tenantId, contract.Id, DocumentProcessingStatus.Processing, "amendment.pdf"),
            // A document of the same tenant linked to a different contract does not count here.
            NewDocument(tenantId, EntityId.New(), DocumentProcessingStatus.Completed, "other.pdf"));

        using var body = await GetContractAsync(contract.Id, tenantId);

        // `stage` is null unless state is `processing` (ADR-027 §D9) — present-and-null, never omitted.
        AssertReadiness(body.RootElement, state: "ready", stage: null, documentCount: 2, completedDocumentCount: 1);
    }

    [Fact]
    public async Task Terminal_but_never_completed_documents_leave_the_contract_unavailable()
    {
        var tenantId = TenantId.New();
        var contract = PortfolioEndpointTests.NewContract(tenantId, Now, supplierId: null);
        await _factory.SeedContractAsync(contract);
        await SeedDocumentsAsync(
            NewDocument(tenantId, contract.Id, DocumentProcessingStatus.NeedsReview, "msa.pdf"),
            NewDocument(tenantId, contract.Id, DocumentProcessingStatus.Failed, "sow.pdf"));

        using var body = await GetContractAsync(contract.Id, tenantId);

        // §D8's single definition: NeedsReview is not validated, and nothing is still moving, so the
        // empty tab tree really is empty rather than "not yet".
        AssertReadiness(body.RootElement, state: "unavailable", stage: null, documentCount: 2, completedDocumentCount: 0);
    }

    [Fact]
    public async Task Portfolio_reports_the_tenant_wide_processing_document_count_beside_the_page()
    {
        var tenantId = TenantId.New();
        var otherTenant = TenantId.New();
        var contract = PortfolioEndpointTests.NewContract(tenantId, Now, supplierId: null);
        await _factory.SeedContractAsync(contract);
        await SeedDocumentsAsync(
            NewDocument(tenantId, contract.Id, DocumentProcessingStatus.Uploaded, "msa.pdf"),
            NewDocument(tenantId, contract.Id, DocumentProcessingStatus.Processing, "sow.pdf"),
            NewDocument(tenantId, contract.Id, DocumentProcessingStatus.Completed, "done.pdf"),
            // Not linked to any contract yet (classification still queued): still "processing" for
            // the tenant — that is exactly the empty portfolio's "3 documents still processing".
            NewDocument(tenantId, contractId: null, DocumentProcessingStatus.Uploaded, "unlinked.pdf"),
            NewDocument(otherTenant, contractId: null, DocumentProcessingStatus.Uploaded, "not-yours.pdf"));

        using var page = await GetAsync("/api/contracts", tenantId);
        Assert.Equal(1, page.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, page.RootElement.GetProperty("processingDocumentCount").GetInt32());

        // Independent of the filter: a page that matches nothing still says why there is nothing.
        using var empty = await GetAsync($"/api/contracts?supplierId={Guid.NewGuid()}", tenantId);
        Assert.Equal(0, empty.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Empty(empty.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(3, empty.RootElement.GetProperty("processingDocumentCount").GetInt32());
    }

    private static void AssertReadiness(JsonElement root, string state, string? stage, int documentCount, int completedDocumentCount)
    {
        var readiness = root.GetProperty("readiness");
        Assert.Equal(state, readiness.GetProperty("state").GetString());
        if (stage is null)
        {
            Assert.Equal(JsonValueKind.Null, readiness.GetProperty("stage").ValueKind);
        }
        else
        {
            Assert.Equal(stage, readiness.GetProperty("stage").GetString());
        }
        Assert.Equal(documentCount, readiness.GetProperty("documentCount").GetInt32());
        Assert.Equal(completedDocumentCount, readiness.GetProperty("completedDocumentCount").GetInt32());
    }

    private Task<JsonDocument> GetContractAsync(EntityId contractId, TenantId tenantId) =>
        GetAsync($"/api/contracts/{contractId.Value}", tenantId);

    private async Task<JsonDocument> GetAsync(string url, TenantId tenantId)
    {
        var client = _factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Tenant-Id", tenantId.Value.ToString());

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode} {url}: {body}");
        return JsonDocument.Parse(body);
    }

    private async Task SeedDocumentsAsync(params Document[] documents)
    {
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
        dbContext.Documents.AddRange(documents);
        await dbContext.SaveChangesAsync();
    }

    private static Document NewDocument(TenantId tenantId, EntityId? contractId, DocumentProcessingStatus status, string fileName) =>
        new()
        {
            TenantId = tenantId,
            ContractId = contractId,
            FileName = fileName,
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value}/{fileName}",
            Checksum = $"sha256:{fileName}",
            ProcessingStatus = status,
            CreatedAt = Now,
        };
}
