using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Raffa.Api.Tests;

/// <summary>
/// The portal admin surface (<c>/api/admin/*</c>): only identities on the <c>PlatformAdmin:ObjectIds</c>
/// allowlist may onboard a customer; everyone else gets a 404 that reveals nothing. Reuses the real
/// Postgres host of <see cref="InvitationLifecycleEndpointFixture"/> with the allowlist switched on.
/// </summary>
public sealed class PlatformAdminEndpointTests : IClassFixture<InvitationLifecycleEndpointFixture>
{
    private const string Staff = "staff@raffa.example";

    private readonly HttpClient _client;

    public PlatformAdminEndpointTests(InvitationLifecycleEndpointFixture fixture)
    {
        _client = fixture
            .WithWebHostBuilder(builder => builder.UseSetting("PlatformAdmin:ObjectIds:0", Staff))
            .CreateClient();
    }

    [Fact]
    public async Task Status_is_401_when_anonymous_and_reports_the_allowlist_for_signed_in_callers()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _client.GetAsync("/api/admin/me")).StatusCode);

        Assert.True(await IsPlatformAdminAsync(Staff));
        Assert.False(await IsPlatformAdminAsync("someone@customer.example"));
    }

    [Fact]
    public async Task A_non_platform_admin_gets_404_and_no_workspace_is_created()
    {
        var response = await CreateCustomerWorkspaceAsync("someone@customer.example", "Sneaky Co", "boss@sneaky.example");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var list = await ListWorkspacesAsync("someone@customer.example");
        Assert.Empty(list.RootElement.GetProperty("workspaces").EnumerateArray());
    }

    [Fact]
    public async Task A_platform_admin_creates_the_customer_workspace_and_invites_its_first_admin()
    {
        var response = await CreateCustomerWorkspaceAsync(Staff, "Acme Demo", "cfo@acme.example");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var workspaceId = body.RootElement.GetProperty("workspace").GetProperty("id").GetGuid();
        var invitation = body.RootElement.GetProperty("invitation");
        Assert.Equal("cfo@acme.example", invitation.GetProperty("email").GetString());
        Assert.Equal("Admin", invitation.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("invitationError").ValueKind);

        // The staff member is Admin of the new workspace, so it is in their list and can be entered.
        using var list = await ListWorkspacesAsync(Staff);
        var entry = list.RootElement.GetProperty("workspaces").EnumerateArray()
            .Single(w => w.GetProperty("id").GetGuid() == workspaceId);
        Assert.Equal("Admin", entry.GetProperty("role").GetString());
        Assert.Equal("Acme Demo", entry.GetProperty("name").GetString());
    }

    [Fact]
    public async Task A_missing_customer_email_or_workspace_name_is_a_400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateCustomerWorkspaceAsync(Staff, "No Owner Co", "  ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateCustomerWorkspaceAsync(Staff, " ", "cfo@nameless.example")).StatusCode);
    }

    private async Task<bool> IsPlatformAdminAsync(string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/admin/me");
        request.Headers.Add("X-User-Id", userId);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("isPlatformAdmin").GetBoolean();
    }

    private async Task<HttpResponseMessage> CreateCustomerWorkspaceAsync(string userId, string name, string customerEmail)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/admin/workspaces")
        {
            Content = JsonContent.Create(new { name, customerEmail }),
        };
        request.Headers.Add("X-User-Id", userId);
        return await _client.SendAsync(request);
    }

    private async Task<JsonDocument> ListWorkspacesAsync(string userId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/workspaces");
        request.Headers.Add("X-User-Id", userId);
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
