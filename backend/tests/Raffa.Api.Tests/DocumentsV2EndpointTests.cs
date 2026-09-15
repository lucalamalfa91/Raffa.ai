using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Raffa.Api.Tests;

/// <summary>
/// The V2 document endpoints (task E13/F04/US01/T02, story us-01-documents-v2 AC-3/AC-4/AC-5):
/// <c>GET /api/documents</c> (R-DOC-06/09), <c>GET /api/documents/{id}/preview</c> (R-DOC-08),
/// <c>POST /api/documents/{id}/reprocess</c> and <c>DELETE /api/documents/{id}</c> (both Admin,
/// R-DOC-07/R-DOC-10). Runs on the in-memory host with the real fixture gateway, a recording
/// storage and a recording audit writer, so "nothing was deleted" and "one audit row was written"
/// are assertions about behaviour, not about mocks.
///
/// <para>
/// Task E14/F02/US02/T01 (wave w14, ADR-022 w14 footer / ADR-025 §E) deleted the
/// <c>X-Role</c>/<c>X-Workspace-Role</c> header branch of
/// <see cref="Raffa.Api.Infrastructure.WorkspaceRoleResolver"/>: a client-declared role is never an
/// authorization source. The Admin-only assertions below now seed a real
/// <c>workspace_membership</c> row in this same in-memory host (the InMemory
/// <see cref="Raffa.Identity.Workspace.Infrastructure.IdentityWorkspaceDbContext"/>
/// <see cref="TestSupport.InMemoryAskEngineFactory"/> already wires up) rather than relying on a
/// header that no longer grants anything — see
/// <c>Raffa.Api.Tests.DocumentAdminActionsAuthorizationTests</c> for this same story's own
/// dedicated N9 proof, including the creator's real bootstrap-written membership.
/// </para>
/// </summary>
public sealed class DocumentsV2EndpointTests : IClassFixture<RaffaApiFactory>
{
    private const string MsaText =
        "MASTER SERVICES AGREEMENT between Acme Corp and Contoso Ltd, effective 2026-01-01. " +
        "This Agreement governs all Order Forms executed by the parties. Annual fees are EUR 48,000, " +
        "payable within thirty days of invoice. The initial term is thirty-six months and renews " +
        "automatically unless either party gives ninety days written notice.";

    private static readonly DateTimeOffset Now = new(2026, 9, 9, 10, 0, 0, TimeSpan.Zero);
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47];

    private readonly WebApplicationFactory<Program> _baseFactory;

    public DocumentsV2EndpointTests(RaffaApiFactory factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task List_requires_a_tenant_header_and_rejects_an_unknown_status_or_bad_paging()
    {
        var host = CreateHost();
        var client = host.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/documents")).StatusCode);

        var tenantId = Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.BadRequest, (await GetAsync(client, "/api/documents?status=Nope", tenantId)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GetAsync(client, "/api/documents?page=0", tenantId)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GetAsync(client, "/api/documents?pageSize=101", tenantId)).StatusCode);
    }

    [Fact]
    public async Task List_returns_the_tenants_documents_with_the_v2_columns()
    {
        var host = CreateHost();
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var otherTenant = Guid.NewGuid();

        var documentId = await UploadAsync(host, tenantId, "msa.pdf");
        await UploadAsync(host, otherTenant, "not-yours.pdf");

        var response = await GetAsync(client, "/api/documents", tenantId.ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, body.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(25, body.RootElement.GetProperty("pageSize").GetInt32());

        var item = Assert.Single(body.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(documentId, item.GetProperty("id").GetGuid());
        Assert.Equal("msa.pdf", item.GetProperty("fileName").GetString());
        Assert.Equal("Msa", item.GetProperty("documentType").GetString());
        Assert.Equal(1, item.GetProperty("pageCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("stage").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("supplierName").ValueKind);
        Assert.True(item.GetProperty("weakFactCount").GetInt32() >= 0);
        Assert.Equal(JsonValueKind.String, item.GetProperty("contractId").ValueKind);
    }

    [Fact]
    public async Task List_paging_and_status_filter_are_applied()
    {
        var host = CreateHost();
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();

        await UploadAsync(host, tenantId, "one.pdf");
        await UploadAsync(host, tenantId, "two.pdf");

        var firstPage = await ReadJsonAsync(await GetAsync(client, "/api/documents?page=1&pageSize=1", tenantId.ToString()));
        Assert.Equal(2, firstPage.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Single(firstPage.RootElement.GetProperty("items").EnumerateArray());

        var status = firstPage.RootElement.GetProperty("items")[0].GetProperty("processingStatus").GetString();
        var filtered = await ReadJsonAsync(await GetAsync(client, $"/api/documents?status={status}", tenantId.ToString()));
        Assert.Equal(2, filtered.RootElement.GetProperty("totalCount").GetInt32());

        var empty = await ReadJsonAsync(await GetAsync(client, "/api/documents?status=Uploaded", tenantId.ToString()));
        Assert.Equal(status == "Uploaded" ? 2 : 0, empty.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Preview_streams_a_png_for_the_owning_tenant_only()
    {
        var host = CreateHost();
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var documentId = await UploadAsync(host, tenantId, "msa.pdf");

        var response = await GetAsync(client, $"/api/documents/{documentId}/preview", tenantId.ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        var png = await response.Content.ReadAsByteArrayAsync();
        Assert.Equal(PngSignature, png[..4]);

        // Another tenant, and an unknown id, are both 404 — neither is told which.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await GetAsync(client, $"/api/documents/{documentId}/preview", Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await GetAsync(client, $"/api/documents/{Guid.NewGuid()}/preview", tenantId.ToString())).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await GetAsync(client, "/api/documents/not-a-guid/preview", tenantId.ToString())).StatusCode);
    }

    [Fact]
    public async Task Reprocess_is_admin_only_and_reports_the_rerun()
    {
        var host = CreateHost();
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var documentId = await UploadAsync(host, tenantId, "msa.pdf");

        // Procurement (and a caller with no role at all) get 403 — R-DOC-07. A spoofed X-Role
        // header is no longer an authorization source (ADR-025 §E), so it proves nothing here; both
        // callers simply hold no real membership row in this tenant.
        // NW-05 (2026-09-14): the seeded Procurement member is refused (403); a caller with no
        // membership in this tenant is 404, never 403 (ADR-025 Rule B1).
        const string procurementEmail = "buyer@acme.example";
        await SeedMembershipAsync(host, tenantId, procurementEmail, WorkspaceRoleName.Procurement);
        await AssertStatusAsync(
            HttpStatusCode.Forbidden,
            await SendAsync(client, HttpMethod.Post, $"/api/documents/{documentId}/reprocess", tenantId.ToString(), role: null, userId: procurementEmail));
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendAsync(client, HttpMethod.Post, $"/api/documents/{documentId}/reprocess", tenantId.ToString(), role: null, userId: "stranger@elsewhere.example")).StatusCode);

        // A real membership row is what the Admin case now proves (task E14/F02/US02/T01) — the
        // (now-inert) X-Role header is dropped rather than kept as decoration.
        const string adminEmail = "admin@acme.example";
        await SeedMembershipAsync(host, tenantId, adminEmail, WorkspaceRoleName.Admin);
        var response = await SendAsync(
            client, HttpMethod.Post, $"/api/documents/{documentId}/reprocess", tenantId.ToString(), role: null, userId: adminEmail);
        // 202, not 200 (task E16/F02/US03/T01, ADR-027 §D1): the re-run is queued for the Worker.
        // The summary the 200 used to carry (documentType, pagesParsed, chunksIndexed) is now read
        // back through GET /api/documents once the Worker has run -- proven just below.
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(documentId, body.RootElement.GetProperty("documentId").GetGuid());
        Assert.Equal(JsonValueKind.String, body.RootElement.GetProperty("extractionJobId").ValueKind);
        Assert.Equal("Uploaded", body.RootElement.GetProperty("processingStatus").GetString());

        Assert.Contains(
            host.Audit.Entries,
            e => e.Action == "document.reprocessed" && e.Detail != null && e.Detail.Contains("queued", StringComparison.Ordinal));

        // The Worker's half: exactly one pointer was published for the re-run, and processing it
        // leaves the document classified again, with its contract link intact.
        Assert.Equal(1, await host.Factory.DrainExtractionQueueAsync());
        var reread = await ReadJsonAsync(await GetAsync(client, $"/api/documents/{documentId}", tenantId.ToString()));
        Assert.Equal("Msa", reread.RootElement.GetProperty("documentType").GetString());
        Assert.NotEqual("Uploaded", reread.RootElement.GetProperty("processingStatus").GetString());

        // An unknown document is a 404 for an Admin, never a 403.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendAsync(client, HttpMethod.Post, $"/api/documents/{Guid.NewGuid()}/reprocess", tenantId.ToString(), role: null, userId: adminEmail)).StatusCode);
    }

    [Fact]
    public async Task Validate_completes_a_reviewed_document_audits_the_sign_off_and_is_idempotent()
    {
        var host = CreateHost();
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();
        // MsaText names its two parties without a role label, so the fixture extractor proposes the
        // supplier at low confidence and the upload lands in needs_review — a real review to close.
        var documentId = await UploadAsync(host, tenantId, "msa.pdf");
        var before = await ReadJsonAsync(await GetAsync(client, $"/api/documents/{documentId}", tenantId.ToString()));
        Assert.Equal("NeedsReview", before.RootElement.GetProperty("processingStatus").GetString());

        // Procurement signs off — no Admin gate on a review (unlike reprocess/delete).
        // NW-05 (2026-09-14): the reviewer is a real Procurement member, seeded; the X-Role header is
        // inert and stays only as a record of what the caller claims.
        await SeedMembershipAsync(host, tenantId, "buyer@acme.example", WorkspaceRoleName.Procurement);
        var response = await SendJsonAsync(
            client, HttpMethod.Post, $"/api/documents/{documentId}/validate", tenantId.ToString(),
            """{"acceptedFields":["supplier","currency"]}""", "Procurement", "buyer@acme.example");
        var body = await ReadJsonAsync(response);
        Assert.Equal(documentId, body.RootElement.GetProperty("documentId").GetGuid());
        Assert.Equal("Completed", body.RootElement.GetProperty("processingStatus").GetString());
        Assert.False(body.RootElement.GetProperty("alreadyValidated").GetBoolean());
        Assert.Equal(
            ["supplier", "currency"],
            body.RootElement.GetProperty("acceptedFields").EnumerateArray().Select(f => f.GetString()));

        var audit = Assert.Single(host.Audit.Entries, e => e.Action == "document.validated");
        Assert.Equal("buyer@acme.example", audit.Actor);
        Assert.Contains("acceptedFields=supplier,currency", audit.Detail);

        // The list now reports the document as completed — it feeds Ask/Portfolio/Renewals.
        var list = await ReadJsonAsync(await GetAsync(client, "/api/documents", tenantId.ToString()));
        var item = Assert.Single(list.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal("Completed", item.GetProperty("processingStatus").GetString());

        // A second click is a 200 no-op that says so, never an error.
        var again = await ReadJsonAsync(await SendJsonAsync(
            client, HttpMethod.Post, $"/api/documents/{documentId}/validate", tenantId.ToString(), "{}"));
        Assert.True(again.RootElement.GetProperty("alreadyValidated").GetBoolean());

        // Unknown and cross-tenant documents are 404; a malformed id is 400.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendJsonAsync(client, HttpMethod.Post, $"/api/documents/{Guid.NewGuid()}/validate", tenantId.ToString(), "{}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendJsonAsync(client, HttpMethod.Post, $"/api/documents/{documentId}/validate", Guid.NewGuid().ToString(), "{}")).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await SendJsonAsync(client, HttpMethod.Post, "/api/documents/not-a-guid/validate", tenantId.ToString(), "{}")).StatusCode);
    }

    [Fact]
    public async Task Evidence_lists_the_latest_fact_per_field_with_page_span_confidence_and_passage()
    {
        var host = CreateHost();
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var documentId = await UploadAsync(host, tenantId, "msa.pdf");

        var document = await ReadJsonAsync(await GetAsync(client, $"/api/documents/{documentId}", tenantId.ToString()));
        var contractId = document.RootElement.GetProperty("contractId").GetGuid();

        var response = await GetAsync(client, $"/api/contracts/{contractId}/evidence", tenantId.ToString());
        var evidence = (await ReadJsonAsync(response)).RootElement.EnumerateArray().ToList();
        Assert.NotEmpty(evidence);

        // The fixture extractor read these straight from MsaText: quoted span, real page, real
        // confidence, the passage around the span and the file it came from.
        var annualSpend = Assert.Single(evidence, e => e.GetProperty("fieldName").GetString() == "annualSpend");
        Assert.Equal("48000", annualSpend.GetProperty("value").GetString());
        Assert.Equal(1, annualSpend.GetProperty("sourcePage").GetInt32());
        Assert.Equal("EUR 48,000,", annualSpend.GetProperty("sourceSpan").GetString());
        Assert.True(annualSpend.GetProperty("confidence").GetDouble() > 0.9);
        Assert.Equal("msa.pdf", annualSpend.GetProperty("sourceFileName").GetString());
        Assert.Contains("Annual fees are EUR 48,000", annualSpend.GetProperty("passage").GetString());
        Assert.Equal(JsonValueKind.Number, annualSpend.GetProperty("highlightStart").ValueKind);

        // The classification verdict rides along as the `type` fact, with no page or span.
        var type = Assert.Single(evidence, e => e.GetProperty("fieldName").GetString() == "type");
        Assert.Equal("Msa", type.GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Null, type.GetProperty("sourcePage").ValueKind);

        // The unlabelled supplier is proposed below the critical bar: present, weak, reviewable.
        var supplier = Assert.Single(evidence, e => e.GetProperty("fieldName").GetString() == "supplier");
        Assert.Equal("Contoso Ltd", supplier.GetProperty("value").GetString());
        Assert.True(supplier.GetProperty("confidence").GetDouble() < 0.8);

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await GetAsync(client, $"/api/contracts/{Guid.NewGuid()}/evidence", tenantId.ToString())).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await GetAsync(client, $"/api/contracts/{contractId}/evidence", Guid.NewGuid().ToString())).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await GetAsync(client, "/api/contracts/not-a-guid/evidence", tenantId.ToString())).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync($"/api/contracts/{contractId}/evidence")).StatusCode);
    }

    [Fact]
    public async Task Delete_is_admin_only_removes_the_objects_and_audits()
    {
        var host = CreateHost();
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var documentId = await UploadAsync(host, tenantId, "msa.pdf");

        // A spoofed X-Role header proves nothing after task E14/F02/US02/T01 (ADR-025 §E) -- this
        // caller simply holds no real membership row in this tenant.
        // NW-05 (2026-09-14): the role comes from the membership row and nowhere else -- a seeded
        // Procurement member is 403, a caller with no membership here is 404, never 403 (ADR-025 B1).
        const string procurementEmail = "buyer@acme.example";
        await SeedMembershipAsync(host, tenantId, procurementEmail, WorkspaceRoleName.Procurement);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await SendAsync(client, HttpMethod.Delete, $"/api/documents/{documentId}", tenantId.ToString(), role: null, userId: procurementEmail)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendAsync(client, HttpMethod.Delete, $"/api/documents/{documentId}", tenantId.ToString(), role: null, userId: "stranger@elsewhere.example")).StatusCode);
        Assert.Empty(host.Storage.Deleted);

        // A real membership row is what the Admin case now proves.
        const string adminEmail = "admin@acme.example";
        await SeedMembershipAsync(host, tenantId, adminEmail, WorkspaceRoleName.Admin);
        var response = await SendAsync(
            client, HttpMethod.Delete, $"/api/documents/{documentId}", tenantId.ToString(), role: null, userId: adminEmail);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        Assert.Contains(host.Storage.Deleted, path => path.EndsWith("msa.pdf", StringComparison.Ordinal));
        Assert.Contains(host.Storage.Deleted, path => path.EndsWith("/preview/page-1.png", StringComparison.Ordinal));
        Assert.Contains(host.Audit.Entries, e => e.Action == "document.deleted" && e.ResourceId == documentId.ToString());

        // Gone: the metadata read and the list both stop reporting it, and a second delete is 404.
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await GetAsync(client, $"/api/documents/{documentId}", tenantId.ToString())).StatusCode);

        var list = await ReadJsonAsync(await GetAsync(client, "/api/documents", tenantId.ToString()));
        Assert.Equal(0, list.RootElement.GetProperty("totalCount").GetInt32());

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendAsync(client, HttpMethod.Delete, $"/api/documents/{documentId}", tenantId.ToString(), role: null, userId: adminEmail)).StatusCode);
    }

    [Fact]
    public async Task A_caller_with_no_role_header_is_resolved_through_the_workspace_membership()
    {
        var host = CreateHost();
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var documentId = await UploadAsync(host, tenantId, "msa.pdf");

        // The web sends X-User-Id on every call and no role header at all (see web/src/api/client.ts),
        // so this is the branch that decides whether its Admin-only buttons work: the membership row.
        const string adminEmail = "admin@acme.example";
        const string procurementEmail = "buyer@acme.example";
        await SeedMembershipAsync(host, tenantId, adminEmail, WorkspaceRoleName.Admin);
        await SeedMembershipAsync(host, tenantId, procurementEmail, WorkspaceRoleName.Procurement);

        await AssertStatusAsync(
            HttpStatusCode.Forbidden,
            await SendAsync(
                client, HttpMethod.Post, $"/api/documents/{documentId}/reprocess", tenantId.ToString(),
                role: null, userId: procurementEmail));

        // 202 since task E16/F02/US03/T01: the Admin's re-run is queued, not run inline.
        await AssertStatusAsync(
            HttpStatusCode.Accepted,
            await SendAsync(
                client, HttpMethod.Post, $"/api/documents/{documentId}/reprocess", tenantId.ToString(),
                role: null, userId: adminEmail));

        // A membership in another tenant grants nothing here. Under NW-05 that caller is a non-member here: 404, never 403 (ADR-025 B1).
        await AssertStatusAsync(
            HttpStatusCode.NotFound,
            await SendAsync(
                client, HttpMethod.Post, $"/api/documents/{documentId}/reprocess", Guid.NewGuid().ToString(),
                role: null, userId: adminEmail));
    }

    [Fact]
    public async Task Another_tenants_admin_cannot_delete_this_tenants_document()
    {
        var host = CreateHost();
        var client = host.Factory.CreateClient();
        var tenantId = Guid.NewGuid();
        var documentId = await UploadAsync(host, tenantId, "msa.pdf");

        // A *real* Admin membership in a different tenant -- proving tenant scoping on the delete
        // path itself, independent of the Admin gate (a spoofed header would no longer reach this
        // far at all after task E14/F02/US02/T01, ADR-025 §E).
        var foreignTenantId = Guid.NewGuid();
        const string foreignAdminEmail = "foreign-admin@contoso.example";
        await SeedMembershipAsync(host, foreignTenantId, foreignAdminEmail, WorkspaceRoleName.Admin);

        var response = await SendAsync(
            client, HttpMethod.Delete, $"/api/documents/{documentId}", foreignTenantId.ToString(), role: null, userId: foreignAdminEmail);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(host.Storage.Deleted);
    }

    // ----- helpers -----

    private Host CreateHost()
    {
        var gateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), new FixedClock(Now), new AiGatewayOcrOptions()));
        var storage = new RecordingDocumentStorage();
        var audit = new RecordingAuditWriter();
        var factory = _baseFactory.WithInMemoryAskEngine(gateway, new FixedClock(Now), storage, audit);
        return new Host { Factory = factory, Storage = storage, Audit = audit };
    }

    /// <summary>
    /// Uploads and then <em>processes</em> the document. Since task E16/F02/US03/T01 the upload
    /// request returns 201 at the store and the content gate + pipeline run on the Worker; this host
    /// has no Worker, so the helper plays it — <see cref="InMemoryAskEngineFactory.DrainExtractionQueueAsync"/>
    /// runs the real handler on the pointer the upload published. Every test in this class that
    /// asserts on a processed document (type, contract id, evidence, validate, preview) therefore
    /// still proves the full path, not a shortcut.
    /// </summary>
    private static async Task<Guid> UploadAsync(Host host, Guid tenantId, string fileName)
    {
        var client = host.Factory.CreateClient();
        var file = new ByteArrayContent(BuildPdf(MsaText));
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        using var content = new MultipartFormDataContent { { file, "file", fileName } };
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/documents") { Content = content };
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var documentId = body.RootElement.GetProperty("id").GetGuid();

        Assert.Equal(1, await host.Factory.DrainExtractionQueueAsync());
        return documentId;
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string url, string tenantId) =>
        SendAsync(client, HttpMethod.Get, url, tenantId);

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string tenantId,
        string? role = null,
        string userId = ImplicitTenantAdminStartupFilter.Email)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Tenant-Id", tenantId);
        request.Headers.Add("X-User-Id", userId);
        if (role is not null)
        {
            request.Headers.Add("X-Role", role);
        }

        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        string tenantId,
        string json,
        string? role = null,
        string userId = ImplicitTenantAdminStartupFilter.Email)
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Tenant-Id", tenantId);
        request.Headers.Add("X-User-Id", userId);
        if (role is not null)
        {
            request.Headers.Add("X-Role", role);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Seeds one workspace user with one role, the way an invite would.</summary>
    private static async Task SeedMembershipAsync(Host host, Guid tenantId, string email, WorkspaceRoleName roleName)
    {
        using var scope = host.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();

        var tenant = new TenantId(tenantId);
        // ADR-010 w16 footer S16-1: the membership/role predicates match ExternalSubjectId only now
        // -- set it to the same value presented as X-User-Id (-> the `oid` claim), same as `Email`.
        var user = new WorkspaceUser { TenantId = tenant, Email = email, ExternalSubjectId = email, CreatedAt = Now };
        var role = new WorkspaceRole { TenantId = tenant, Name = roleName, CreatedAt = Now };
        db.WorkspaceUsers.Add(user);
        db.WorkspaceRoles.Add(role);
        db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            TenantId = tenant,
            WorkspaceUserId = user.Id,
            WorkspaceRoleId = role.Id,
            CreatedAt = Now,
        });

        await db.SaveChangesAsync();
    }

    /// <summary>Reads a 200 body, failing with the server's own error text when it is not one -
    /// an unhandled exception in a handler is far cheaper to diagnose from the assertion message
    /// than from a bare "Expected OK, actual InternalServerError".</summary>
    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, Excerpt(response, body));
        return JsonDocument.Parse(body);
    }

    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, Excerpt(response, body));
    }

    private static string Excerpt(HttpResponseMessage response, string body) =>
        $"HTTP {(int)response.StatusCode}: {body[..Math.Min(1500, body.Length)]}";

    private static byte[] BuildPdf(string text) => Encoding.Latin1.GetBytes(
        "%PDF-1.4\n" +
        "1 0 obj << /Type /Page >> endobj\n" +
        "2 0 obj << /Length 0 >>\n" +
        "stream\n" +
        $"BT ({text}) Tj ET\n" +
        "endstream\n" +
        "endobj\n" +
        "%%EOF\n");

    private sealed class Host
    {
        public required WebApplicationFactory<Program> Factory { get; init; }
        public required RecordingDocumentStorage Storage { get; init; }
        public required RecordingAuditWriter Audit { get; init; }
    }
}
