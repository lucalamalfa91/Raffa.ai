using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.Api.Tests.TestSupport;
using Raffa.Documents.Contracts.Application;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Api.Tests;

/// <summary>
/// Dedicated real-Postgres host fixture for <see cref="MembershipRemovalEndpointTests"/> (task
/// E15/F01/US01/T01, wave w14) — same rationale and shape as
/// <see cref="InvitationLifecycleEndpointFixture"/> (real unique-index enforcement; a swapped-in
/// <see cref="RecordingAuditWriter"/> since removal/re-invite both write real audit rows), plus
/// the Documents/Contracts schema on the same instance: T7a reads <c>GET /api/workspaces</c>,
/// whose host handler joins a validated-contract count per workspace (ADR-026 §D2) — see the
/// comment inside <see cref="InitializeAsync"/>.
/// </summary>
public sealed class MembershipRemovalEndpointFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string AppRoleName = "raffa_membership_removal_app";
    private const string AppRolePassword = "raffa_membership_removal_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    private string _appConnectionString = string.Empty;

    internal RecordingAuditWriter AuditWriter { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var superuserConnectionString = _postgres.GetConnectionString();

        var identityOptions = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(identityOptions, superuserConnectionString);
        await using (var db = new IdentityWorkspaceDbContext(identityOptions.Options))
        {
            await db.Database.MigrateAsync();
        }

        // T7a's `GET /api/workspaces` joins PortfolioQueryService.CountValidatedContractsAsync onto
        // every listed workspace (WorkspaceEndpointExtensions.ListWorkspacesAsync, ADR-026 §D2) --
        // a DocumentsContractsDbContext query, so that module must live on this same instance too,
        // or the host resolves its connection string from the ambient configuration and dials
        // 127.0.0.1:5432, where nothing listens on a CI runner ("Connection refused", a 500 that
        // kept `main` red after wave w14). Same two-module shape WorkspaceDirectoryEndpointFixture
        // already uses; PortfolioQueryService is a sealed class, so it cannot be swapped for a fake.
        var documentsOptions = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(documentsOptions, superuserConnectionString);
        await using (var db = new DocumentsContractsDbContext(documentsOptions.Options))
        {
            await db.Database.MigrateAsync();

            // Granted after both modules' tables exist, so one role covers every table regardless
            // of which module's migration created it.
            await db.Database.ExecuteSqlRawAsync(
                $"""
                CREATE ROLE {AppRoleName} LOGIN PASSWORD '{AppRolePassword}' NOSUPERUSER NOBYPASSRLS;
                GRANT USAGE ON SCHEMA public TO {AppRoleName};
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRoleName};
                """);
        }

        _appConnectionString = new NpgsqlConnectionStringBuilder(superuserConnectionString)
        {
            Username = AppRoleName,
            Password = AppRolePassword,
        }.ConnectionString;
    }

    Task IAsyncLifetime.DisposeAsync() => _postgres.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:IdentityWorkspace", _appConnectionString);
        builder.UseSetting("ConnectionStrings:DocumentsContracts", _appConnectionString);
        // Fix 2026-09-14: same X-User-Id -> `oid` bridge InvitationLifecycleEndpointFixture
        // registers, and for the same reason -- a dedicated-host fixture never sees the one
        // TestSupport.InMemoryAskEngineFactory installs, so after NW-05 every request here
        // authenticated nobody and answered 401.
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IAuditWriter>(AuditWriter);
            services.AddAuthentication(TestUserIdAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestUserIdAuthenticationHandler>(
                    TestUserIdAuthenticationHandler.SchemeName, _ => { });
        });
    }
}

/// <summary>
/// T7 (a/c), T7(b), and T8 (task E15/F01/US01/T01, wave w14; ADR-025 Rule D.5a-d, §H) over the
/// real HTTP pipeline, plus the `DELETE /api/workspaces/{tenantId}/members/{membershipId}`
/// endpoint's own guard shape. <b>T7(b) is authored below and <c>Skip</c>ped with a named
/// reason</b> — the same "write it now, skip it with a named reason, never a silent gap"
/// discipline ADR-025 §H already applies to T14 — see
/// <see cref="GapReasons.MembershipVerifiedReadsNotYetWired"/>'s own doc comment for why a
/// meaningful, non-vacuous proof of "document read/write → 404" needs a membership check this
/// wave never adds to <c>DocumentsEndpointExtensions</c>'s plain read/write paths, and
/// <see cref="RemovedMemberRetrievalTests"/> for the identical gap on the Ask path (T7(d)).
/// </summary>
public sealed class MembershipRemovalEndpointTests : IClassFixture<MembershipRemovalEndpointFixture>
{
    private readonly MembershipRemovalEndpointFixture _fixture;

    public MembershipRemovalEndpointTests(MembershipRemovalEndpointFixture fixture)
    {
        _fixture = fixture;
    }

    // ----- the DELETE endpoint's own guard -----

    [Fact]
    public async Task No_identity_returns_401()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "No Identity Co", "admin@noidentity.example");
        var membershipId = await GetOwnMembershipIdAsync(client, tenantId, "admin@noidentity.example");

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/workspaces/{tenantId}/members/{membershipId}");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Non_member_gets_404_never_403()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Stranger Danger Co", "admin@strangerdanger.example");
        var membershipId = await GetOwnMembershipIdAsync(client, tenantId, "admin@strangerdanger.example");

        var response = await RemoveMemberAsync(client, tenantId, membershipId, "stranger@strangerdanger.example");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task A_procurement_member_removing_someone_gets_403()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Procurement Cannot Co", "admin@proccannot.example");
        await InviteAndAcceptAsync(client, tenantId, "admin@proccannot.example", "buyer@proccannot.example", "Procurement");
        var targetMembershipId = await GetOwnMembershipIdAsync(client, tenantId, "admin@proccannot.example");

        var response = await RemoveMemberAsync(client, tenantId, targetMembershipId, "buyer@proccannot.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_admin_can_remove_a_procurement_member()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Admin Removes Co", "admin@adminremoves.example");
        var buyerMembershipId = await InviteAndAcceptAsync(client, tenantId, "admin@adminremoves.example", "buyer@adminremoves.example", "Procurement");

        var response = await RemoveMemberAsync(client, tenantId, buyerMembershipId, "admin@adminremoves.example");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ----- T7: removal is immediate and total (a, c) -----

    [Fact]
    public async Task T7a_a_removed_member_no_longer_sees_the_tenant_although_their_user_row_survives()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Removal Visibility Co", "admin@removalvis.example");
        var removedMembershipId = await InviteAndAcceptAsync(client, tenantId, "admin@removalvis.example", "removed@removalvis.example", "Legal");

        var beforeRemoval = await ListWorkspacesAsync(client, "removed@removalvis.example");
        Assert.Contains(tenantId.ToString(), beforeRemoval, StringComparison.OrdinalIgnoreCase);

        var removeResponse = await RemoveMemberAsync(client, tenantId, removedMembershipId, "admin@removalvis.example");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        var afterRemoval = await ListWorkspacesAsync(client, "removed@removalvis.example");
        Assert.DoesNotContain(tenantId.ToString(), afterRemoval, StringComparison.OrdinalIgnoreCase);

        // The WorkspaceUser row itself survives (audit continuity; WorkspaceSignIn needs it) --
        // only the membership is gone.
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();
        var tenant = new TenantId(tenantId);
        using var _ = scope.ServiceProvider.GetRequiredService<Raffa.SharedKernel.Tenancy.ITenantContext>().BeginScope(tenant);
        Assert.True(await db.WorkspaceUsers.AnyAsync(u => u.TenantId == tenant && u.Email == "removed@removalvis.example"));
        Assert.False(await db.WorkspaceMemberships.AnyAsync(m => m.Id == new EntityId(removedMembershipId)));
    }

    [Fact]
    public async Task T7c_a_stale_invitation_link_is_revoked_in_the_same_transaction_as_the_removal()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Stale Link Co", "admin@stalelink.example");

        // A second, still-unaccepted invitation for the same person the removal below targets --
        // simulating "invited to a second role, never clicked, then removed from the first".
        var secondInviteBody = await InviteAndParseAsync(client, tenantId, "admin@stalelink.example", "removed@stalelink.example", "Finance");

        // The Legal invitation is accepted (creating the membership that gets removed); the Finance
        // invitation above is deliberately left unaccepted so it is still "live" going into removal
        // -- but ADR-026 §D4's own email-level uniqueness means only one can be live at once, so
        // issue the Legal invite AFTER revoking test setup would collide. Instead: accept a single
        // invitation, then re-invite the same (now-member) person is refused (already a member) --
        // so this test seeds the stale link the realistic way: revoke is implicit revocation via
        // removal, not a second concurrent invite. Revoke the Finance one first to free the email,
        // then invite+accept Legal, matching the one-live-invitation-per-email invariant.
        var revokeSecond = await RevokeInvitationAsync(client, tenantId, secondInviteBody.GetProperty("id").GetGuid(), "admin@stalelink.example");
        Assert.Equal(HttpStatusCode.NoContent, revokeSecond.StatusCode);

        var membershipId = await InviteAndAcceptAsync(client, tenantId, "admin@stalelink.example", "removed@stalelink.example", "Legal");

        // Now the actual stale-link scenario: invite the same person again (a second, distinct
        // pending invitation -- allowed because the Legal one above is already accepted, not live),
        // then remove them before they ever click it.
        var staleInviteBody = await InviteAndParseAsync(client, tenantId, "admin@stalelink.example", "removed@stalelink.example", "Finance");
        var staleToken = ExtractToken(staleInviteBody.GetProperty("acceptUrl").GetString()!);

        var removeResponse = await RemoveMemberAsync(client, tenantId, membershipId, "admin@stalelink.example");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        var acceptResponse = await AcceptInvitationAsync(client, staleToken, "removed@stalelink.example");
        Assert.Equal(HttpStatusCode.NotFound, acceptResponse.StatusCode);
    }

    /// <summary>
    /// T7(b): "a removed member's document read/write → 404" (ADR-025 Rule D.5b/D.5e). Authored
    /// now and <c>Skip</c>ped with a named reason rather than either weakened to something vacuous
    /// or silently dropped — see <see cref="GapReasons.MembershipVerifiedReadsNotYetWired"/> for
    /// the full reasoning (today neither <c>DocumentsEndpointExtensions.GetDocumentAsync</c> nor
    /// its sibling plain-path handler <c>ValidateDocumentAsync</c> consults
    /// <c>workspace_membership</c> at all, so both would answer exactly as if the caller were
    /// still a member). The document is seeded directly through <see cref="DocumentUploadService"/>
    /// in a DI scope — the same "bypass the full pipeline, prove the property" shortcut
    /// <see cref="RemovedMemberRetrievalTests"/> takes for T7(d) — rather than the real multipart
    /// admission gate, which is this test's own concern to seed, not to re-prove. Seeding a real
    /// document (not a random, never-existent id) matters: a nonexistent-document 404 would pass
    /// today for the wrong reason and prove nothing about membership.
    /// </summary>
    [Fact(Skip = GapReasons.MembershipVerifiedReadsNotYetWired)]
    public async Task T7b_a_removed_members_document_read_and_write_calls_both_404()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Removed Document Access Co", "admin@removeddocaccess.example");
        var removedMembershipId = await InviteAndAcceptAsync(client, tenantId, "admin@removeddocaccess.example", "removed@removeddocaccess.example", "Procurement");

        Guid documentId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var uploadService = scope.ServiceProvider.GetRequiredService<DocumentUploadService>();
            using var content = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                "The liability cap under this agreement is CHF 1,000,000."));
            var uploadResult = await uploadService.UploadAsync(
                new TenantId(tenantId), "sample.pdf", "application/pdf", content, "test-actor@example.com", CancellationToken.None);
            Assert.True(uploadResult.IsSuccess);
            documentId = uploadResult.Value.DocumentId.Value;
        }

        var removeResponse = await RemoveMemberAsync(client, tenantId, removedMembershipId, "admin@removeddocaccess.example");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        // Read: GET /api/documents/{id} -- DocumentsEndpointExtensions.GetDocumentAsync today
        // resolves only the X-Tenant-Id header, never the caller's own membership.
        using var readRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/documents/{documentId}");
        readRequest.Headers.Add("X-Tenant-Id", tenantId.ToString());
        readRequest.Headers.Add("X-User-Id", "removed@removeddocaccess.example");
        var readResponse = await client.SendAsync(readRequest);
        Assert.Equal(HttpStatusCode.NotFound, readResponse.StatusCode);

        // Write: POST /api/documents/{id}/validate -- the plain (non-Admin-gated) write path,
        // structurally identical to the read path above (tenant header + RLS only, no membership
        // check); an empty body is valid ("every flagged field was corrected instead").
        using var writeRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/documents/{documentId}/validate")
        {
            Content = JsonContent.Create(new { }),
        };
        writeRequest.Headers.Add("X-Tenant-Id", tenantId.ToString());
        writeRequest.Headers.Add("X-User-Id", "removed@removeddocaccess.example");
        var writeResponse = await client.SendAsync(writeRequest);
        Assert.Equal(HttpStatusCode.NotFound, writeResponse.StatusCode);
    }

    // ----- T8: re-invite after removal -----

    [Fact]
    public async Task T8_re_invite_after_removal_succeeds_at_the_same_role_with_a_different_token()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Re Invite Co", "admin@reinvite.example");
        var originalInviteBody = await InviteAndParseAsync(client, tenantId, "admin@reinvite.example", "boomerang@reinvite.example", "Procurement");
        var originalToken = ExtractToken(originalInviteBody.GetProperty("acceptUrl").GetString()!);

        var acceptResponse = await AcceptInvitationAsync(client, originalToken, "boomerang@reinvite.example");
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);
        var membershipId = await FindMembershipIdAsync(client, tenantId, "admin@reinvite.example", "boomerang@reinvite.example");

        var removeResponse = await RemoveMemberAsync(client, tenantId, membershipId, "admin@reinvite.example");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        var reInviteResponse = await InviteAsync(client, tenantId, "admin@reinvite.example", "boomerang@reinvite.example", "Procurement");
        Assert.Equal(HttpStatusCode.Created, reInviteResponse.StatusCode);

        using var reInviteBody = JsonDocument.Parse(await reInviteResponse.Content.ReadAsStringAsync());
        var newToken = ExtractToken(reInviteBody.RootElement.GetProperty("acceptUrl").GetString()!);
        Assert.NotEqual(originalToken, newToken);

        var reAcceptResponse = await AcceptInvitationAsync(client, newToken, "boomerang@reinvite.example");
        Assert.Equal(HttpStatusCode.OK, reAcceptResponse.StatusCode);
        using var reAcceptBody = JsonDocument.Parse(await reAcceptResponse.Content.ReadAsStringAsync());
        Assert.Equal("Procurement", reAcceptBody.RootElement.GetProperty("role").GetString());
    }

    // ----- helpers -----

    private static async Task<Guid> CreateWorkspaceAsync(HttpClient client, string name, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces")
        {
            Content = JsonContent.Create(new { name }),
        };
        request.Headers.Add("X-User-Id", userId);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private static async Task<HttpResponseMessage> InviteAsync(HttpClient client, Guid tenantId, string adminUserId, string email, string role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{tenantId}/invites")
        {
            Content = JsonContent.Create(new { email, role }),
        };
        request.Headers.Add("X-User-Id", adminUserId);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> InviteAndParseAsync(HttpClient client, Guid tenantId, string adminUserId, string email, string role)
    {
        var response = await InviteAsync(client, tenantId, adminUserId, email, role);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private static async Task<HttpResponseMessage> AcceptInvitationAsync(HttpClient client, string token, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/invites/accept");
        request.Headers.Add("X-Invitation-Token", token);
        request.Headers.Add("X-User-Id", userId);
        return await client.SendAsync(request);
    }

    /// <summary>Invites and immediately accepts <paramref name="email"/>, returning the resulting
    /// membership id (looked up afterward, since the accept response carries the workspace but not
    /// the membership's own id).</summary>
    private static async Task<Guid> InviteAndAcceptAsync(
        HttpClient client, Guid tenantId, string adminUserId, string email, string role)
    {
        var inviteBody = await InviteAndParseAsync(client, tenantId, adminUserId, email, role);
        var token = ExtractToken(inviteBody.GetProperty("acceptUrl").GetString()!);

        var acceptResponse = await AcceptInvitationAsync(client, token, email);
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);

        return await FindMembershipIdAsync(client, tenantId, adminUserId, email);
    }

    private static async Task<Guid> FindMembershipIdAsync(HttpClient client, Guid tenantId, string callerUserId, string memberEmail)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/workspaces/{tenantId}/members");
        request.Headers.Add("X-User-Id", callerUserId);
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var member = body.RootElement.GetProperty("members").EnumerateArray()
            .Single(m => string.Equals(m.GetProperty("email").GetString(), memberEmail, StringComparison.OrdinalIgnoreCase));
        // 2026-09-13: `id` is WorkspaceUser's own id (stable across Active/Invited, ADR-026 SS D3),
        // never an action id -- DELETE .../members/{membershipId} needs `membershipId` specifically.
        return member.GetProperty("membershipId").GetGuid();
    }

    private static Task<Guid> GetOwnMembershipIdAsync(HttpClient client, Guid tenantId, string userId) =>
        FindMembershipIdAsync(client, tenantId, userId, userId);

    private static async Task<HttpResponseMessage> RemoveMemberAsync(HttpClient client, Guid tenantId, Guid membershipId, string callerUserId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/workspaces/{tenantId}/members/{membershipId}");
        request.Headers.Add("X-User-Id", callerUserId);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> RevokeInvitationAsync(HttpClient client, Guid tenantId, Guid invitationId, string adminUserId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/workspaces/{tenantId}/invites/{invitationId}");
        request.Headers.Add("X-User-Id", adminUserId);
        return await client.SendAsync(request);
    }

    private static async Task<string> ListWorkspacesAsync(HttpClient client, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/workspaces");
        request.Headers.Add("X-User-Id", userId);
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        // Quotes the server's own error text on a mismatch -- an unhandled exception is far
        // cheaper to diagnose from the assertion message than from a bare "Expected OK, actual
        // InternalServerError" in a CI log (same helper as R1DocumentsV2EndToEndTests.AssertStatusAsync).
        Assert.True(
            response.StatusCode == HttpStatusCode.OK,
            $"HTTP {(int)response.StatusCode}: {body[..Math.Min(12000, body.Length)]}");
        return body;
    }

    private static string ExtractToken(string acceptUrl) => acceptUrl["/invite/accept#".Length..];
}
