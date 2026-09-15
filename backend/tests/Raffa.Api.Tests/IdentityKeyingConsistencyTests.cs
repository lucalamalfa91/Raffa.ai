using System.Net;
using System.Net.Http.Json;
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
/// Named test S-T24 (ADR-010 w16 footer §4; task E18/F02/US01/T01, wave w16 NW-07): the
/// "is this identity this <c>workspace_user</c>?" predicate is one mechanism now, not four
/// disagreeing ones. Every <see cref="WorkspaceUser"/> row here is seeded directly through the
/// host's own <see cref="IdentityWorkspaceDbContext"/> — deliberately not
/// <see cref="PresentedCallersAsMembersExtensions.WithPresentedCallersAsMembers"/>, whose
/// auto-membership-by-header shim would itself grant by <c>Email</c> and mask exactly the S16-1
/// defect this class proves fixed.
/// </summary>
public sealed class IdentityKeyingConsistencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// S-T24(a): a <see cref="WorkspaceUser"/> whose <c>Email</c> is set to another member's
    /// <c>oid</c> string confers no membership (404, <see cref="Raffa.Api.Infrastructure.CallerContext"/>)
    /// and no role (403, <see cref="Raffa.Api.Infrastructure.WorkspaceRoleResolver"/>) — ADR-010 w16
    /// footer S16-1, the email leg deleted from both authorization comparators. The row is real (it
    /// has its own, different, live-membership <c>ExternalSubjectId</c>) — only the string sitting in
    /// its <c>Email</c> column happens to look like a different member's <c>oid</c>, exactly the "one
    /// ordinary invite away from being a grant" shape the footer names.
    /// </summary>
    [Fact]
    public async Task S_T24a_email_equal_to_another_members_oid_confers_no_membership_and_no_role()
    {
        using var factory = new RaffaApiFactory { ImplicitTenantAdmin = false };
        var tenantId = Guid.NewGuid();
        const string maskedAsAnotherOid = "11111111-1111-1111-1111-111111111111";

        await SeedMembershipAsync(
            factory, tenantId, email: maskedAsAnotherOid, externalSubjectId: "this-rows-own-real-oid",
            WorkspaceRoleName.Admin);

        var client = factory.CreateClient();

        // Membership half: CallerContext.ResolveTenantAsync -> 404, never 200/500.
        using var listRequest = new HttpRequestMessage(HttpMethod.Get, "/api/conversations");
        listRequest.Headers.Add("X-Tenant-Id", tenantId.ToString());
        listRequest.Headers.Add("X-User-Id", maskedAsAnotherOid);
        var listResponse = await client.SendAsync(listRequest);
        Assert.Equal(HttpStatusCode.NotFound, listResponse.StatusCode);

        // Role half: WorkspaceRoleResolver.IsAdminAsync -> 403, never 201. Membership itself already
        // failed above, so this is also, independently, a 404-shaped tenant -- assert the Admin-only
        // route never answers 201 either way (WorkspaceInviteAuthorizationTests.Non_member_gets_404_never_403
        // is the sibling proof that a non-member gets 404 here, never 403 or 201).
        using var inviteRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{tenantId}/invites")
        {
            Content = JsonContent.Create(new { email = "someone@acme.example", role = "Procurement" }),
        };
        inviteRequest.Headers.Add("X-User-Id", maskedAsAnotherOid);
        var inviteResponse = await client.SendAsync(inviteRequest);
        Assert.NotEqual(HttpStatusCode.Created, inviteResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.OK, inviteResponse.StatusCode);
    }

    /// <summary>
    /// S-T24(b), <c>CallerContext</c>/<c>WorkspaceRoleResolver</c> half: a subject stored with
    /// non-lowercase characters resolves <b>identically</b> through
    /// <see cref="Raffa.Api.Infrastructure.CallerContext"/> and
    /// <see cref="Raffa.Api.Infrastructure.WorkspaceRoleResolver"/> — listed and usable, never
    /// listed-and-404 (ADR-010 w16 footer S16-2: each leg normalises itself instead of the whole
    /// identity being forced through one case fold). Runs over the EF Core InMemory provider
    /// (<see cref="InMemoryAskEngineFactory.WithInMemoryAskEngine"/>), which has no RLS concept at
    /// all — the one thing this class can prove from <c>Raffa.Api.Tests</c> is that the *application*
    /// query logic these two seams share treats a mixed-case subject consistently.
    ///
    /// <para>
    /// <b>The <c>GET /api/workspaces</c> (<see cref="WorkspaceDirectoryService"/>) third of S-T24(b)
    /// is proven separately</b>, in
    /// <c>Raffa.Api.Tests.WorkspaceDirectoryEndpointTests.A_mixed_case_subject_is_listed_and_usable_never_listed_and_404</c>
    /// (real Postgres, real <c>identity_self</c> RLS policy) — not here. That query's cross-tenant
    /// discovery scan runs with **no tenant scope at all** (candidate discovery precedes knowing
    /// which tenant); under the InMemory provider that same call reliably returns zero candidates
    /// for a seeded row, seed order and factory shape notwithstanding — reproduced independently of
    /// this task's own change, so it reads as an EF Core InMemory-provider limitation on this
    /// specific query shape, not a product defect. Asserting it here would be exactly the "vacuous
    /// pass, or a hidden false negative" trap the class doc comment already names for RLS; proving
    /// it against the real policy is strictly the stronger test anyway, since S16-2's whole point is
    /// the GUC/policy pairing, which InMemory cannot exercise regardless of this method's own
    /// application-level query logic.
    /// </para>
    /// </summary>
    [Fact]
    public async Task S_T24b_a_mixed_case_subject_resolves_identically_everywhere_it_is_checked()
    {
        var gateway = new RecordingAiGateway(
            new FixtureAiGateway(new AiGatewayModelOptions(), SystemClock.Instance, new AiGatewayOcrOptions()));
        using var baseFactory = new RaffaApiFactory { ImplicitTenantAdmin = false };
        var factory = baseFactory.WithInMemoryAskEngine(gateway);
        var tenantId = Guid.NewGuid();
        const string mixedCaseOid = "AbC-9f2D-Entra-Object-Id";

        await SeedMembershipAsync(
            factory, tenantId, email: "member@acme.example", externalSubjectId: mixedCaseOid, WorkspaceRoleName.Admin);

        var client = factory.CreateClient();

        // CallerContext: the same identity, presented the same way, is a live member -- 200, never 404.
        using var conversationsRequest = new HttpRequestMessage(HttpMethod.Get, "/api/conversations");
        conversationsRequest.Headers.Add("X-Tenant-Id", tenantId.ToString());
        conversationsRequest.Headers.Add("X-User-Id", mixedCaseOid);
        var conversationsResponse = await client.SendAsync(conversationsRequest);
        Assert.Equal(HttpStatusCode.OK, conversationsResponse.StatusCode);

        // WorkspaceRoleResolver: the same identity resolves its real Admin role -- 201, never 403.
        using var inviteRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/workspaces/{tenantId}/invites")
        {
            Content = JsonContent.Create(new { email = "invitee@acme.example", role = "Procurement" }),
        };
        inviteRequest.Headers.Add("X-User-Id", mixedCaseOid);
        var inviteResponse = await client.SendAsync(inviteRequest);
        Assert.Equal(HttpStatusCode.Created, inviteResponse.StatusCode);
    }

    // ----- helpers -----

    private static async Task SeedMembershipAsync(
        WebApplicationFactory<Program> factory, Guid tenantId, string email, string externalSubjectId,
        WorkspaceRoleName roleName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityWorkspaceDbContext>();

        var tenant = new TenantId(tenantId);
        // A real Workspace row, not only the user/role/membership triad: S_T24b's invite assertion
        // reaches WorkspaceInvitationService.IssueAsync, which reads the workspace row (for its
        // Name, passed to IGuestProvisioner) -- absent, IssueAsync still runs (workspace?.Name ??
        // string.Empty), but this seeds the realistic shape CreateWorkspaceAsync would have produced,
        // the same convention WorkspaceInviteOutcomeTests/DocumentAdminActionsAuthorizationTests
        // already use for their own directly-seeded members.
        db.Workspaces.Add(new WorkspaceTenant { TenantId = tenant, Name = $"Workspace {tenantId}", CreatedAt = Now });

        // The full role catalog, not only roleName: every workspace has Admin/Procurement/Legal/
        // Finance/ReadOnly from day one (product spec §3.1, WorkspaceFactory.
        // CreateWorkspaceWithDefaultRoles' own invariant) -- that factory cannot be reused directly
        // here since it always mints its own TenantId rather than accepting this method's
        // caller-supplied one. S_T24b's invite assertion invites a *different* role than the
        // membership it seeds (Procurement, for an Admin caller); without every role row present,
        // WorkspaceMembershipService.InviteAsync 400s with "no seeded 'Procurement' role", a
        // fixture gap unrelated to the identity-keying property this class actually proves.
        var roles = Enum.GetValues<WorkspaceRoleName>()
            .Select(name => new WorkspaceRole { TenantId = tenant, Name = name, CreatedAt = Now })
            .ToArray();
        db.WorkspaceRoles.AddRange(roles);
        var role = roles.Single(r => r.Name == roleName);

        var user = new WorkspaceUser
        {
            TenantId = tenant,
            Email = email,
            ExternalSubjectId = externalSubjectId,
            CreatedAt = Now,
        };
        db.WorkspaceUsers.Add(user);
        db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            TenantId = tenant,
            WorkspaceUserId = user.Id,
            WorkspaceRoleId = role.Id,
            CreatedAt = Now,
        });

        await db.SaveChangesAsync();
    }
}
