using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Raffa.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E01/F09/US01/T01 AC-2: "Cross-tenant isolation holds
/// across the whole path" — not just within one module (already exhaustively covered by each
/// module's own RLS test suite, e.g. <c>Raffa.Documents.Contracts.Tests
/// .DocumentQueryServiceTests.Returns_null_for_a_document_that_belongs_to_a_different_tenant</c>),
/// but across the full create-workspace -&gt; upload -&gt; audit chain, driven through the one
/// real host, with two genuinely different, independently-created tenants.
/// </summary>
public sealed class R0CrossTenantIsolationTests : IClassFixture<R0IntegrationFixture>
{
    private readonly R0IntegrationFixture _fixture;

    public R0CrossTenantIsolationTests(R0IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Tenant_b_cannot_read_tenant_as_document_or_audit_trail()
    {
        var client = _fixture.CreateClient();

        var tenantA = await CreateWorkspaceAsync(client, "Tenant A Co");
        var tenantB = await CreateWorkspaceAsync(client, "Tenant B Co");

        using var uploadContent = new MultipartFormDataContent
        {
            // Task E13/F04/US01/T01: an admitted document (the shared born-digital contract
            // fixture) — the admission gate refuses placeholder bytes before any row is written,
            // and this test is about tenant isolation over a document that does exist.
            { new ByteArrayContent(R1ExtractionFixtures.BuildBornDigitalPdfBytes()), "file", R1ExtractionFixtures.BornDigitalFileName },
        };
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, "/api/documents")
        {
            Content = uploadContent,
        };
        uploadRequest.Headers.Add("X-Tenant-Id", tenantA.ToString());
        var uploadResponse = await client.SendAsync(uploadRequest);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var documentId = await R0EndToEndTests.ReadGuidPropertyAsync(uploadResponse, "id");

        // AC-2: tenant B, reading with its own (different, genuinely valid) tenant claim, gets
        // nothing back for tenant A's document -- not a 200 with someone else's data.
        using var getAsTenantB = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/{documentId}");
        getAsTenantB.Headers.Add("X-Tenant-Id", tenantB.ToString());
        var getResponse = await client.SendAsync(getAsTenantB);
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);

        // Sanity check: tenant A itself can still read it back (proves the 404 above is
        // cross-tenant isolation, not a broken document id).
        using var getAsTenantA = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/{documentId}");
        getAsTenantA.Headers.Add("X-Tenant-Id", tenantA.ToString());
        var getAsTenantAResponse = await client.SendAsync(getAsTenantA);
        Assert.Equal(HttpStatusCode.OK, getAsTenantAResponse.StatusCode);

        // AC-2: tenant B's own (real, authenticated) Admin sees an empty audit trail -- tenant
        // A's upload event never crosses the boundary.
        using var auditAsTenantB = new HttpRequestMessage(HttpMethod.Get, "/api/audit");
        auditAsTenantB.Headers.Add(TestPrincipalStartupFilter.TenantIdHeaderName, tenantB.ToString());
        auditAsTenantB.Headers.Add(TestPrincipalStartupFilter.RoleHeaderName, "Admin");
        var auditAsTenantBResponse = await client.SendAsync(auditAsTenantB);
        Assert.Equal(HttpStatusCode.OK, auditAsTenantBResponse.StatusCode);

        using var auditDoc = JsonDocument.Parse(await auditAsTenantBResponse.Content.ReadAsStringAsync());
        var tenantBEvents = auditDoc.RootElement.EnumerateArray().ToList();
        Assert.DoesNotContain(tenantBEvents, e => e.GetProperty("resourceId").GetString() == documentId.ToString());

        // AC-2, other direction: tenant A's own Admin *does* see its own upload event -- proves
        // the empty result above is isolation, not a broken audit-write path.
        using var auditAsTenantA = new HttpRequestMessage(HttpMethod.Get, "/api/audit");
        auditAsTenantA.Headers.Add(TestPrincipalStartupFilter.TenantIdHeaderName, tenantA.ToString());
        auditAsTenantA.Headers.Add(TestPrincipalStartupFilter.RoleHeaderName, "Admin");
        var auditAsTenantAResponse = await client.SendAsync(auditAsTenantA);
        Assert.Equal(HttpStatusCode.OK, auditAsTenantAResponse.StatusCode);

        using var auditAsTenantADoc = JsonDocument.Parse(await auditAsTenantAResponse.Content.ReadAsStringAsync());
        var tenantAEvents = auditAsTenantADoc.RootElement.EnumerateArray().ToList();
        Assert.Contains(tenantAEvents, e => e.GetProperty("resourceId").GetString() == documentId.ToString());
    }

    private static async Task<Guid> CreateWorkspaceAsync(HttpClient client, string name)
    {
        var response = await client.PostAsJsonAsync("/api/workspaces", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await R0EndToEndTests.ReadGuidPropertyAsync(response, "id");
    }
}
