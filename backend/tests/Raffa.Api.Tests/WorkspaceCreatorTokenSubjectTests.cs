using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Raffa.Api.Tests;

/// <summary>
/// Fix 2026-09-14 (NW-05, ADR-010 w15 footer §2.1). The first real sign-in on deployed `dev` could
/// not create a workspace: `POST /api/workspaces` answered
/// <c>400 "'ab5b6f66-…' is not a valid email address to invite."</c>, because since NW-05 the
/// caller identity is the token's <c>oid</c> and the handler still passed it where the creator
/// row's Email column wanted an address. Every pre-existing test in this project sends an address
/// in <c>X-User-Id</c>, which is why none of them caught it — the subject was an email by
/// accident. These two model the real token shape: a GUID subject and a separate <c>email</c>
/// claim (<see cref="TestUserIdAuthenticationHandler.UserEmailHeaderName"/>).
/// </summary>
public sealed class WorkspaceCreatorTokenSubjectTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Subject = "ab5b6f66-1bd6-44e3-8ea0-2ceff69b62a6";
    private const string Email = "founder@acme.example";

    private readonly WebApplicationFactory<Program> _baseFactory;

    public WorkspaceCreatorTokenSubjectTests(WebApplicationFactory<Program> factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task A_guid_subject_with_an_email_claim_creates_the_workspace_and_can_then_reach_it()
    {
        var client = WithInMemoryIdentity().CreateClient();

        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces")
        {
            Content = JsonContent.Create(new { name = "Luca Test SRL", industry = "Software", country = "CH" }),
        };
        create.Headers.Add("X-User-Id", Subject);
        create.Headers.Add(TestUserIdAuthenticationHandler.UserEmailHeaderName, Email);
        var created = await client.SendAsync(create);
        var createdBody = await created.Content.ReadAsStringAsync();

        Assert.True(created.StatusCode == HttpStatusCode.Created, $"HTTP {(int)created.StatusCode}: {createdBody}");
        using var createdDoc = JsonDocument.Parse(createdBody);
        var tenantId = createdDoc.RootElement.GetProperty("id").GetGuid();
        Assert.Equal("Admin", createdDoc.RootElement.GetProperty("role").GetString());

        // The half that matters: the creator's row must be keyed by the `oid` too, or every later
        // membership lookup (all of which match Email OR ExternalSubjectId against the token's
        // `oid`) fails to find the creator and the workspace they just made is unreachable.
        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/workspaces");
        list.Headers.Add("X-User-Id", Subject);
        list.Headers.Add(TestUserIdAuthenticationHandler.UserEmailHeaderName, Email);
        var listed = await client.SendAsync(list);
        var listedBody = await listed.Content.ReadAsStringAsync();

        Assert.True(listed.StatusCode == HttpStatusCode.OK, $"HTTP {(int)listed.StatusCode}: {listedBody}");
        using var listedDoc = JsonDocument.Parse(listedBody);
        Assert.Contains(
            listedDoc.RootElement.GetProperty("workspaces").EnumerateArray(),
            w => w.GetProperty("id").GetGuid() == tenantId);

        using var members = new HttpRequestMessage(HttpMethod.Get, $"/api/workspaces/{tenantId}/members");
        members.Headers.Add("X-User-Id", Subject);
        var membersResponse = await client.SendAsync(members);
        var membersBody = await membersResponse.Content.ReadAsStringAsync();

        Assert.True(membersResponse.StatusCode == HttpStatusCode.OK, $"HTTP {(int)membersResponse.StatusCode}: {membersBody}");
        Assert.Contains(Email, membersBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_guid_subject_without_an_email_claim_is_400_with_a_named_reason_and_writes_nothing()
    {
        var client = WithInMemoryIdentity().CreateClient();

        using var create = new HttpRequestMessage(HttpMethod.Post, "/api/workspaces")
        {
            Content = JsonContent.Create(new { name = "Luca Test SRL" }),
        };
        create.Headers.Add("X-User-Id", Subject);
        var created = await client.SendAsync(create);
        var body = await created.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, created.StatusCode);
        Assert.Contains("no email address", body, StringComparison.Ordinal);
        Assert.DoesNotContain("is not a valid email address to invite", body, StringComparison.Ordinal);

        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/workspaces");
        list.Headers.Add("X-User-Id", Subject);
        var listed = await client.SendAsync(list);

        Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
        using var listedDoc = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
        Assert.Empty(listedDoc.RootElement.GetProperty("workspaces").EnumerateArray());
    }

    private WebApplicationFactory<Program> WithInMemoryIdentity()
    {
        var gateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));
        return _baseFactory.WithInMemoryAskEngine(gateway);
    }
}
