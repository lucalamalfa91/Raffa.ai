using System.Net;
using System.Text.Json;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.IntegrationTests;

/// <summary>
/// Task E13/F04/US01/T02 (documents-v2-api): the V2 document surface over real HTTP against a real,
/// migrated Postgres+pgvector+RLS host — the same fixture <see cref="R1EndToEndTests"/> uses, so
/// these assertions cover the production composition root, not an in-memory stand-in.
///
/// <para>
/// The one that matters most is <see cref="Reprocess_replaces_unreadable_chunks_and_pages_every_new_one"/>:
/// <c>inputs/requirements.md</c> R-DOC-07 AC-1 is explicit that after a reprocess no embedding row
/// for the tenant starts with <c>%PDF</c>, and AC-2 that every new row carries a 1-based page.
/// </para>
/// </summary>
public sealed class R1DocumentsV2EndToEndTests : IClassFixture<R1IntegrationFixture>
{
    private readonly R1IntegrationFixture _fixture;

    public R1DocumentsV2EndToEndTests(R1IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task List_preview_and_delete_work_over_http_for_the_owning_tenant()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();

        var (documentId, contractId) = await R1EndToEndTests.UploadAndProcessAsync(
            client, tenantId, R1ExtractionFixtures.BuildBornDigitalPdfBytes(),
            R1ExtractionFixtures.BornDigitalFileName, R1ExtractionFixtures.BornDigitalMimeType);

        // R-DOC-06: the list is server-side, tenant-scoped, and carries the V2 columns.
        var listResponse = await R1EndToEndTests.GetAsync(client, "/api/documents", tenantId);
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await R1EndToEndTests.ParseAsync(listResponse);
        Assert.Equal(1, list.GetProperty("totalCount").GetInt32());

        var row = list.GetProperty("items")[0];
        Assert.Equal(documentId, row.GetProperty("id").GetGuid());
        Assert.Equal(contractId, row.GetProperty("contractId").GetGuid());
        Assert.Equal("Msa", row.GetProperty("documentType").GetString());
        Assert.Equal(1, row.GetProperty("pageCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("stage").ValueKind);

        // Another tenant sees nothing at all (RLS + the explicit predicate).
        var otherList = await R1EndToEndTests.ParseAsync(
            await R1EndToEndTests.GetAsync(client, "/api/documents", Guid.NewGuid()));
        Assert.Equal(0, otherList.GetProperty("totalCount").GetInt32());

        // R-DOC-08: a PNG comes back, and only for the owning tenant.
        var preview = await R1EndToEndTests.GetAsync(client, $"/api/documents/{documentId}/preview", tenantId);
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        Assert.Equal("image/png", preview.Content.Headers.ContentType?.MediaType);
        var png = await preview.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await R1EndToEndTests.GetAsync(client, $"/api/documents/{documentId}/preview", Guid.NewGuid())).StatusCode);

        // R-DOC-10: Procurement cannot delete; Admin can, and the row and its objects go.
        // 2026-09-13 (wave w14, E14/F02/US02/T01 deleted the X-Role branch -- ADR-025 SS E, ADR-022
        // w14 footer clause 1): role now comes only from a real workspace_membership row, keyed on
        // X-User-Id (ICallerIdentity). Same seed + header pattern already proven in
        // Raffa.Api.Tests.DocumentsV2EndpointTests -- see that file's own SeedMembershipAsync.
        const string adminEmail = "admin@acme.example";
        const string procurementEmail = "buyer@acme.example";
        await SeedMembershipAsync(tenantId, adminEmail, WorkspaceRoleName.Admin);
        await SeedMembershipAsync(tenantId, procurementEmail, WorkspaceRoleName.Procurement);

        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Delete, $"/api/documents/{documentId}", tenantId, procurementEmail)).StatusCode);
        await AssertStatusAsync(
            HttpStatusCode.NoContent,
            await SendAsync(client, HttpMethod.Delete, $"/api/documents/{documentId}", tenantId, adminEmail));

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await R1EndToEndTests.GetAsync(client, $"/api/documents/{documentId}", tenantId)).StatusCode);
        Assert.Contains(
            _fixture.DocumentStorage.Deleted,
            path => path.EndsWith(R1ExtractionFixtures.BornDigitalFileName, StringComparison.Ordinal));

        using var scope = _fixture.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));

        Assert.Equal(0, await dbContext.Embeddings.CountAsync(e => e.SourceId == new EntityId(documentId)));
        // The contract itself survives its source document (R-DOC-10 "detaches the link").
        Assert.NotNull(await dbContext.Contracts.SingleOrDefaultAsync(c => c.Id == new EntityId(contractId)));
    }

    [Fact]
    public async Task Reprocess_replaces_unreadable_chunks_and_pages_every_new_one()
    {
        var client = _fixture.CreateClient();
        var tenantId = Guid.NewGuid();

        var (documentId, _) = await R1EndToEndTests.UploadAndProcessAsync(
            client, tenantId, R1ExtractionFixtures.BuildScannedImageOcrBytes(),
            R1ExtractionFixtures.ScannedFileName, R1ExtractionFixtures.ScannedMimeType);

        // Put the index back into the state a pre-V2 document is in: raw PDF soup, no page.
        using (var scope = _fixture.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));

            var chunks = await dbContext.Embeddings
                .Where(e => e.SourceId == new EntityId(documentId))
                .ToListAsync();
            Assert.NotEmpty(chunks);
            foreach (var chunk in chunks)
            {
                chunk.ChunkText = "%PDF-1.4 unreadable bytes from the V1 indexer";
                chunk.Page = null;
            }

            await dbContext.SaveChangesAsync();
        }

        // 2026-09-13 (wave w14): same membership-based auth as the delete test above.
        const string adminEmail = "admin@acme.example";
        await SeedMembershipAsync(tenantId, adminEmail, WorkspaceRoleName.Admin);

        var response = await SendAsync(client, HttpMethod.Post, $"/api/documents/{documentId}/reprocess", tenantId, adminEmail);
        await AssertStatusAsync(HttpStatusCode.OK, response);
        var summary = await R1EndToEndTests.ParseAsync(response);
        Assert.Equal(2, summary.GetProperty("pagesParsed").GetInt32());
        Assert.Equal(2, summary.GetProperty("chunksIndexed").GetInt32());

        using (var scope = _fixture.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            using var tenantScope = tenantContext.BeginScope(new TenantId(tenantId));

            var chunks = await dbContext.Embeddings
                .Where(e => e.TenantId == new TenantId(tenantId))
                .OrderBy(e => e.ChunkIndex)
                .ToListAsync();

            Assert.Equal(2, chunks.Count);
            Assert.All(chunks, chunk =>
            {
                Assert.DoesNotContain("%PDF", chunk.ChunkText, StringComparison.Ordinal);
                Assert.DoesNotContain("fixture-ocr:", chunk.ChunkText, StringComparison.Ordinal);
                Assert.NotNull(chunk.Page);
                Assert.True(chunk.Page >= 1);
            });
            Assert.Equal([1, 2], chunks.Select(c => c.Page!.Value).ToArray());
        }
    }

    /// <summary>Asserts a status, quoting the server's own error text when it differs — an
    /// unhandled exception is far cheaper to diagnose from the assertion message than from a bare
    /// "Expected NoContent, actual InternalServerError" in a CI log.</summary>
    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == expected,
            $"HTTP {(int)response.StatusCode}: {body[..Math.Min(2000, body.Length)]}");
    }

    /// <summary>Seeds a real <c>workspace_membership</c> row -- the only role source
    /// <see cref="Raffa.Api.Infrastructure.WorkspaceRoleResolver"/> reads since wave w14
    /// (E14/F02/US02/T01). Same shape as <c>Raffa.Api.Tests.DocumentsV2EndpointTests</c>'s own
    /// <c>SeedMembershipAsync</c>.</summary>
    private async Task SeedMembershipAsync(Guid tenantId, string email, WorkspaceRoleName roleName)
    {
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();

        var tenant = new TenantId(tenantId);
        var user = new WorkspaceUser { TenantId = tenant, Email = email, CreatedAt = DateTimeOffset.UtcNow };
        var role = new WorkspaceRole { TenantId = tenant, Name = roleName, CreatedAt = DateTimeOffset.UtcNow };
        db.WorkspaceUsers.Add(user);
        db.WorkspaceRoles.Add(role);
        db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            TenantId = tenant,
            WorkspaceUserId = user.Id,
            WorkspaceRoleId = role.Id,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    /// <summary>X-User-Id is the only identity source <see cref="ICallerIdentity"/> reads
    /// (ADR-025 SS A2); <paramref name="userId"/> must already hold the real
    /// <c>workspace_membership</c> row a caller <see cref="SeedMembershipAsync"/> wrote, or every
    /// Admin-only endpoint answers 403 with no role resolved at all.</summary>
    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client, HttpMethod method, string url, Guid tenantId, string userId)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        request.Headers.Add("X-User-Id", userId);
        return await client.SendAsync(request);
    }
}
