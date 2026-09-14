using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.Api.Tests.TestSupport;
using Raffa.Documents.Contracts.Domain;
using Raffa.Documents.Contracts.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
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
/// Dedicated real-Postgres host fixture for <see cref="WorkspaceDirectoryEndpointTests"/> (task
/// E14/F03/US01/T01, wave w14). Unlike this project's other endpoint tests (which either need no
/// database at all or swap onto the EF Core InMemory provider via
/// <c>TestSupport.InMemoryAskEngineFactory</c>), T1a's whole point is that dropping the identity
/// scope must make Postgres Row-Level Security itself deny every row — the InMemory provider has no
/// RLS concept at all, so it would make that assertion vacuous. Mirrors
/// <c>Raffa.IntegrationTests.R0IntegrationFixture</c>'s shape (a <see cref="WebApplicationFactory{Program}"/>
/// that <em>is</em> the fixture) and
/// <c>Raffa.Identity.Workspace.Tests.WorkspaceRlsCrossTenantIsolationTests</c>'s dedicated,
/// deliberately unprivileged Postgres role (the Testcontainers bootstrap role is always a
/// superuser, which unconditionally bypasses row security — asserting isolation over that
/// connection would pass vacuously).
///
/// Only <c>ConnectionStrings:IdentityWorkspace</c> and <c>ConnectionStrings:DocumentsContracts</c>
/// are overridden to point at this fixture's own Testcontainer — every other required connection
/// string (`Storage`/`Audit`/`Renewals`/`Savings`/`Chat`/`Suppliers`/`Quotes`) falls back to
/// <c>appsettings.Development.json</c>'s static, never-dialled default, the same convention
/// <c>SavingsKpiEndpointTests</c> already relies on. That premise holds for every route these
/// tests reach <em>except</em> the audit write behind invite/accept
/// (<c>A_non_admin_invited_member_sees_their_own_real_role</c>), which is why
/// <see cref="ConfigureWebHost"/> swaps <c>IAuditWriter</c> for the recording fake rather than
/// letting that one path dial the never-dialled default and 500.
/// </summary>
public sealed class WorkspaceDirectoryEndpointFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string AppRoleName = "raffa_workspace_directory_app";
    private const string AppRolePassword = "raffa_workspace_directory_app_test_password";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();

    private string _appConnectionString = string.Empty;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var superuserConnectionString = _postgres.GetConnectionString();

        var identityOptions = new DbContextOptionsBuilder<IdentityWorkspaceDbContext>();
        IdentityWorkspaceDbContextOptions.Configure(identityOptions, superuserConnectionString);
        await using (var db = new IdentityWorkspaceDbContext(identityOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity + this wave's three migrations
            // (AddWorkspaceUserIdentitySelfReadPolicy, AddWorkspaceProfileColumns,
            // AddWorkspaceInvitation).
            await db.Database.MigrateAsync();
        }

        var documentsOptions = new DbContextOptionsBuilder<DocumentsContractsDbContext>();
        DocumentsContractsDbContextOptions.Configure(documentsOptions, superuserConnectionString);
        await using (var db = new DocumentsContractsDbContext(documentsOptions.Options))
        {
            // Applies Initial + AddTenantRowLevelSecurity for Documents/Contracts' own tables — the
            // validated-contract count join needs Contract/Document to exist on this same instance.
            await db.Database.MigrateAsync();

            // One unprivileged app role, granted after both modules' tables exist, covers every
            // table regardless of which module's migration created it (same shape
            // R0IntegrationFixture already uses for its own multi-module grant).
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

    // Explicit implementation: WebApplicationFactory<T> already exposes a public
    // ValueTask DisposeAsync() (System.IAsyncDisposable); xunit's own IAsyncLifetime.DisposeAsync
    // returns Task, so this must be explicit to disambiguate — same shape R0IntegrationFixture uses.
    Task IAsyncLifetime.DisposeAsync() => _postgres.DisposeAsync().AsTask();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:IdentityWorkspace", _appConnectionString);
        builder.UseSetting("ConnectionStrings:DocumentsContracts", _appConnectionString);
        // A_non_admin_invited_member_sees_their_own_real_role invites and accepts, and both of those
        // write real audit rows (`workspace.invitation.issued`, `workspace.membership.granted`) through
        // IAuditWriter -> AuditDbContext. This fixture never puts the Audit schema on its container, so
        // without this swap the host dials `ConnectionStrings:Audit`'s never-dialled default
        // (127.0.0.1:5432 -- "Connection refused" on a CI runner) and the invite 500s: the failure that
        // kept `main` red after wave w14. Same RecordingAuditWriter swap MembershipRemovalEndpointFixture
        // and InvitationLifecycleEndpointFixture already use for the same reason.
        // Fix 2026-09-14: same X-User-Id -> `oid` bridge InvitationLifecycleEndpointFixture
        // registers, and for the same reason -- a dedicated-host fixture never sees the one
        // TestSupport.InMemoryAskEngineFactory installs, so after NW-05 every request here
        // authenticated nobody and answered 401.
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IAuditWriter>(new RecordingAuditWriter());
            services.AddAuthentication(TestUserIdAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TestUserIdAuthenticationHandler>(
                    TestUserIdAuthenticationHandler.SchemeName, _ => { });
        });
    }
}

/// <summary>
/// Proves the Definition of Done for task E14/F03/US01/T01 (wave w14 "workspace is real", NW-01,
/// NW-09, NW-24; ADR-026 §D1/§D2/§D5, ADR-025 §F.1/§F.3, ADR-003 w14 footer clause 1):
/// <list type="bullet">
/// <item><b>T1a</b> — identity <c>A</c>, member of one tenant only, gets exactly that tenant back;
/// the other tenant's id, name and count appear nowhere. Because this runs over the dedicated
/// unprivileged app role (<see cref="WorkspaceDirectoryEndpointFixture"/>'s own doc comment), this
/// is also the wave's end-to-end proof that the GUC and the `identity_self` policy agree: if
/// `WorkspaceEndpointExtensions.ListWorkspacesAsync` ever stopped opening the identity scope, RLS
/// would deny the caller's own <c>workspace_user</c> row too, discovery would find zero candidates,
/// and this exact test would start failing (`[]` instead of `[T1]`) — the assertion is contingent on
/// the real mechanism, not merely on application-level filtering
/// (<c>Raffa.Tenancy.Tests.WorkspaceUserIdentitySelfPolicyTests</c> proves the GUC/policy pair in
/// isolation; this class proves the real HTTP handler is actually wired into it).</item>
/// <item><b>T2b</b> — a crafted `X-Tenant-Id` for a foreign tenant changes nothing (N5).</item>
/// <item>200 + `[]` for a caller with no membership; 401 with no identity.</item>
/// <item>The workspace profile round-trip (NW-24): `industry`/`country` stored, `currency` derived,
/// an unsupported country is 400 through the existing <c>Result&lt;T&gt;</c> failure path, absent
/// values round-trip absent.</item>
/// <item>`contractCount` reflects real Documents/Contracts data, joined in the host (ADR-026 §D2).</item>
/// </list>
/// </summary>
public sealed class WorkspaceDirectoryEndpointTests : IClassFixture<WorkspaceDirectoryEndpointFixture>
{
    private readonly WorkspaceDirectoryEndpointFixture _fixture;

    public WorkspaceDirectoryEndpointTests(WorkspaceDirectoryEndpointFixture fixture)
    {
        _fixture = fixture;
    }

    private static async Task<Guid> CreateWorkspaceAsync(
        HttpClient client, string name, string userId, string? industry = null, string? country = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces")
        {
            Content = JsonContent.Create(new { name, industry, country }),
        };
        request.Headers.Add("X-User-Id", userId);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetGuid();
    }

    /// <summary>Asserts a status, quoting the server's own error text when it differs -- an
    /// unhandled exception is far cheaper to diagnose from the assertion message than from a bare
    /// "Expected Created, actual InternalServerError" in a CI log (same helper as
    /// R1DocumentsV2EndToEndTests.AssertStatusAsync).</summary>
    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(
            response.StatusCode == expected,
            $"HTTP {(int)response.StatusCode}: {body[..Math.Min(12000, body.Length)]}");
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

    /// <summary>Fix 2026-09-14: `POST /api/workspaces/{tenantId}/invites/accept` -- no
    /// `X-Invitation-Token` header at all, the one thing that distinguishes this from the inline
    /// token-based accept request <see cref="A_non_admin_invited_member_sees_their_own_real_role"/>
    /// builds by hand above.</summary>
    private static async Task<HttpResponseMessage> AcceptForIdentityAsync(HttpClient client, Guid tenantId, string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{tenantId}/invites/accept");
        request.Headers.Add("X-User-Id", userId);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> GetWorkspacesAsync(
        HttpClient client, string? userId, Guid? craftedTenantId = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/workspaces");
        if (userId is not null)
        {
            request.Headers.Add("X-User-Id", userId);
        }

        if (craftedTenantId is not null)
        {
            request.Headers.Add("X-Tenant-Id", craftedTenantId.Value.ToString());
        }

        return await client.SendAsync(request);
    }

    /// <summary>Writes one validated contract (a <see cref="Contract"/> with one linked
    /// <see cref="Document"/> at <see cref="DocumentProcessingStatus.Completed"/>) directly through
    /// the host's own DI-resolved <see cref="DocumentsContractsDbContext"/> — there is no HTTP path
    /// this test needs that runs the real extraction pipeline, the same reasoning
    /// <c>PortfolioQueryServiceTests</c> already gives for seeding this way.</summary>
    private async Task SeedValidatedContractAsync(Guid tenantIdValue)
    {
        var tenantId = new TenantId(tenantIdValue);
        using var scope = _fixture.Services.CreateScope();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        using var _ = tenantContext.BeginScope(tenantId);
        var db = scope.ServiceProvider.GetRequiredService<DocumentsContractsDbContext>();

        var contract = new Contract
        {
            TenantId = tenantId,
            Type = ContractDocumentType.Msa,
            Status = "processing",
            Currency = "USD",
            AutoRenewal = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Contracts.Add(contract);
        db.Documents.Add(new Document
        {
            TenantId = tenantId,
            ContractId = contract.Id,
            FileName = "seed.pdf",
            MimeType = "application/pdf",
            StoragePath = $"{tenantIdValue}/seed.pdf",
            Checksum = "seed-checksum",
            ProcessingStatus = DocumentProcessingStatus.Completed,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task T1a_identity_gets_exactly_its_own_tenant_and_nothing_from_another()
    {
        var client = _fixture.CreateClient();
        var t1 = await CreateWorkspaceAsync(client, "Tenant One", "a@acme.example");
        var t2 = await CreateWorkspaceAsync(client, "Tenant Two — Secret", "c@other.example");

        var response = await GetWorkspacesAsync(client, "a@acme.example");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(raw);
        var workspaces = body.RootElement.GetProperty("workspaces");

        Assert.Equal(1, workspaces.GetArrayLength());
        var only = workspaces[0];
        Assert.Equal(t1, only.GetProperty("id").GetGuid());
        Assert.Equal("Tenant One", only.GetProperty("name").GetString());
        Assert.Equal("Admin", only.GetProperty("role").GetString());

        // F.1e/F.1g: T2's id, name and count appear nowhere at all in the raw payload.
        Assert.DoesNotContain(t2.ToString(), raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Secret", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task T2b_a_crafted_tenant_header_changes_nothing()
    {
        var client = _fixture.CreateClient();
        var t1 = await CreateWorkspaceAsync(client, "Only Mine", "b@acme.example");
        var t2 = await CreateWorkspaceAsync(client, "Not Mine", "d@other.example");

        // N5: a crafted X-Tenant-Id for a tenant this identity does not belong to must not widen,
        // narrow or otherwise change the response at all — the endpoint takes no tenant input.
        var withoutHeader = await GetWorkspacesAsync(client, "b@acme.example");
        var withCraftedHeader = await GetWorkspacesAsync(client, "b@acme.example", craftedTenantId: t2);

        var bodyWithout = await withoutHeader.Content.ReadAsStringAsync();
        var bodyWith = await withCraftedHeader.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, withCraftedHeader.StatusCode);
        using var parsedWith = JsonDocument.Parse(bodyWith);
        var workspaces = parsedWith.RootElement.GetProperty("workspaces");
        Assert.Equal(1, workspaces.GetArrayLength());
        Assert.Equal(t1, workspaces[0].GetProperty("id").GetGuid());
        Assert.DoesNotContain(t2.ToString(), bodyWith, StringComparison.OrdinalIgnoreCase);

        // Same JSON either way — the crafted header is inert, not merely "still correct".
        Assert.Equal(bodyWithout, bodyWith);
    }

    [Fact]
    public async Task A_caller_with_no_membership_gets_200_and_an_empty_array()
    {
        var client = _fixture.CreateClient();

        var response = await GetWorkspacesAsync(client, "nobody-knows-me@acme.example");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("workspaces").GetArrayLength());
    }

    [Fact]
    public async Task Missing_identity_returns_401()
    {
        var client = _fixture.CreateClient();

        var response = await GetWorkspacesAsync(client, userId: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Blank_identity_header_returns_401()
    {
        var client = _fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/workspaces");
        request.Headers.Add("X-User-Id", "   ");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_non_admin_invited_member_sees_their_own_real_role()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Shared Workspace", "admin@acme.example");

        var inviteResponse = await InviteAsync(client, tenantId, "admin@acme.example", "member@acme.example", "Procurement");
        await AssertStatusAsync(HttpStatusCode.Created, inviteResponse);

        // 2026-09-13 (E15/F01/US01/T01, phase 3): invite alone writes no membership -- accept
        // does. GET /api/workspaces lists live memberships only, so this test's own title
        // ("sees their own real role") requires the accept step it was missing.
        using var inviteBody = JsonDocument.Parse(await inviteResponse.Content.ReadAsStringAsync());
        var token = inviteBody.RootElement.GetProperty("acceptUrl").GetString()!["/invite/accept#".Length..];
        using var acceptRequest = new HttpRequestMessage(HttpMethod.Post, "/api/invites/accept");
        acceptRequest.Headers.Add("X-Invitation-Token", token);
        acceptRequest.Headers.Add("X-User-Id", "member@acme.example");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(acceptRequest)).StatusCode);

        var response = await GetWorkspacesAsync(client, "member@acme.example");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var workspaces = body.RootElement.GetProperty("workspaces");
        var only = Assert.Single(EnumerateArray(workspaces));
        Assert.Equal(tenantId, only.GetProperty("id").GetGuid());
        Assert.Equal("Procurement", only.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Pending_invitation_is_surfaced_before_accept_and_gone_after_no_token_needed()
    {
        // Fix 2026-09-14: the end-to-end contract the SPA's own auto-join relies on --
        // GET /api/workspaces tells a signed-in, not-yet-a-member caller about a live invitation, and
        // POST /api/workspaces/{tenantId}/invites/accept completes it with no token at all (contrast
        // A_non_admin_invited_member_sees_their_own_real_role's own hand-built X-Invitation-Token
        // request above).
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Discoverable Co", "admin@discoverable.example");
        await AssertStatusAsync(
            HttpStatusCode.Created,
            await InviteAsync(client, tenantId, "admin@discoverable.example", "findable@discoverable.example", "Finance"));

        var before = await GetWorkspacesAsync(client, "findable@discoverable.example");
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        using var beforeBody = JsonDocument.Parse(await before.Content.ReadAsStringAsync());
        Assert.Equal(0, beforeBody.RootElement.GetProperty("workspaces").GetArrayLength());
        var pendingBefore = Assert.Single(EnumerateArray(beforeBody.RootElement.GetProperty("pendingInvitations")));
        Assert.Equal(tenantId, pendingBefore.GetProperty("tenantId").GetGuid());
        Assert.Equal("Discoverable Co", pendingBefore.GetProperty("workspaceName").GetString());
        Assert.Equal("Finance", pendingBefore.GetProperty("role").GetString());

        var acceptResponse = await AcceptForIdentityAsync(client, tenantId, "findable@discoverable.example");
        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);
        using var acceptBody = JsonDocument.Parse(await acceptResponse.Content.ReadAsStringAsync());
        Assert.Equal(tenantId, acceptBody.RootElement.GetProperty("workspaceId").GetGuid());
        Assert.Equal("Finance", acceptBody.RootElement.GetProperty("role").GetString());

        var after = await GetWorkspacesAsync(client, "findable@discoverable.example");
        using var afterBody = JsonDocument.Parse(await after.Content.ReadAsStringAsync());
        Assert.Equal(0, afterBody.RootElement.GetProperty("pendingInvitations").GetArrayLength());
        var workspaceAfter = Assert.Single(EnumerateArray(afterBody.RootElement.GetProperty("workspaces")));
        Assert.Equal(tenantId, workspaceAfter.GetProperty("id").GetGuid());
        Assert.Equal("Finance", workspaceAfter.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Create_with_industry_and_country_derives_and_stores_currency()
    {
        var client = _fixture.CreateClient();
        // Lower-case input on purpose: proves normalisation, not just the happy-path spelling.
        var tenantId = await CreateWorkspaceAsync(
            client, "Profiled GmbH", "profiled@acme.example", industry: "Manufacturing", country: "de");

        var response = await GetWorkspacesAsync(client, "profiled@acme.example");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var only = Assert.Single(EnumerateArray(body.RootElement.GetProperty("workspaces")));

        Assert.Equal(tenantId, only.GetProperty("id").GetGuid());
        Assert.Equal("DE", only.GetProperty("country").GetString());
        // NW-24 / ADR-003 w14 footer clause 1: currency is derived, never typed — DE -> EUR.
        Assert.Equal("EUR", only.GetProperty("currency").GetString());
    }

    [Fact]
    public async Task An_unsupported_country_is_400_not_a_raw_postgres_error()
    {
        var client = _fixture.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces")
        {
            Content = JsonContent.Create(new { name = "Bad Country Co", country = "US" }),
        };
        request.Headers.Add("X-User-Id", "badcountry@acme.example");

        var response = await client.SendAsync(request);

        // The existing Result<T> failure path (WorkspaceProvisioningService), not a 500 from an
        // unhandled Postgres check-constraint/length exception.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errorBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("US", errorBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Absent_profile_fields_round_trip_as_absent_never_an_invented_default()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "No Profile Yet", "noprofile@acme.example");

        var response = await GetWorkspacesAsync(client, "noprofile@acme.example");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var only = Assert.Single(EnumerateArray(body.RootElement.GetProperty("workspaces")));

        Assert.Equal(tenantId, only.GetProperty("id").GetGuid());
        Assert.Equal(JsonValueKind.Null, only.GetProperty("country").ValueKind);
        // Never a fabricated "CHF" (or any other currency) when no country was ever supplied.
        Assert.Equal(JsonValueKind.Null, only.GetProperty("currency").ValueKind);
    }

    [Fact]
    public async Task ContractCount_reflects_real_validated_contract_data()
    {
        var client = _fixture.CreateClient();
        var tenantId = await CreateWorkspaceAsync(client, "Has Contracts", "hascontracts@acme.example");

        await SeedValidatedContractAsync(tenantId);

        var response = await GetWorkspacesAsync(client, "hascontracts@acme.example");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var only = Assert.Single(EnumerateArray(body.RootElement.GetProperty("workspaces")));

        Assert.Equal(1, only.GetProperty("contractCount").GetInt32());
    }

    private static IEnumerable<JsonElement> EnumerateArray(JsonElement array)
    {
        foreach (var element in array.EnumerateArray())
        {
            yield return element;
        }
    }
}
