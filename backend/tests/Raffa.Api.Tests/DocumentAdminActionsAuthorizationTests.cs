using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
/// Task E14/F02/US02/T01 (wave w14, story us-02-admin-role-from-membership) — N9, both halves, over
/// the real HTTP surface, proving what the deletion in
/// <see cref="Raffa.Api.Infrastructure.WorkspaceRoleResolver"/> actually buys: with the header
/// branch of <c>ResolveAsync</c> gone, the workspace creator's own bootstrap-written Admin
/// membership (task E14/F02/US01/T01, ADR-025 §D.2) is what makes Delete and Retry (reprocess)
/// succeed — no new authorization code, exactly as ADR-025 §E predicts — and a real Procurement
/// membership, never a spoofed <c>X-Role</c>/<c>X-Workspace-Role</c> header, is what keeps them at
/// 403 (ADR-022 w14 footer clause 1, ADR-025 §E/Rule E2).
///
/// <para>
/// The Procurement fixture is written directly through <see cref="IdentityWorkspaceDbContext"/>,
/// never through <see cref="WorkspaceMembershipService.InviteAsync"/>: that method grants a live
/// membership at invite time <b>today</b>, but task E15/F01/US01/T01 (phase 3) moves the grant to
/// accept time, which would silently break a fixture built on it — the w14 table's own recorded
/// ordering correction for NW-14 ("the chain NW-02 → NW-01 → NW-14 holds only while InviteAsync
/// still grants on invite").
/// </para>
/// </summary>
public sealed class DocumentAdminActionsAuthorizationTests : IClassFixture<RaffaApiFactory>
{
    private const string MsaText =
        "MASTER SERVICES AGREEMENT between Acme Corp and Contoso Ltd, effective 2026-01-01. " +
        "This Agreement governs all Order Forms executed by the parties. Annual fees are EUR 48,000, " +
        "payable within thirty days of invoice. The initial term is thirty-six months and renews " +
        "automatically unless either party gives ninety days written notice.";

    private static readonly DateTimeOffset Now = new(2026, 9, 11, 9, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _baseFactory;

    public DocumentAdminActionsAuthorizationTests(RaffaApiFactory factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task N9_the_creators_own_bootstrap_membership_makes_delete_and_reprocess_succeed()
    {
        var host = CreateHost();
        var client = host.Factory.CreateClient();
        const string creatorEmail = "founder@acme.example";

        var tenantId = await CreateWorkspaceAsync(client, "Acme Procurement", creatorEmail);
        var deleteTarget = await UploadAsync(host, tenantId, "to-delete.pdf");
        var reprocessTarget = await UploadAsync(host, tenantId, "to-reprocess.pdf");

        var deleteResponse = await SendAsync(
            client, HttpMethod.Delete, $"/api/documents/{deleteTarget}", tenantId, creatorEmail);
        await AssertStatusAsync(HttpStatusCode.NoContent, deleteResponse);
        Assert.Contains(host.Audit.Entries, e => e.Action == "document.deleted");

        var reprocessResponse = await SendAsync(
            client, HttpMethod.Post, $"/api/documents/{reprocessTarget}/reprocess", tenantId, creatorEmail);
        // 202 since task E16/F02/US03/T01: the re-run is queued for the Worker, not run inline.
        await AssertStatusAsync(HttpStatusCode.Accepted, reprocessResponse);
        Assert.Contains(host.Audit.Entries, e => e.Action == "document.reprocessed");
    }

    [Fact]
    public async Task N9_a_procurement_member_gets_403_on_both_with_and_without_a_spoofed_admin_header()
    {
        var host = CreateHost();
        var client = host.Factory.CreateClient();
        const string creatorEmail = "founder@acme.example";
        const string procurementEmail = "buyer@acme.example";

        var tenantId = await CreateWorkspaceAsync(client, "Acme Procurement", creatorEmail);
        // Fixture built straight through the DbContext -- see this class's own doc comment for why
        // InviteAsync is the wrong seam for this fixture.
        await SeedMembershipAsync(host, tenantId, procurementEmail, WorkspaceRoleName.Procurement);
        var deleteTarget = await UploadAsync(host, tenantId, "delete-attempt.pdf");
        var reprocessTarget = await UploadAsync(host, tenantId, "reprocess-attempt.pdf");

        // No role header at all.
        await AssertStatusAsync(
            HttpStatusCode.Forbidden,
            await SendAsync(client, HttpMethod.Delete, $"/api/documents/{deleteTarget}", tenantId, procurementEmail));
        await AssertStatusAsync(
            HttpStatusCode.Forbidden,
            await SendAsync(client, HttpMethod.Post, $"/api/documents/{reprocessTarget}/reprocess", tenantId, procurementEmail));

        // ADR-025 §E Rule E2 / ADR-022 w14 footer clause 1: a header claiming Admin never grants --
        // even sent by a real, live Procurement member of this same tenant. X-Role first, then the
        // X-Workspace-Role alias.
        await AssertStatusAsync(
            HttpStatusCode.Forbidden,
            await SendAsync(client, HttpMethod.Delete, $"/api/documents/{deleteTarget}", tenantId, procurementEmail, role: "Admin"));
        await AssertStatusAsync(
            HttpStatusCode.Forbidden,
            await SendAsync(client, HttpMethod.Post, $"/api/documents/{reprocessTarget}/reprocess", tenantId, procurementEmail, role: "Admin"));
        await AssertStatusAsync(
            HttpStatusCode.Forbidden,
            await SendAsync(client, HttpMethod.Delete, $"/api/documents/{deleteTarget}", tenantId, procurementEmail, workspaceRole: "Admin"));
        await AssertStatusAsync(
            HttpStatusCode.Forbidden,
            await SendAsync(client, HttpMethod.Post, $"/api/documents/{reprocessTarget}/reprocess", tenantId, procurementEmail, workspaceRole: "Admin"));

        // Nothing was ever deleted, and the document is still readable by the real Admin.
        Assert.Empty(host.Storage.Deleted);
        await AssertStatusAsync(
            HttpStatusCode.OK, await SendAsync(client, HttpMethod.Get, $"/api/documents/{deleteTarget}", tenantId, creatorEmail));
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

    private static async Task<Guid> CreateWorkspaceAsync(HttpClient client, string name, string creatorEmail)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces")
        {
            Content = JsonContent.Create(new { name }),
        };
        request.Headers.Add("X-User-Id", creatorEmail);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Uploads and then plays the Worker (task E16/F02/US03/T01): the 201 returns at the
    /// store, so the document is processed here through <see cref="InMemoryAskEngineFactory.DrainExtractionQueueAsync"/>
    /// before the Admin actions are exercised against it — delete must find a preview blob to
    /// remove, reprocess must find a classified document to re-queue.</summary>
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

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        Guid tenantId,
        string userId,
        string? role = null,
        string? workspaceRole = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("X-Tenant-Id", tenantId.ToString());
        request.Headers.Add("X-User-Id", userId);
        if (role is not null)
        {
            request.Headers.Add("X-Role", role);
        }

        if (workspaceRole is not null)
        {
            request.Headers.Add("X-Workspace-Role", workspaceRole);
        }

        return await client.SendAsync(request);
    }

    /// <summary>Reads the response body before asserting, so a failure prints the server's own
    /// error text instead of a bare "Expected Forbidden, actual InternalServerError".</summary>
    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == expected,
            $"HTTP {(int)response.StatusCode}: {body[..Math.Min(1000, body.Length)]}");
    }

    /// <summary>Seeds the Procurement fixture directly through the DbContext -- see this class's own
    /// doc comment for why <see cref="WorkspaceMembershipService.InviteAsync"/> is the wrong seam.</summary>
    private static async Task SeedMembershipAsync(
        Host host, Guid tenantId, string email, WorkspaceRoleName roleName)
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
