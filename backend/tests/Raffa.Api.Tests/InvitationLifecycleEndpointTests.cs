using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.Api.Tests.TestSupport;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Raffa.Api.Tests;

/// <summary>
/// Dedicated real-Postgres host fixture for <see cref="InvitationLifecycleEndpointTests"/> (task
/// E15/F01/US01/T01, wave w14). T11's own concurrent-accept race and T10/T11's "translate the
/// unique-index violation to 409, not 500" both need a real unique constraint — the EF Core
/// InMemory provider this project's other endpoint tests swap onto
/// (<c>TestSupport.InMemoryAskEngineFactory</c>) does not enforce one — the same reason
/// <c>WorkspaceDirectoryEndpointFixture</c> stood up its own dedicated Postgres Testcontainer
/// instead of reusing that swap. Mirrors that fixture's shape, narrowed to
/// <c>ConnectionStrings:IdentityWorkspace</c> alone: none of this class's tests reach Documents/
/// Contracts. Every successful issue/accept/revoke in this task's own code writes a real
/// <see cref="IAuditWriter"/> entry, unlike <c>WorkspaceDirectoryEndpointFixture</c>'s own tests —
/// so, like <c>TestSupport.InMemoryAskEngineFactory</c>, this fixture swaps in a
/// <see cref="RecordingAuditWriter"/> rather than leaving <c>ConnectionStrings:Audit</c> pointed at
/// appsettings.Development.json's static, never-reachable-here default (which every write below
/// would otherwise try, and fail, to dial for real).
/// </summary>
public sealed class InvitationLifecycleEndpointFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string AppRoleName = "raffa_invitation_lifecycle_app";
    private const string AppRolePassword = "raffa_invitation_lifecycle_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    private string _appConnectionString = string.Empty;

    /// <summary>Every <see cref="Raffa.SharedKernel.AuditEntry"/> this host wrote, across every test
    /// in the class (the fixture is shared, xunit's own <c>IClassFixture</c> convention) — T10's own
    /// "the token appears in no audit row" checks for its own per-test secret's absence across the
    /// whole accumulated set, which is robust to that sharing.</summary>
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
        builder.ConfigureTestServices(services => services.AddSingleton<IAuditWriter>(AuditWriter));
    }
}

/// <summary>
/// T10, T11, T12 (task E15/F01/US01/T01, wave w14; ADR-025 §C/§D.1/§D.3, §H) over the real HTTP
/// pipeline: issue → pre-accept → accept, the token's own hygiene, and the two hardening cases
/// (a malformed prefix, and a well-formed foreign-tenant prefix with the wrong secret).
/// </summary>
public sealed class InvitationLifecycleEndpointTests : IClassFixture<InvitationLifecycleEndpointFixture>
{
    private readonly InvitationLifecycleEndpointFixture _fixture;

    public InvitationLifecycleEndpointTests(InvitationLifecycleEndpointFixture fixture)
    {
        _fixture = fixture;
    }

    // ----- T10: token hygiene -----

    [Fact]
    public async Task T10_the_stored_column_is_a_hash_and_the_token_never_appears_in_the_database()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Hygiene Co", "admin@hygiene.example");
        var inviteBody = await InviteAndParseAsync(client, tenantId, "admin@hygiene.example", "invitee@hygiene.example", "Procurement");

        var token = ExtractToken(inviteBody.GetProperty("acceptUrl").GetString()!);
        var secret = token.Split('.', 2)[1];

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();
        var tenant = new TenantId(tenantId);
        using var _ = scope.ServiceProvider.GetRequiredService<Raffa.SharedKernel.Tenancy.ITenantContext>().BeginScope(tenant);
        var invitation = await db.WorkspaceInvitations.SingleAsync(i => i.TenantId == tenant);

        Assert.NotEqual(token, invitation.TokenHash);
        Assert.NotEqual(secret, invitation.TokenHash);
        Assert.DoesNotContain(secret, invitation.TokenHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T10_the_pre_accept_response_carries_only_name_role_and_expiry()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Minimal Disclosure Co", "admin@minimal.example");
        var inviteBody = await InviteAndParseAsync(client, tenantId, "admin@minimal.example", "secret-invitee@minimal.example", "Legal");
        var token = ExtractToken(inviteBody.GetProperty("acceptUrl").GetString()!);

        var response = await GetInvitationAsync(client, token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(raw);
        var properties = body.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.Equal(["expiresAt", "role", "workspaceName"], properties);
        Assert.Equal("Legal", body.RootElement.GetProperty("role").GetString());
        Assert.Equal("Minimal Disclosure Co", body.RootElement.GetProperty("workspaceName").GetString());
        // The invited email never round-trips through pre-accept -- echoing it would turn a leaked
        // link into an address-discovery tool (Rule D.3e).
        Assert.DoesNotContain("secret-invitee", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task T10_the_token_appears_in_no_audit_row()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Audit Clean Co", "admin@auditclean.example");
        var inviteBody = await InviteAndParseAsync(client, tenantId, "admin@auditclean.example", "auditee@auditclean.example", "Finance");
        var token = ExtractToken(inviteBody.GetProperty("acceptUrl").GetString()!);
        var secret = token.Split('.', 2)[1];

        var acceptResponse = await AcceptInvitationAsync(client, token, "auditee@auditclean.example");
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);

        // ADR-025 §G: never the token, never its hash, never a raw header dump. Checked against
        // every field of every entry this host wrote (the fixture's RecordingAuditWriter, shared
        // across the whole test class) -- not just the ones this test itself triggered.
        Assert.DoesNotContain(_fixture.AuditWriter.Entries, entry =>
            entry.Actor.Contains(secret, StringComparison.Ordinal)
            || entry.Action.Contains(secret, StringComparison.Ordinal)
            || entry.ResourceType.Contains(secret, StringComparison.Ordinal)
            || entry.ResourceId.Contains(secret, StringComparison.Ordinal)
            || (entry.Detail?.Contains(secret, StringComparison.Ordinal) ?? false));
    }

    [Fact]
    public async Task T10_the_accept_request_carries_the_token_in_a_header_never_a_query_string()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Header Only Co", "admin@headeronly.example");
        var inviteBody = await InviteAndParseAsync(client, tenantId, "admin@headeronly.example", "headeronly@headeronly.example", "ReadOnly");
        var token = ExtractToken(inviteBody.GetProperty("acceptUrl").GetString()!);

        // A token supplied only as a query string (never as the header) must be refused exactly
        // like no token at all -- proving the route itself carries no {token} parameter.
        using var queryRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/invites/accept?token={Uri.EscapeDataString(token)}");
        queryRequest.Headers.Add("X-User-Id", "headeronly@headeronly.example");
        var queryResponse = await client.SendAsync(queryRequest);
        Assert.Equal(HttpStatusCode.NotFound, queryResponse.StatusCode);

        var headerResponse = await AcceptInvitationAsync(client, token, "headeronly@headeronly.example");
        Assert.Equal(HttpStatusCode.OK, headerResponse.StatusCode);
    }

    // ----- T11: accept binding -----

    [Fact]
    public async Task T11_email_mismatch_is_403_and_writes_no_membership_row()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Mismatch Co", "admin@mismatch.example");
        var inviteBody = await InviteAndParseAsync(client, tenantId, "admin@mismatch.example", "real@mismatch.example", "Procurement");
        var token = ExtractToken(inviteBody.GetProperty("acceptUrl").GetString()!);

        var response = await AcceptInvitationAsync(client, token, "impostor@mismatch.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        // 2026-09-13 (E14/F02/US01/T01, phase 1): the workspace creator becomes Admin at
        // creation, so a fresh tenant is never membership-empty -- the point this test proves
        // is that the impostor's rejected accept wrote no membership for THEM, not that the
        // tenant has zero memberships overall.
        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();
        var tenant = new TenantId(tenantId);
        using var _ = scope.ServiceProvider.GetRequiredService<Raffa.SharedKernel.Tenancy.ITenantContext>().BeginScope(tenant);
        Assert.False(await db.WorkspaceMemberships.AnyAsync(m =>
            m.TenantId == tenant
            && db.WorkspaceUsers.Any(u => u.Id == m.WorkspaceUserId && u.Email == "impostor@mismatch.example")));

        // ADR-025 §G: a mismatched accept is one of the nine named audit actions -- the security-
        // relevant event of someone attempting to accept an invitation that was not theirs must not
        // go unaudited. The invited address may appear in Detail (a different channel than the 403
        // body Rule D.3b guards), attributed to the impostor identity that actually made the call.
        Assert.Contains(_fixture.AuditWriter.Entries, entry =>
            entry.Action == "workspace.invitation.rejected"
            && entry.Actor == "impostor@mismatch.example"
            && entry.Detail == "email=real@mismatch.example");
    }

    [Fact]
    public async Task T11_expired_is_410()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Expired Co", "admin@expired.example");
        var inviteBody = await InviteAndParseAsync(client, tenantId, "admin@expired.example", "late@expired.example", "Legal");
        var token = ExtractToken(inviteBody.GetProperty("acceptUrl").GetString()!);

        using (var scope = _fixture.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();
            var tenant = new TenantId(tenantId);
            using var _ = scope.ServiceProvider.GetRequiredService<Raffa.SharedKernel.Tenancy.ITenantContext>().BeginScope(tenant);
            var invitation = await db.WorkspaceInvitations.SingleAsync(i => i.TenantId == tenant);
            invitation.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
            await db.SaveChangesAsync();
        }

        var preview = await GetInvitationAsync(client, token);
        Assert.Equal(HttpStatusCode.Gone, preview.StatusCode);

        var accept = await AcceptInvitationAsync(client, token, "late@expired.example");
        Assert.Equal(HttpStatusCode.Gone, accept.StatusCode);
    }

    [Theory]
    [InlineData("not-a-real-token")]
    [InlineData("00000000000000000000000000000000.made-up-secret")]
    public async Task T11_unknown_or_malformed_is_404(string garbageToken)
    {
        var client = _fixture.CreateClient();

        var preview = await GetInvitationAsync(client, garbageToken);
        Assert.Equal(HttpStatusCode.NotFound, preview.StatusCode);

        var accept = await AcceptInvitationAsync(client, garbageToken, "nobody@nowhere.example");
        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
    }

    [Fact]
    public async Task T11_a_revoked_invitation_is_404()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Revoked Co", "admin@revoked.example");
        var inviteBody = await InviteAndParseAsync(client, tenantId, "admin@revoked.example", "gone@revoked.example", "Finance");
        var invitationId = inviteBody.GetProperty("id").GetGuid();
        var token = ExtractToken(inviteBody.GetProperty("acceptUrl").GetString()!);

        var revokeResponse = await RevokeInvitationAsync(client, tenantId, invitationId, "admin@revoked.example");
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        var accept = await AcceptInvitationAsync(client, token, "gone@revoked.example");
        Assert.Equal(HttpStatusCode.NotFound, accept.StatusCode);
    }

    [Fact]
    public async Task T11_a_second_accept_by_the_same_identity_is_409()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Repeat Co", "admin@repeat.example");
        var inviteBody = await InviteAndParseAsync(client, tenantId, "admin@repeat.example", "repeat@repeat.example", "Procurement");
        var token = ExtractToken(inviteBody.GetProperty("acceptUrl").GetString()!);

        var first = await AcceptInvitationAsync(client, token, "repeat@repeat.example");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await AcceptInvitationAsync(client, token, "repeat@repeat.example");
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task T11_two_concurrent_accepts_leave_exactly_one_membership_and_never_500()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Race Co", "admin@race.example");
        var inviteBody = await InviteAndParseAsync(client, tenantId, "admin@race.example", "racer@race.example", "Legal");
        var token = ExtractToken(inviteBody.GetProperty("acceptUrl").GetString()!);

        var responses = await Task.WhenAll(
            AcceptInvitationAsync(client, token, "racer@race.example"),
            AcceptInvitationAsync(client, token, "racer@race.example"));

        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Conflict);

        using var scope = _fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();
        var tenant = new TenantId(tenantId);
        using var _ = scope.ServiceProvider.GetRequiredService<Raffa.SharedKernel.Tenancy.ITenantContext>().BeginScope(tenant);
        var memberships = await db.WorkspaceMemberships.Where(m => m.TenantId == tenant).ToListAsync();
        // The tenant's own creator (Admin) already holds a separate membership -- this asserts the
        // racer produced exactly one, not zero or two, among the total.
        var user = await db.WorkspaceUsers.SingleAsync(u => u.TenantId == tenant && u.Email == "racer@race.example");
        Assert.Single(memberships, m => m.WorkspaceUserId == user.Id);
    }

    [Fact]
    public async Task T11_an_invitation_issued_through_the_new_path_is_acceptable_end_to_end()
    {
        // Regression: LinkSignInAsync.ResolveSignedInUser fails when no WorkspaceUser row exists
        // for the email -- proving the invite endpoint still writes that row is exactly what makes
        // this pass rather than 400.
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "End To End Co", "admin@e2e.example");
        var inviteBody = await InviteAndParseAsync(client, tenantId, "admin@e2e.example", "newhire@e2e.example", "Admin");
        var token = ExtractToken(inviteBody.GetProperty("acceptUrl").GetString()!);

        var response = await AcceptInvitationAsync(client, token, "newhire@e2e.example");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(tenantId, body.RootElement.GetProperty("workspaceId").GetGuid());
        Assert.Equal("End To End Co", body.RootElement.GetProperty("workspaceName").GetString());
        Assert.Equal("Admin", body.RootElement.GetProperty("role").GetString());
    }

    // ----- T12: token-prefix hardening -----

    [Fact]
    public async Task T12_a_non_guid_prefix_is_404_with_no_scope_entered()
    {
        var client = _fixture.CreateClient();

        var response = await GetInvitationAsync(client, "not-a-guid-at-all.some-secret-value");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task T12_a_valid_foreign_tenant_prefix_with_a_wrong_secret_is_404_with_no_workspace_name_disclosed()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Foreign Tenant Secret Co", "admin@foreign.example");
        // A real tenant id (the route/prefix half is not a secret) paired with a guessed secret
        // that cannot possibly match any stored hash.
        var forgedToken = $"{tenantId:N}.{Guid.NewGuid():N}{Guid.NewGuid():N}";

        var response = await GetInvitationAsync(client, forgedToken);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Foreign Tenant Secret Co", body, StringComparison.Ordinal);
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

    private static async Task<HttpResponseMessage> InviteAsync(
        HttpClient client, Guid tenantId, string adminUserId, string email, string role)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{tenantId}/invites")
        {
            Content = JsonContent.Create(new { email, role }),
        };
        request.Headers.Add("X-User-Id", adminUserId);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> InviteAndParseAsync(
        HttpClient client, Guid tenantId, string adminUserId, string email, string role)
    {
        var response = await InviteAsync(client, tenantId, adminUserId, email, role);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private static async Task<HttpResponseMessage> GetInvitationAsync(HttpClient client, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/invites");
        request.Headers.Add("X-Invitation-Token", token);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> AcceptInvitationAsync(HttpClient client, string token, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/invites/accept");
        request.Headers.Add("X-Invitation-Token", token);
        request.Headers.Add("X-User-Id", userId);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> RevokeInvitationAsync(
        HttpClient client, Guid tenantId, Guid invitationId, string adminUserId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/workspaces/{tenantId}/invites/{invitationId}");
        request.Headers.Add("X-User-Id", adminUserId);
        return await client.SendAsync(request);
    }

    /// <summary>The accept link is <c>/invite/accept#&lt;token&gt;</c> -- a URL fragment is never
    /// transmitted to a server, so the SPA reads it client-side (ADR-025 Rule C9); this test-side
    /// helper does the equivalent parse over the plain string the 201 body carries.</summary>
    private static string ExtractToken(string acceptUrl) => acceptUrl["/invite/accept#".Length..];
}
