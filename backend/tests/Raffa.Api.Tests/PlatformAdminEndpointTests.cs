using System.Net;
using System.Runtime.CompilerServices;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;

namespace Raffa.Api.Tests;

/// <summary>
/// The portal admin (Raffa staff): prospect invitations, the workspace registry and suspension, on the
/// real Postgres host of <see cref="InvitationLifecycleEndpointFixture"/> with the allowlist on and open
/// workspace creation switched off. Staff are never members of a customer workspace.
/// </summary>
public sealed class PlatformAdminEndpointTests : IClassFixture<InvitationLifecycleEndpointFixture>
{
    private const string Staff = "staff@raffa.example";

    // One derived host per fixture, not per test: xunit builds a new test-class instance for every test,
    // and a fresh host each time exhausted the shared Postgres test container's connections.
    private static readonly ConditionalWeakTable<InvitationLifecycleEndpointFixture, HttpClient> Clients = new();

    private readonly HttpClient _client;

    public PlatformAdminEndpointTests(InvitationLifecycleEndpointFixture fixture)
    {
        _client = Clients.GetValue(
            fixture,
            f => f
                .WithWebHostBuilder(builder =>
                {
                    builder.UseSetting("PlatformAdmin:ObjectIds:0", Staff);
                    builder.UseSetting("PlatformAdmin:RequireInvitationToCreateWorkspace", "true");
                    builder.UseSetting("RenewalReviews:PermitLimit", "1000");
                })
                .CreateClient());
    }

    [Fact]
    public async Task Only_a_platform_admin_reaches_the_admin_surface()
    {
        await AssertStatusAsync(HttpStatusCode.Unauthorized, await _client.GetAsync("/api/admin/me"));
        Assert.True(await IsPlatformAdminAsync(Staff));
        Assert.False(await IsPlatformAdminAsync("someone@customer.example"));

        foreach (var path in new[] { "/api/admin/overview", "/api/admin/workspaces", "/api/admin/invitations", "/api/admin/users", "/api/admin/market-network", "/api/admin/renewal-reviews" })
        {
            await AssertStatusAsync(HttpStatusCode.NotFound, await SendAsync(HttpMethod.Get, path, "someone@customer.example"));
            await AssertStatusAsync(HttpStatusCode.OK, await SendAsync(HttpMethod.Get, path, Staff));
        }

        Assert.Equal(
            HttpStatusCode.NotFound,
            (await SendAsync(HttpMethod.Post, "/api/admin/invitations", "someone@customer.example", new { email = "x@y.example" })).StatusCode);
    }

    [Fact]
    public async Task The_market_network_monitor_says_so_when_the_host_has_no_market_store()
    {
        using var json = await GetJsonAsync("/api/admin/market-network", Staff);

        Assert.False(json.RootElement.GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task A_prospect_cannot_create_a_workspace_until_they_accept_an_invitation_and_it_is_spent_once()
    {
        const string prospect = "ceo@prospect-one.example";
        await AssertStatusAsync(HttpStatusCode.Forbidden, await CreateWorkspaceAsync(prospect, "Prospect One"));

        var token = await InviteAsync(prospect);
        await AssertStatusAsync(HttpStatusCode.OK, await AcceptAsync(token, prospect));
        // Accepting again as the same person is a harmless no-op.
        await AssertStatusAsync(HttpStatusCode.OK, await AcceptAsync(token, prospect));

        var created = await CreateWorkspaceAsync(prospect, "Prospect One");
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        // One invitation makes one workspace.
        await AssertStatusAsync(HttpStatusCode.Forbidden, await CreateWorkspaceAsync(prospect, "Prospect One Bis"));

        // The registry now lists it (metadata only) and the overview counts it.
        using var workspaces = await GetJsonAsync("/api/admin/workspaces", Staff);
        Assert.Contains(
            workspaces.RootElement.GetProperty("workspaces").EnumerateArray(),
            w => w.GetProperty("name").GetString() == "Prospect One" && w.GetProperty("adminEmail").GetString() == prospect);
        using var overview = await GetJsonAsync("/api/admin/overview", Staff);
        Assert.True(overview.RootElement.GetProperty("totalWorkspaces").GetInt32() >= 1);
    }

    [Fact]
    public async Task An_invitation_is_bound_to_its_address_and_single_use()
    {
        var token = await InviteAsync("owner@prospect-two.example");

        await AssertStatusAsync(HttpStatusCode.Forbidden, await AcceptAsync(token, "intruder@elsewhere.example"));
        await AssertStatusAsync(HttpStatusCode.NotFound, await AcceptAsync("not-a-real-token", "owner@prospect-two.example"));

        await AssertStatusAsync(HttpStatusCode.OK, await AcceptAsync(token, "owner@prospect-two.example"));
        await AssertStatusAsync(HttpStatusCode.Conflict, await AcceptAsync(token, "intruder@elsewhere.example"));
    }

    [Fact]
    public async Task A_revoked_or_regenerated_invitation_cannot_be_used()
    {
        var revoked = await InviteWithIdAsync("a@prospect-three.example");
        await AssertStatusAsync(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Delete, $"/api/admin/invitations/{revoked.Id}", Staff));
        await AssertStatusAsync(HttpStatusCode.Gone, await AcceptAsync(revoked.Token, "a@prospect-three.example"));

        var first = await InviteWithIdAsync("b@prospect-three.example");
        var regenerated = await SendAsync(HttpMethod.Post, $"/api/admin/invitations/{first.Id}/regenerate", Staff);
        await AssertStatusAsync(HttpStatusCode.Created, regenerated);
        await AssertStatusAsync(HttpStatusCode.Gone, await AcceptAsync(first.Token, "b@prospect-three.example"));
    }

    [Fact]
    public async Task A_workspace_can_be_suspended_and_reactivated_from_the_registry()
    {
        const string owner = "owner@prospect-four.example";
        await AssertStatusAsync(HttpStatusCode.OK, await AcceptAsync(await InviteAsync(owner), owner));
        await AssertStatusAsync(HttpStatusCode.Created, await CreateWorkspaceAsync(owner, "Prospect Four"));

        var id = await RegistryIdAsync("Prospect Four");
        Assert.Equal("active", await RegistryStatusAsync(id));

        await AssertStatusAsync(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Post, $"/api/admin/workspaces/{id}/suspend", Staff));
        Assert.Equal("suspended", await RegistryStatusAsync(id));
        await AssertStatusAsync(HttpStatusCode.NotFound, await SendAsync(HttpMethod.Post, $"/api/admin/workspaces/{Guid.NewGuid()}/suspend", Staff));

        // Nobody but a platform admin may suspend or reactivate.
        await AssertStatusAsync(HttpStatusCode.NotFound, await SendAsync(HttpMethod.Post, $"/api/admin/workspaces/{id}/reactivate", owner));
        Assert.Equal("suspended", await RegistryStatusAsync(id));

        await AssertStatusAsync(HttpStatusCode.NoContent, await SendAsync(HttpMethod.Post, $"/api/admin/workspaces/{id}/reactivate", Staff));
        Assert.Equal("active", await RegistryStatusAsync(id));
    }

    [Fact]
    public async Task A_landing_page_lead_is_stored_on_postgres_and_listed_newest_first()
    {
        var older = $"Lead Older {Guid.NewGuid():N}";
        var newer = $"Lead Newer {Guid.NewGuid():N}";
        foreach (var company in new[] { older, newer })
        {
            // Anonymous: no X-User-Id, no X-Tenant-Id.
            var response = await _client.PostAsJsonAsync("/api/public/renewal-reviews", new
            {
                name = " Marco Bianchi ", email = "marco@lead.example", company, renewal = "3\u20136 months", consent = true, website = "",
            });
            await AssertStatusAsync(HttpStatusCode.Created, response);
            await Task.Delay(20);
        }

        // The honeypot answers the same and writes nothing.
        var bot = $"Lead Bot {Guid.NewGuid():N}";
        await AssertStatusAsync(HttpStatusCode.Created, await _client.PostAsJsonAsync("/api/public/renewal-reviews", new
        {
            name = "Bot", email = "bot@spam.example", company = bot, renewal = "Not sure yet", consent = true, website = "x",
        }));

        await AssertStatusAsync(HttpStatusCode.NotFound, await SendAsync(HttpMethod.Get, "/api/admin/renewal-reviews", "someone@customer.example"));
        using var json = await GetJsonAsync("/api/admin/renewal-reviews", Staff);
        var items = json.RootElement.GetProperty("items").EnumerateArray().ToList();
        var companies = items.Select(i => i.GetProperty("company").GetString()).ToList();
        Assert.DoesNotContain(bot, companies);
        Assert.True(companies.IndexOf(newer) >= 0 && companies.IndexOf(newer) < companies.IndexOf(older));

        var lead = items.Single(i => i.GetProperty("company").GetString() == newer);
        Assert.Equal("Marco Bianchi", lead.GetProperty("name").GetString());
        Assert.Equal("marco@lead.example", lead.GetProperty("email").GetString());
        Assert.Equal("3\u20136 months", lead.GetProperty("renewal").GetString());
        Assert.NotEqual(Guid.Empty, lead.GetProperty("id").GetGuid());
        Assert.Equal(TimeSpan.Zero, lead.GetProperty("createdAt").GetDateTimeOffset().Offset);
    }

    // ----- helpers -----

    private static async Task AssertStatusAsync(HttpStatusCode expected, HttpResponseMessage response) =>
        Assert.True(
            response.StatusCode == expected,
            $"Expected {expected} but got {(int)response.StatusCode} {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

    private async Task<bool> IsPlatformAdminAsync(string userId)
    {
        using var body = await GetJsonAsync("/api/admin/me", userId);
        return body.RootElement.GetProperty("isPlatformAdmin").GetBoolean();
    }

    private async Task<string> InviteAsync(string email) => (await InviteWithIdAsync(email)).Token;

    private async Task<(Guid Id, string Token)> InviteWithIdAsync(string email)
    {
        var response = await SendAsync(HttpMethod.Post, "/api/admin/invitations", Staff, new { email });
        await AssertStatusAsync(HttpStatusCode.Created, response);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var url = body.RootElement.GetProperty("acceptUrl").GetString()!;
        Assert.Contains("/prospect/accept#", url, StringComparison.Ordinal);
        // No mail transport in this host: the response says so and carries the link to share.
        Assert.Equal("no_transport", body.RootElement.GetProperty("deliveryOutcome").GetString());
        return (body.RootElement.GetProperty("id").GetGuid(), url[(url.IndexOf('#') + 1)..]);
    }

    private Task<HttpResponseMessage> AcceptAsync(string token, string userId)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/platform/invitations/accept");
        request.Headers.Add("X-User-Id", userId);
        request.Headers.Add("X-Platform-Invitation-Token", token);
        return _client.SendAsync(request);
    }

    private Task<HttpResponseMessage> CreateWorkspaceAsync(string userId, string name) =>
        SendAsync(HttpMethod.Post, "/api/workspaces", userId, new { name });

    private async Task<Guid> RegistryIdAsync(string name)
    {
        using var body = await GetJsonAsync("/api/admin/workspaces", Staff);
        return body.RootElement.GetProperty("workspaces").EnumerateArray()
            .Single(w => w.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();
    }

    private async Task<string?> RegistryStatusAsync(Guid id)
    {
        using var body = await GetJsonAsync("/api/admin/workspaces", Staff);
        return body.RootElement.GetProperty("workspaces").EnumerateArray()
            .Single(w => w.GetProperty("id").GetGuid() == id).GetProperty("status").GetString();
    }

    private async Task<JsonDocument> GetJsonAsync(string path, string userId)
    {
        var response = await SendAsync(HttpMethod.Get, path, userId);
        await AssertStatusAsync(HttpStatusCode.OK, response);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string userId, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        request.Headers.Add("X-User-Id", userId);
        return _client.SendAsync(request);
    }
}
