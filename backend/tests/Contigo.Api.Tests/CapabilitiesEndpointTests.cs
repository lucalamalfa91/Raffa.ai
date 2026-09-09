using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

namespace Contigo.Api.Tests;

/// <summary>
/// Host-level proof for task E13/F08/US01/T01 (story us-01-capability-catalog AC-1) that
/// `GET /api/capabilities` is real HTTP behaviour, not just C#. <see cref="CapabilitiesEndpointExtensions"/>
/// is deliberately unmapped in the real `Program.cs` yet (a later task, named "mapped by F06" in
/// this story's own Tasks row, adds `app.MapCapabilitiesEndpoints()`) — see that file's own doc
/// comment — so this test cannot use `WebApplicationFactory&lt;Program&gt;` the way every sibling
/// `*EndpointTests` class does (that factory boots the real `Program.cs`, which never calls this
/// extension method, and which also requires several `ConnectionStrings:*` values this endpoint
/// does not need at all). A bare minimal host built directly in this test — no connection string,
/// no other module — is enough: the handler under test touches nothing but the in-memory
/// `CapabilityCatalog` and an `X-Role` header.
/// </summary>
public sealed class CapabilitiesEndpointTests : IAsyncLifetime
{
    private WebApplication? _app;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        _app = builder.Build();
        _app.MapCapabilitiesEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
    }

    [Fact]
    public async Task Returns_the_versioned_catalog()
    {
        var payload = await _client!.GetFromJsonAsync<JsonElement>("/api/capabilities");

        Assert.Equal("capabilities-v2.0", payload.GetProperty("version").GetString());
        Assert.True(payload.GetProperty("capabilities").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Anonymous_caller_does_not_see_workspace_members()
    {
        var payload = await _client!.GetFromJsonAsync<JsonElement>("/api/capabilities");

        Assert.DoesNotContain(
            payload.GetProperty("capabilities").EnumerateArray(),
            c => c.GetProperty("key").GetString() == "workspace-members");
    }

    [Fact]
    public async Task Procurement_role_header_does_not_see_workspace_members()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/capabilities");
        request.Headers.Add("X-Role", "Procurement");

        var response = await _client!.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.DoesNotContain(
            payload.GetProperty("capabilities").EnumerateArray(),
            c => c.GetProperty("key").GetString() == "workspace-members");
    }

    [Fact]
    public async Task Admin_role_header_sees_workspace_members()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/capabilities");
        request.Headers.Add("X-Role", "Admin");

        var response = await _client!.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Contains(
            payload.GetProperty("capabilities").EnumerateArray(),
            c => c.GetProperty("key").GetString() == "workspace-members");
    }

    [Fact]
    public async Task Admin_only_entry_exposes_the_admin_role_gate_and_availability_wire_values()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/capabilities");
        request.Headers.Add("X-Role", "Workspace Admin");

        var response = await _client!.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var payload = await response.Content.ReadFromJsonAsync<JsonElement>();

        var workspaceMembers = payload.GetProperty("capabilities").EnumerateArray()
            .Single(c => c.GetProperty("key").GetString() == "workspace-members");

        Assert.Equal("admin", workspaceMembers.GetProperty("roleGate").GetString());
        Assert.Equal("admin", workspaceMembers.GetProperty("availability").GetString());
        Assert.Equal("/workspace/members", workspaceMembers.GetProperty("routePattern").GetString());
    }

    [Fact]
    public async Task Every_entry_carries_the_AC_1_field_shape()
    {
        var payload = await _client!.GetFromJsonAsync<JsonElement>("/api/capabilities");

        foreach (var entry in payload.GetProperty("capabilities").EnumerateArray())
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("key").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("title").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("routePattern").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("description").GetString()));
            Assert.True(entry.GetProperty("exampleQuestions").GetArrayLength() > 0);
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("roleGate").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(entry.GetProperty("availability").GetString()));
            Assert.True(entry.GetProperty("howTo").GetArrayLength() > 0);
        }
    }
}
