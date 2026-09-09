using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Contigo.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E01/F09/US01/T01 (r0-integration, AC-1): "Authenticate
/// -&gt; create workspace -&gt; invite -&gt; upload document -&gt; audit event" actually works
/// end-to-end over real HTTP, against the real <c>Contigo.Api</c> composition root and a real,
/// migrated Postgres+RLS database (see <see cref="R0IntegrationFixture"/>) — not a set of
/// isolated per-module proofs that have never been driven together through the one host that
/// will run in `dev`/`demo`.
///
/// Reads response bodies as raw <see cref="JsonElement"/>s rather than typed DTOs deliberately:
/// the endpoints under test return anonymous objects with intentionally lower-cased property
/// names (matching ASP.NET Core's default camelCase JSON policy), so this sidesteps any
/// naming-policy/case-sensitivity mismatch between what the host serializes and what a
/// strongly-typed client-side record would expect to deserialize.
/// </summary>
public sealed class R0EndToEndTests : IClassFixture<R0IntegrationFixture>
{
    private readonly R0IntegrationFixture _fixture;

    public R0EndToEndTests(R0IntegrationFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Workspace_to_upload_to_storage_to_audit_end_to_end()
    {
        var client = _fixture.CreateClient();

        // 1. Create workspace (AC-1 "create workspace").
        var createWorkspaceResponse = await client.PostAsJsonAsync(
            "/api/workspaces", new { name = "Acme Procurement" });
        Assert.Equal(HttpStatusCode.Created, createWorkspaceResponse.StatusCode);
        var tenantId = await ReadGuidPropertyAsync(createWorkspaceResponse, "id");

        // 2. Invite an Admin (AC-1 "invite").
        var inviteResponse = await client.PostAsJsonAsync(
            $"/api/workspaces/{tenantId}/invites", new { email = "admin@acme.example", role = "Admin" });
        Assert.Equal(HttpStatusCode.Created, inviteResponse.StatusCode);

        // 3. Upload a document (AC-1 "upload document"; ADR-009/ADR-011 tenant-scoped storage).
        // Task E13/F04/US01/T01 (documents-admission): POST /api/documents now sniffs the format
        // and runs the admission gate before persisting anything, so this path needs a document
        // that is really a PDF and really reads as a contract — the same born-digital fixture R1
        // uses, rather than a few bytes of placeholder text.
        var fileBytes = R1ExtractionFixtures.BuildBornDigitalPdfBytes();
        using var uploadContent = new MultipartFormDataContent
        {
            { new ByteArrayContent(fileBytes), "file", R1ExtractionFixtures.BornDigitalFileName },
        };
        using var uploadRequest = new HttpRequestMessage(HttpMethod.Post, "/api/documents")
        {
            Content = uploadContent,
        };
        uploadRequest.Headers.Add("X-Tenant-Id", tenantId.ToString());
        var uploadResponse = await client.SendAsync(uploadRequest);
        Assert.Equal(HttpStatusCode.Created, uploadResponse.StatusCode);
        var documentId = await ReadGuidPropertyAsync(uploadResponse, "id");

        // 4. Read the metadata back (AC-1 "storage" -- proves the round trip, not just the write).
        using var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/{documentId}");
        getRequest.Headers.Add("X-Tenant-Id", tenantId.ToString());
        var getResponse = await client.SendAsync(getRequest);
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        // Task E13/F04/US01/T02: an upload now stores two objects under the tenant prefix - the
        // document itself and its rendered first-page preview (R-DOC-08) - so this reads the
        // document blob by name rather than assuming a single save.
        var savedInStorage = _fixture.DocumentStorage.Saved
            .Single(s => s.Path.EndsWith(R1ExtractionFixtures.BornDigitalFileName, StringComparison.Ordinal));
        Assert.StartsWith($"{tenantId:D}/", savedInStorage.Path, StringComparison.Ordinal);
        Assert.Equal(fileBytes, savedInStorage.Content);

        var savedPreview = _fixture.DocumentStorage.Saved
            .Single(s => s.Path.EndsWith("/preview/page-1.png", StringComparison.Ordinal));
        Assert.StartsWith($"{tenantId:D}/", savedPreview.Path, StringComparison.Ordinal);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, savedPreview.Content[..4]);

        // 5. Read the audit trail as an authenticated Admin (AC-1 "audit event").
        using var auditRequest = new HttpRequestMessage(HttpMethod.Get, "/api/audit");
        auditRequest.Headers.Add(TestPrincipalStartupFilter.TenantIdHeaderName, tenantId.ToString());
        auditRequest.Headers.Add(TestPrincipalStartupFilter.RoleHeaderName, "Admin");
        var auditResponse = await client.SendAsync(auditRequest);
        Assert.Equal(HttpStatusCode.OK, auditResponse.StatusCode);

        using var auditDoc = JsonDocument.Parse(await auditResponse.Content.ReadAsStringAsync());
        var events = auditDoc.RootElement.EnumerateArray().ToList();
        Assert.Contains(events, e =>
            e.GetProperty("action").GetString() == "document.uploaded" &&
            e.GetProperty("resourceId").GetString() == documentId.ToString());
    }

    [Fact]
    public async Task Unauthenticated_caller_cannot_read_the_audit_trail()
    {
        var client = _fixture.CreateClient();

        var response = await client.GetAsync("/api/audit");

        // AC-1 "Authenticate" is load-bearing, not decorative: no test-principal headers -> 401,
        // even though the tenant/document machinery underneath is fully working.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    internal static async Task<Guid> ReadGuidPropertyAsync(HttpResponseMessage response, string propertyName)
    {
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty(propertyName).GetGuid();
    }
}
