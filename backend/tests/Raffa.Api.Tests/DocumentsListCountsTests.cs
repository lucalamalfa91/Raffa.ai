using System.Net;
using System.Text.Json;
using Raffa.Api.Tests.TestSupport;
using Raffa.Documents.Contracts.Application.Admission;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// Task E16/F02/US03/T01 (wave w15, ADR-027 §D6/§D7): <c>GET /api/documents</c> carries a
/// server-computed <c>counts</c> object and a <c>rejectionReason</c> per row, and accepts
/// <c>status=Rejected</c>. The counts are the numbers the rail badge and the "Needs your attention"
/// toggle render, so the assertions here are about what the wire says, not about a view model:
/// tenant-wide (never page- or filter-scoped), overlapping projections (no sum holds), and
/// <c>Rejected</c> outside <c>all</c> altogether — a rejected file was never added. Rows are seeded
/// straight into the InMemory <c>DocumentsContractsDbContext</c> at explicit statuses, the same
/// shape <see cref="InMemoryAskEngineFactory.SeedContractAsync"/> uses for contracts, because the
/// point is the projection over every status, not the pipeline that produces them (that is
/// <see cref="DocumentUploadEndpointTests"/>' job).
/// </summary>
public sealed class DocumentsListCountsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _factory;

    public DocumentsListCountsTests(WebApplicationFactory<Program> factory)
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
    public async Task Counts_are_tenant_wide_and_ignore_the_status_filter_and_the_page()
    {
        var tenantId = TenantId.New();
        var otherTenant = TenantId.New();

        await SeedDocumentsAsync(
            NewDocument(tenantId, DocumentProcessingStatus.Uploaded, "uploaded.pdf"),
            NewDocument(tenantId, DocumentProcessingStatus.Processing, "processing.pdf"),
            NewDocument(tenantId, DocumentProcessingStatus.NeedsReview, "needs-review.pdf"),
            NewDocument(tenantId, DocumentProcessingStatus.Completed, "completed.pdf"),
            NewDocument(tenantId, DocumentProcessingStatus.Failed, "failed.pdf"),
            Rejected(NewDocument(tenantId, DocumentProcessingStatus.Rejected, "carbonara.pdf")),
            // Another tenant's rows must never leak into this tenant's numbers.
            NewDocument(otherTenant, DocumentProcessingStatus.Completed, "not-yours.pdf"),
            NewDocument(otherTenant, DocumentProcessingStatus.Rejected, "not-yours-either.pdf"));

        // A filtered, one-row page: the page is scoped, the counts are not (ADR-027 §D7 "tenant-wide,
        // never page-wide" — a page-scoped count would be a new lie at page 2). `needsAttention` is
        // "not Completed and not Rejected" (§C9), so it contains the two processing rows; `needsReview`
        // (§C5) is the one NeedsReview row inside it. Overlapping projections: no sum is asserted.
        using var page = await GetAsync($"/api/documents?status=Completed&pageSize=1", tenantId);
        Assert.Equal(1, page.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
        AssertCounts(page.RootElement, all: 5, needsAttention: 4, needsReview: 1, processing: 2, rejected: 1);

        // And the unfiltered read says exactly the same five numbers.
        using var all = await GetAsync("/api/documents", tenantId);
        Assert.Equal(6, all.RootElement.GetProperty("totalCount").GetInt32());
        AssertCounts(all.RootElement, all: 5, needsAttention: 4, needsReview: 1, processing: 2, rejected: 1);

        // rejectionReason is present-and-null on every row that was not rejected — one row shape
        // for the screen (the same reason supplierName/stage are never omitted keys).
        foreach (var item in all.RootElement.GetProperty("items").EnumerateArray())
        {
            var status = item.GetProperty("processingStatus").GetString();
            var reason = item.GetProperty("rejectionReason");
            if (status == "Rejected")
            {
                Assert.Equal("no_readable_text", reason.GetString());
            }
            else
            {
                Assert.Equal(JsonValueKind.Null, reason.ValueKind);
            }
        }
    }

    [Fact]
    public async Task Rejected_is_a_listable_status_with_its_reason_code()
    {
        var tenantId = TenantId.New();
        await SeedDocumentsAsync(
            NewDocument(tenantId, DocumentProcessingStatus.Completed, "completed.pdf"),
            Rejected(NewDocument(tenantId, DocumentProcessingStatus.Rejected, "carbonara.pdf")));

        // ADR-027 §D6: the refusal is a row, so the list's own status filter must know the value —
        // before this task `?status=Rejected` was a 400 ("unknown status").
        using var rejected = await GetAsync("/api/documents?status=Rejected", tenantId);
        var item = Assert.Single(rejected.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("carbonara.pdf", item.GetProperty("fileName").GetString());
        Assert.Equal("Rejected", item.GetProperty("processingStatus").GetString());
        Assert.Equal("no_readable_text", item.GetProperty("rejectionReason").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("stage").ValueKind);
        AssertCounts(rejected.RootElement, all: 1, needsAttention: 0, needsReview: 0, processing: 0, rejected: 1);
    }

    [Fact]
    public async Task An_empty_tenant_reports_five_zeros_present_not_absent()
    {
        using var body = await GetAsync("/api/documents", TenantId.New());

        Assert.Equal(0, body.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Empty(body.RootElement.GetProperty("items").EnumerateArray());
        AssertCounts(body.RootElement, all: 0, needsAttention: 0, needsReview: 0, processing: 0, rejected: 0);
    }

    private static void AssertCounts(JsonElement root, int all, int needsAttention, int needsReview, int processing, int rejected)
    {
        var counts = root.GetProperty("counts");
        Assert.Equal(all, counts.GetProperty("all").GetInt32());
        Assert.Equal(needsAttention, counts.GetProperty("needsAttention").GetInt32());
        Assert.Equal(needsReview, counts.GetProperty("needsReview").GetInt32());
        Assert.Equal(processing, counts.GetProperty("processing").GetInt32());
        Assert.Equal(rejected, counts.GetProperty("rejected").GetInt32());
    }

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

    private static Document NewDocument(TenantId tenantId, DocumentProcessingStatus status, string fileName) =>
        new()
        {
            TenantId = tenantId,
            ContractId = null,
            FileName = fileName,
            MimeType = "application/pdf",
            StoragePath = $"{tenantId.Value}/{fileName}",
            Checksum = $"sha256:{fileName}",
            ProcessingStatus = status,
            CreatedAt = Now,
        };

    /// <summary>The Worker's own §D6 verdict, as <c>ExtractionRequestedHandler.RejectAsync</c>
    /// writes it: a reason code, the detected type and the classify confidence.</summary>
    private static Document Rejected(Document document)
    {
        document.RejectionReason = AdmissionRejectionReason.NoReadableText;
        document.RejectionDetectedType = ContractDocumentType.Other;
        document.RejectionConfidence = 0;
        return document;
    }
}
