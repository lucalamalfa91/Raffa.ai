using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Raffa.Chat.Application.Capabilities;

namespace Raffa.Api.Tests;

/// <summary>
/// Host-level proof for <see cref="CapabilitiesEndpointExtensions"/> (wave w16 NW-31,
/// task E18/F03/US01/T01; S16-6a / S-T27). <c>GET /api/capabilities</c> returns the whole
/// static catalog — including Admin-gated rows — and a spoofed role header confers nothing:
/// the JSON is identical with no header, with a client-asserted Admin header, and with a
/// client-asserted Procurement header. A bare minimal host is enough: the handler touches
/// nothing but the in-memory <see cref="CapabilityCatalog"/>.
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
    public async Task Returns_the_whole_catalog_including_admin_gated_rows()
    {
        var payload = await _client!.GetFromJsonAsync<JsonElement>("/api/capabilities");

        Assert.Equal(CapabilityCatalog.Version, payload.GetProperty("version").GetString());
        Assert.Equal(CapabilityCatalog.All.Count, payload.GetProperty("capabilities").GetArrayLength());
        Assert.Contains(
            payload.GetProperty("capabilities").EnumerateArray(),
            c => c.GetProperty("key").GetString() == "workspace-members");
    }

    [Fact]
    public async Task Spoofed_role_headers_do_not_change_the_response()
    {
        var anonymous = await ReadBodyAsync(headers: null);
        var spoofedAdmin = await ReadBodyAsync(("X-Role", "Admin"), ("X-Workspace-Role", "Admin"));
        var spoofedProcurement = await ReadBodyAsync(("X-Role", "Procurement"));

        Assert.Equal(anonymous, spoofedAdmin);
        Assert.Equal(anonymous, spoofedProcurement);
        Assert.Contains("\"workspace-members\"", anonymous, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Admin_entry_exposes_the_role_gate_as_a_label()
    {
        var payload = await _client!.GetFromJsonAsync<JsonElement>("/api/capabilities");

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

    private async Task<string> ReadBodyAsync(params (string Name, string Value)[]? headers)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/capabilities");
        if (headers is not null)
        {
            foreach (var (name, value) in headers)
            {
                request.Headers.Add(name, value);
            }
        }

        var response = await _client!.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }
}
