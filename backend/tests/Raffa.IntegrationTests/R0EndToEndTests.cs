using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Raffa.IntegrationTests;

/// <summary>
/// Proves the Definition of Done for task E01/F09/US01/T01 (r0-integration, AC-1): "Authenticate
/// -&gt; create workspace -&gt; invite -&gt; upload document -&gt; audit event" actually works
/// end-to-end over real HTTP, against the real <c>Raffa.Api</c> composition root and a real,
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

        // 1. Create workspace (AC-1 "create workspace"). Task E14/F02/US01/T01 (wave w14, ADR-025
        // §D.2a): the endpoint now requires a presented identity -- the creator becomes this
        // tenant's Admin by virtue of creating it (Rule D.2b), which is also why step 2 below
        // invites a *different* address than the creator's own.
        using var createWorkspaceRequest = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces")
        {
            Content = JsonContent.Create(new { name = "Acme Procurement" }),
        };
        createWorkspaceRequest.Headers.Add("X-User-Id", "admin@acme.example");
        var createWorkspaceResponse = await client.SendAsync(createWorkspaceRequest);
        Assert.Equal(HttpStatusCode.Created, createWorkspaceResponse.StatusCode);
        var tenantId = await ReadGuidPropertyAsync(createWorkspaceResponse, "id");

        // 2. Invite an Admin (AC-1 "invite"). The creator (admin@acme.example) is already this
        // tenant's Admin from step 1, so it is the caller entitled to invite (ADR-025 §D.1a); the
        // invited address must differ from the creator's own or InviteAsync's own "already holds
        // the role" guard would refuse it.
        using var inviteRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{tenantId}/invites")
        {
            Content = JsonContent.Create(new { email = "teammate@acme.example", role = "Admin" }),
        };
        inviteRequest.Headers.Add("X-User-Id", "admin@acme.example");
        var inviteResponse = await client.SendAsync(inviteRequest);
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

        // Fix 2026-09-14 (ADR-027 §D1): the upload returns at the store; the pipeline, and with it
        // the first-page preview asserted below, runs on the Worker. Play the Worker here -- before
        // w15 the preview existed by the time the 201 came back, which is why this assertion went
        // red on `main` with "Sequence contains no matching element".
        Assert.Equal(1, await _fixture.DrainExtractionQueueAsync());

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

        // 5. Read the audit trail as an authenticated Admin (AC-1 "audit event"). Wave w16's NW-08
        // swapped the guard from a simulated claims principal to ICallerContext/WorkspaceRoleResolver
        // (ADR-025 §K): the creator from step 1 is already this tenant's real Admin member, so the
        // same X-User-Id + X-Tenant-Id pair every other tenant-scoped call in this test already
        // sends is what reaches it now -- no test-only claim header left to simulate.
        using var auditRequest = new HttpRequestMessage(HttpMethod.Get, "/api/audit");
        auditRequest.Headers.Add("X-User-Id", "admin@acme.example");
        auditRequest.Headers.Add("X-Tenant-Id", tenantId.ToString());
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
