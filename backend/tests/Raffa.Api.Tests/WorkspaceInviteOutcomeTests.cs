using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Raffa.AiGateway.Configuration;
using Raffa.AiGateway.Fixtures;
using Raffa.Api.Tests.TestSupport;
using Raffa.Identity.Workspace.Application;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Raffa.Api.Tests;

/// <summary>
/// Task E17/F01/US01/T01 (wave w15, NW-67/NW-68/NW-69; ADR-026 w15 footers §1/§3/§8/§9, ADR-025
/// §J): what <c>POST /api/workspaces/{tenantId}/invites</c> says on the wire, over the real HTTP
/// pipeline and the in-memory identity store, with the two seams scripted -- the 201's
/// <c>deliveryOutcome</c> and <c>identityProvisioned</c>; the biconditional
/// <c>deliveryOutcome == "sent" ⇔ mailDelivered</c> across all three outcomes, so
/// <c>mail_failed</c> with <c>mailDelivered: true</c> is not expressible; a fresh provision and an
/// already-present guest being byte-identical in shape; the 502 whose <c>failureReason</c> comes
/// from the closed set and after which no invitation exists; re-issue by replacement; and the
/// unchanged 409s. The guard in front (401 → 404 → 403) is
/// <see cref="WorkspaceInviteAuthorizationTests"/>' own proof and is not repeated here.
/// </summary>
public sealed class WorkspaceInviteOutcomeTests : IClassFixture<RaffaApiFactory>
{
    private const string AcceptUrlBase = "https://app.dev.raffa.example";
    private const string Admin = "admin@acme.example";
    private static readonly DateTimeOffset Now = new(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly WebApplicationFactory<Program> _baseFactory;

    public WorkspaceInviteOutcomeTests(RaffaApiFactory factory)
    {
        _baseFactory = factory;
    }

    [Fact]
    public async Task A_provisioned_guest_with_the_mail_sent_is_201_sent_with_an_absolute_link_and_the_oid_bound()
    {
        var host = CreateHost(new ScriptedProvisioner(GuestProvisioningResult.Provisioned("guest-oid-1")), new ScriptedMailer(configured: true, delivered: true));
        var tenantId = Guid.NewGuid();
        await SeedAdminAsync(host.Factory, tenantId);

        var response = await InviteAsync(host.Factory.CreateClient(), tenantId, "new.hire@acme.example");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("sent", body.RootElement.GetProperty("deliveryOutcome").GetString());
        Assert.True(body.RootElement.GetProperty("mailDelivered").GetBoolean());
        Assert.True(body.RootElement.GetProperty("identityProvisioned").GetBoolean());
        var acceptUrl = body.RootElement.GetProperty("acceptUrl").GetString()!;
        Assert.StartsWith(AcceptUrlBase + "/invite/accept#", acceptUrl);
        Assert.DoesNotContain('?', acceptUrl);

        // ADR-025 §J.3b: the guest's object id is the invited user's external subject from now on.
        using var scope = host.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();
        var user = await db.WorkspaceUsers.SingleAsync(u => u.Email == "new.hire@acme.example");
        Assert.Equal("guest-oid-1", user.ExternalSubjectId);

        Assert.Contains(host.Audit.Entries, e => e.Action == WorkspaceInvitationService.GuestProvisionedAuditAction);
        Assert.Contains(host.Audit.Entries, e => e.Action == WorkspaceInvitationService.MailSentAuditAction);
    }

    [Fact]
    public async Task The_delivery_outcome_and_mailDelivered_agree_across_all_three_outcomes()
    {
        // ADR-026 w15 footer §8: `sent` if and only if `mailDelivered`, computed from one call.
        var sent = await InviteOnceAsync(new ScriptedMailer(configured: true, delivered: true));
        Assert.Equal("sent", sent.GetProperty("deliveryOutcome").GetString());
        Assert.True(sent.GetProperty("mailDelivered").GetBoolean());

        var failed = await InviteOnceAsync(new ScriptedMailer(configured: true, delivered: false));
        Assert.Equal("mail_failed", failed.GetProperty("deliveryOutcome").GetString());
        Assert.False(failed.GetProperty("mailDelivered").GetBoolean());
        Assert.StartsWith(AcceptUrlBase + "/invite/accept#", failed.GetProperty("acceptUrl").GetString());

        var noTransport = await InviteOnceAsync(new ScriptedMailer(configured: false, delivered: false));
        Assert.Equal("no_transport", noTransport.GetProperty("deliveryOutcome").GetString());
        Assert.False(noTransport.GetProperty("mailDelivered").GetBoolean());
    }

    [Fact]
    public async Task A_fresh_provision_and_an_already_present_guest_are_identical_in_shape()
    {
        // ADR-025 §J.4a: the invite form must not become a directory-enumeration oracle.
        var fresh = await InviteOnceAsync(new ScriptedMailer(configured: false, delivered: false), GuestProvisioningResult.Provisioned("g-1"));
        var present = await InviteOnceAsync(new ScriptedMailer(configured: false, delivered: false), GuestProvisioningResult.AlreadyPresent("g-2"));

        Assert.True(fresh.GetProperty("identityProvisioned").GetBoolean());
        Assert.True(present.GetProperty("identityProvisioned").GetBoolean());
        Assert.Equal(
            fresh.EnumerateObject().Select(p => p.Name).Order().ToArray(),
            present.EnumerateObject().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public async Task With_provisioning_not_configured_the_invite_is_link_only_and_identityProvisioned_is_false()
    {
        var host = CreateHost(new NullGuestProvisioner(), new ScriptedMailer(configured: false, delivered: false), new InvitationOptions());
        var tenantId = Guid.NewGuid();
        await SeedAdminAsync(host.Factory, tenantId);

        var response = await InviteAsync(host.Factory.CreateClient(), tenantId, "plain@acme.example");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("identityProvisioned").GetBoolean());
        Assert.Equal("no_transport", body.RootElement.GetProperty("deliveryOutcome").GetString());
        // No base configured: the w14 site-relative link, unchanged.
        Assert.StartsWith("/invite/accept#", body.RootElement.GetProperty("acceptUrl").GetString());
    }

    [Theory]
    [InlineData(GuestProvisioningFailureReason.ConsentMissing, "consent_missing")]
    [InlineData(GuestProvisioningFailureReason.ProvisioningFailed, "provisioning_failed")]
    [InlineData(GuestProvisioningFailureReason.DirectoryUnavailable, "directory_unavailable")]
    public async Task A_provisioning_failure_is_502_with_a_reason_from_the_closed_set_and_no_invitation_exists(
        GuestProvisioningFailureReason reason, string wireValue)
    {
        var mailer = new ScriptedMailer(configured: true, delivered: true);
        var host = CreateHost(new ScriptedProvisioner(GuestProvisioningResult.Failed(reason, "req-1")), mailer);
        var tenantId = Guid.NewGuid();
        await SeedAdminAsync(host.Factory, tenantId);

        var response = await InviteAsync(host.Factory.CreateClient(), tenantId, "blocked@acme.example");

        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(wireValue, body.RootElement.GetProperty("failureReason").GetString());

        // ADR-026 w15 footer §3: no row, no token, no mail -- and the slot is free for a retry.
        Assert.Equal(0, mailer.Sends);
        using var scope = host.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();
        Assert.Empty(await db.WorkspaceInvitations.ToListAsync());
        var failure = Assert.Single(host.Audit.Entries, e => e.Action == WorkspaceInvitationService.GuestProvisioningFailedAuditAction);
        Assert.Contains($"reason={wireValue}", failure.Detail);
    }

    [Fact]
    public async Task Re_inviting_a_live_address_replaces_the_invitation_instead_of_409()
    {
        var host = CreateHost(new NullGuestProvisioner(), new ScriptedMailer(configured: false, delivered: false));
        var tenantId = Guid.NewGuid();
        await SeedAdminAsync(host.Factory, tenantId);
        var client = host.Factory.CreateClient();

        var first = await InviteAsync(client, tenantId, "again@acme.example");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var second = await InviteAsync(client, tenantId, "again@acme.example");
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        using var firstBody = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        using var secondBody = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.NotEqual(firstBody.RootElement.GetProperty("id").GetGuid(), secondBody.RootElement.GetProperty("id").GetGuid());

        // ADR-025 §J.2b: exactly one live invitation, the old one revoked and audited as such.
        using var scope = host.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();
        var rows = await db.WorkspaceInvitations.Where(i => i.Email == "again@acme.example").ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, r => r.RevokedAt == null && r.AcceptedAt == null);
        Assert.Contains(host.Audit.Entries, e => e.Action == "workspace.invitation.revoked");
    }

    [Fact]
    public async Task An_address_that_already_holds_the_role_is_still_409()
    {
        var host = CreateHost(new NullGuestProvisioner(), new ScriptedMailer(configured: false, delivered: false));
        var tenantId = Guid.NewGuid();
        await SeedAdminAsync(host.Factory, tenantId);

        // The Admin invites themselves at the role they already hold: a live MEMBERSHIP is not a
        // re-issue case (ADR-026 w15 footer §9).
        var response = await InviteAsync(host.Factory.CreateClient(), tenantId, Admin, "Admin");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task The_cap_on_live_invitations_is_409_and_audited()
    {
        var host = CreateHost(
            new NullGuestProvisioner(), new ScriptedMailer(configured: false, delivered: false),
            new InvitationOptions { AcceptUrlBase = AcceptUrlBase, LiveInvitationCap = 1 });
        var tenantId = Guid.NewGuid();
        await SeedAdminAsync(host.Factory, tenantId);
        var client = host.Factory.CreateClient();

        Assert.Equal(HttpStatusCode.Created, (await InviteAsync(client, tenantId, "one@acme.example")).StatusCode);
        var over = await InviteAsync(client, tenantId, "two@acme.example");

        Assert.Equal(HttpStatusCode.Conflict, over.StatusCode);
        Assert.Single(host.Audit.Entries, e => e.Action == WorkspaceInvitationService.CapReachedAuditAction);
    }

    // ----- helpers -----

    private async Task<JsonElement> InviteOnceAsync(ScriptedMailer mailer, GuestProvisioningResult? provisioning = null)
    {
        var host = CreateHost(new ScriptedProvisioner(provisioning ?? GuestProvisioningResult.Provisioned("guest-oid")), mailer);
        var tenantId = Guid.NewGuid();
        await SeedAdminAsync(host.Factory, tenantId);

        var response = await InviteAsync(host.Factory.CreateClient(), tenantId, "invitee@acme.example");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private Host CreateHost(IGuestProvisioner provisioner, IInvitationMailer mailer, InvitationOptions? options = null)
    {
        var audit = new RecordingAuditWriter();
        var gateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));
        var factory = _baseFactory
            .WithInMemoryAskEngine(gateway, auditWriter: audit)
            .WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            {
                // The two seams and the policy, scripted per test -- the same "swap the seam, keep
                // the real service" shape the document tests use for storage and audit.
                services.RemoveAll<IGuestProvisioner>();
                services.AddSingleton(provisioner);
                services.RemoveAll<IInvitationMailer>();
                services.AddSingleton(mailer);
                services.RemoveAll<InvitationOptions>();
                services.AddSingleton(options ?? new InvitationOptions { AcceptUrlBase = AcceptUrlBase });
            }));
        return new Host(factory, audit);
    }

    private static async Task<HttpResponseMessage> InviteAsync(HttpClient client, Guid tenantId, string inviteEmail, string role = "Procurement")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{tenantId}/invites")
        {
            Content = JsonContent.Create(new { email = inviteEmail, role }),
        };
        request.Headers.Add("X-User-Id", Admin);
        return await client.SendAsync(request);
    }

    private static async Task SeedAdminAsync(WebApplicationFactory<Program> factory, Guid tenantId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();

        var tenant = new TenantId(tenantId);
        var user = new WorkspaceUser { TenantId = tenant, Email = Admin, CreatedAt = Now };
        var adminRole = new WorkspaceRole { TenantId = tenant, Name = WorkspaceRoleName.Admin, CreatedAt = Now };
        var procurementRole = new WorkspaceRole { TenantId = tenant, Name = WorkspaceRoleName.Procurement, CreatedAt = Now };
        db.Workspaces.Add(new WorkspaceTenant { TenantId = tenant, Name = "Acme Procurement", CreatedAt = Now });
        db.WorkspaceUsers.Add(user);
        db.WorkspaceRoles.AddRange(adminRole, procurementRole);
        db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            TenantId = tenant,
            WorkspaceUserId = user.Id,
            WorkspaceRoleId = adminRole.Id,
            CreatedAt = Now,
        });

        await db.SaveChangesAsync();
    }

    private sealed record Host(WebApplicationFactory<Program> Factory, RecordingAuditWriter Audit);

    private sealed class ScriptedProvisioner(GuestProvisioningResult result) : IGuestProvisioner
    {
        public Task<GuestProvisioningResult> EnsureGuestAsync(string email, string workspaceName, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class ScriptedMailer(bool configured, bool delivered) : IInvitationMailer
    {
        public bool IsConfigured => configured;

        public int Sends { get; private set; }

        public Task<bool> TrySendAsync(
            string email, string workspaceName, WorkspaceRoleName role, string acceptUrl, DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default)
        {
            Sends++;
            return Task.FromResult(delivered);
        }
    }
}
