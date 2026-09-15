using Raffa.Api.Infrastructure;
using Raffa.Identity.Workspace.Application;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Api;

/// <summary>
/// Maps `POST /api/workspaces/{tenantId}/invites` (task E14/F02/US01/T01, wave w14; ADR-025 §D.1a —
/// "the highest-priority security task of the wave"). Moved out of
/// <see cref="WorkspaceEndpointExtensions"/> together with the guard this task adds in front of it:
/// on the wave's base checkout the route took a body, the service and a `CancellationToken` only —
/// no `HttpContext`, no claim, no membership check — so anyone who could reach the API and guess a
/// tenant GUID could grant themselves Admin over another customer's contracts
/// (`WorkspaceEndpointExtensions.cs:59-97` pre-wave; ADR-025 Context fact 1). It has been
/// survivable only because discovery was a `localStorage` array in one browser — NW-01/NW-02 are
/// exactly what make it live, which is why the guard ships one phase earlier than either.
///
/// <para>
/// The guard (ADR-025 Rule D.1a), in order: a presented identity (<see cref="ICallerIdentity"/>) —
/// else <b>401</b>; a live <c>workspace_membership</c> for that identity in the <b>route</b> tenant,
/// resolved by <see cref="ResolveMembershipRoleAsync"/> below — else <b>404</b>, never 403 (a 403 on
/// a tenant the caller does not belong to is a tenant-existence oracle, ADR-025 §B Rule B1); that
/// membership's role is <see cref="WorkspaceRoleName.Admin"/> — else <b>403</b>. The tenant is
/// always the <b>route</b> value, never `X-Tenant-Id` (ADR-022 w14 footer clause 3: "not an input to
/// a membership route"). ADR-025 Rule D.1b: an Admin may invite another Admin — what is refused is a
/// non-Admin reaching the endpoint at all, and any self-assignment (the caller's own role is never
/// read from a body, anywhere in this wave).
/// </para>
///
/// <para>
/// This guard deliberately does not call <see cref="WorkspaceRoleResolver.ResolveAsync"/>: that
/// method tries an authenticated principal's claims, then the interim `X-Role`/`X-Workspace-Role`
/// header, <i>before</i> it ever reaches its own membership branch — a caller with no relationship
/// to the route tenant could set a static `X-Role: Admin` header and be treated as Admin, reopening
/// the exact hole this task exists to close (ADR-025 §D.1a; the non-negotiable §H spoofed-header
/// test). <see cref="WorkspaceRoleResolver"/>'s membership branch is <see langword="private"/>, and
/// that file is this task's own "do not touch" (its header branch is deleted by
/// `E14/F02/US02/T01` in phase 2, not here; single-writer per phase) — so
/// <see cref="ResolveMembershipRoleAsync"/> runs the identical tenant-scoped
/// `workspace_user ⋈ workspace_membership ⋈ workspace_role` join locally, keyed on the identity
/// <see cref="ICallerIdentity"/> already resolved, and never consults a claim or a role header.
/// <see cref="WorkspaceRoleResolver"/> itself stays fully unedited by this task.
/// <see cref="WorkspaceMembershipService.InviteAsync"/> is also unedited: it still writes a
/// <b>live</b> membership at invite time in phase 1 — ADR-025's "an invitation is an offer, not a
/// grant" redesign (the pending `workspace_invitation` table) is phase 3's `E15/F01/US01/T01`. This
/// task's whole job is the guard in front of the existing write, not a new invite lifecycle.
/// </para>
///
/// <para>
/// <b>Task E15/F01/US01/T01 (phase 3, wave w14; ADR-025 §D.1/§D.5, ADR-026 §D5)</b> is the
/// redesign the paragraph above named as still pending: <see cref="InviteAsync"/> now delegates the
/// write to <see cref="WorkspaceInvitationService.IssueAsync"/> (token mint/hash, no membership —
/// see that type's own doc comment), and this file gains
/// <see cref="RevokeInvitationAsync"/> (`DELETE /api/workspaces/{tenantId}/invites/{id}`, Admin
/// only, ADR-025 Rule D.5). The guard above — 401 → 404 → 403 — is unchanged; only what happens
/// once it passes is new.
/// </para>
///
/// <para>
/// <b>Fix 2026-09-14.</b> This file also gains <see cref="AcceptForIdentityAsync"/>
/// (`POST /api/workspaces/{tenantId}/invites/accept`) — the identity-keyed counterpart of
/// <see cref="InvitationsEndpointExtensions.AcceptInvitationAsync"/>'s token-keyed accept, for a
/// caller with no live membership anywhere but a live invitation their own prior
/// `GET /api/workspaces` already surfaced (see that method's own doc comment). It takes the
/// <b>opposite</b> guard from every other route in this file: no membership check at all, because
/// the caller is definitionally not a member yet.
/// </para>
/// </summary>
public static class WorkspaceInvitesEndpointExtensions
{
    public static IEndpointRouteBuilder MapWorkspaceInviteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/workspaces/{tenantId}/invites", InviteAsync);
        endpoints.MapDelete("/api/workspaces/{tenantId}/invites/{id}", RevokeInvitationAsync);
        endpoints.MapPost("/api/workspaces/{tenantId}/invites/accept", AcceptForIdentityAsync);
        return endpoints;
    }

    private static async Task<IResult> InviteAsync(
        string tenantId,
        InviteRequest request,
        ICallerIdentity callerIdentity,
        IdentityWorkspaceDbContext dbContext,
        ITenantContext tenantContext,
        WorkspaceInvitationService invitationService,
        CancellationToken cancellationToken)
    {
        // ADR-025 Rule D.1a, step 1: no identity presented -> 401.
        var identity = callerIdentity.Resolve();
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        if (!Guid.TryParse(tenantId, out var tenantGuid))
        {
            return Results.BadRequest("The tenant id in the route must be a GUID.");
        }

        // ADR-025 Rule D.1c / ADR-022 w14 footer clause 3: the tenant is the route value, verified
        // against membership — X-Tenant-Id is not an input to this endpoint.
        var routeTenantId = new TenantId(tenantGuid);

        // ADR-025 Rule D.1a, steps 2-3 — membership only; see ResolveMembershipRoleAsync's own doc
        // comment for why this does not call WorkspaceRoleResolver.ResolveAsync. null -> no live
        // membership in this tenant for this identity -> 404, never 403 (§B Rule B1: a 403 on a
        // tenant the caller does not belong to is a tenant-existence oracle); any non-Admin role ->
        // 403.
        var callerRole = await ResolveMembershipRoleAsync(
                dbContext, tenantContext, routeTenantId, identity, cancellationToken)
            .ConfigureAwait(false);

        if (callerRole is null)
        {
            return Results.NotFound();
        }

        if (callerRole != WorkspaceRoleName.Admin)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.BadRequest("An 'email' is required.");
        }

        if (!WorkspaceRoleClaimResolver.TryResolve(request.Role, out var role))
        {
            return Results.BadRequest(
                $"'{request.Role}' is not a recognized role (Admin/Procurement/Legal/Finance/ReadOnly).");
        }

        // ADR-025 Rule D.1b: role comes from the body unconstrained beyond the catalog check above
        // — an Admin may invite another Admin. Only reaching this line was gated.
        var issueResult = await invitationService
            .IssueAsync(routeTenantId, request.Email, role, identity, cancellationToken)
            .ConfigureAwait(false);

        // ADR-025 §D.1d / implication 9: a live MEMBERSHIP at this role (or the per-tenant cap, or two
        // concurrent invites racing the unique index) is a 409 (Conflict), never a 400 -- the request
        // is well-formed, it conflicts with existing state. A live INVITATION is no longer a 409: it
        // is replaced in one transaction (ADR-025 §J.2b, ADR-026 w15 footer §9). Task E17/F01/US01/T01
        // (ADR-026 w15 footer §1/§3/§8): the 201 carries `deliveryOutcome` (sent | mail_failed |
        // no_transport, `sent` iff `mailDelivered`) and `identityProvisioned`; a directory that would
        // not provision the guest is a 502 whose body carries `failureReason` from the closed set
        // (consent_missing | provisioning_failed | directory_unavailable) -- and no invitation exists.
        // Every other failure (malformed email, unseeded role) stays the pre-existing 400.
        return issueResult.Status switch
        {
            MembershipOperationStatus.Success => Results.Created(
                $"/api/workspaces/{tenantId}/invites/{issueResult.Invitation!.Id.Value}",
                new
                {
                    id = issueResult.Invitation.Id.Value,
                    email = issueResult.Invitation.Email,
                    role = role.ToString(),
                    expiresAt = issueResult.Invitation.ExpiresAt,
                    // ADR-025 Rule C9 / ADR-026 w15 footer §4: a fragment, never a path/query
                    // string; absolute ({Invitations__AcceptUrlBase}/invite/accept#...) when a base
                    // is configured, the w14 site-relative form otherwise.
                    acceptUrl = issueResult.AcceptUrl,
                    deliveryOutcome = issueResult.DeliveryOutcome.ToWireValue(),
                    mailDelivered = issueResult.MailDelivered,
                    identityProvisioned = issueResult.IdentityProvisioned,
                }),
            MembershipOperationStatus.ProvisioningFailed => Results.Json(
                new { failureReason = issueResult.ProvisioningFailureReason!.Value.ToWireValue() },
                statusCode: StatusCodes.Status502BadGateway),
            MembershipOperationStatus.Conflict => Results.Conflict(issueResult.Error),
            _ => Results.BadRequest(issueResult.Error),
        };
    }

    /// <summary>
    /// `DELETE /api/workspaces/{tenantId}/invites/{id}` (task E15/F01/US01/T01, wave w14; ADR-025
    /// Rule D.5, AC-11): revokes a still-live invitation — 204, and the link stops working
    /// immediately (nothing caches it; the very next pre-accept/accept re-reads
    /// <see cref="Domain.WorkspaceInvitation.RevokedAt"/>). Same identity → membership → Admin guard
    /// as <see cref="InviteAsync"/>, reusing this file's own <see cref="ResolveMembershipRoleAsync"/>
    /// rather than a new copy.
    /// </summary>
    private static async Task<IResult> RevokeInvitationAsync(
        string tenantId,
        string id,
        ICallerIdentity callerIdentity,
        IdentityWorkspaceDbContext dbContext,
        ITenantContext tenantContext,
        WorkspaceInvitationService invitationService,
        CancellationToken cancellationToken)
    {
        var identity = callerIdentity.Resolve();
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        if (!Guid.TryParse(tenantId, out var tenantGuid))
        {
            return Results.BadRequest("The tenant id in the route must be a GUID.");
        }

        if (!Guid.TryParse(id, out var invitationGuid))
        {
            return Results.BadRequest("The invitation id in the route must be a GUID.");
        }

        var routeTenantId = new TenantId(tenantGuid);

        var callerRole = await ResolveMembershipRoleAsync(
                dbContext, tenantContext, routeTenantId, identity, cancellationToken)
            .ConfigureAwait(false);

        if (callerRole is null)
        {
            return Results.NotFound();
        }

        if (callerRole != WorkspaceRoleName.Admin)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var status = await invitationService
            .RevokeAsync(routeTenantId, new EntityId(invitationGuid), identity, cancellationToken)
            .ConfigureAwait(false);

        return status == MembershipOperationStatus.Success ? Results.NoContent() : Results.NotFound();
    }

    /// <summary>
    /// `POST /api/workspaces/{tenantId}/invites/accept` (fix 2026-09-14) — see
    /// <see cref="WorkspaceInvitationService.AcceptForIdentityAsync"/>'s own doc comment for the full
    /// justification (extends ADR-025 §F.3 Exception 2). No body, no token, no membership guard —
    /// unlike <see cref="InviteAsync"/>/<see cref="RevokeInvitationAsync"/> above, the caller is
    /// <b>not</b> a member of the route tenant yet, by construction; the only input is the caller's
    /// own identity. 401 with no identity; 404 for "no live invitation matches this identity here"
    /// (unknown tenant and "not actually invited" collapse to the same answer, Rule B1); 410 expired;
    /// 200 <c>{ workspaceId, workspaceName, role }</c> on success — the identical shape
    /// <see cref="InvitationsEndpointExtensions.AcceptInvitationAsync"/> already returns for the
    /// token path.
    /// </summary>
    private static async Task<IResult> AcceptForIdentityAsync(
        string tenantId,
        ICallerIdentity callerIdentity,
        WorkspaceInvitationService invitationService,
        CancellationToken cancellationToken)
    {
        var identity = callerIdentity.Resolve();
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        if (!Guid.TryParse(tenantId, out var tenantGuid))
        {
            return Results.BadRequest("The tenant id in the route must be a GUID.");
        }

        var result = await invitationService
            .AcceptForIdentityAsync(new TenantId(tenantGuid), identity, callerIdentity.ResolveEmail(), cancellationToken)
            .ConfigureAwait(false);

        return result.Status switch
        {
            MembershipOperationStatus.Success => Results.Ok(new
            {
                workspaceId = result.WorkspaceId.Value,
                workspaceName = result.WorkspaceName,
                role = result.Role!.Value.ToString(),
            }),
            MembershipOperationStatus.Expired => Results.StatusCode(StatusCodes.Status410Gone),
            _ => Results.NotFound(),
        };
    }

    /// <summary>
    /// The guard's membership-only role lookup (ADR-025 §D.1a steps 2-3) — see this class's own doc
    /// comment for why it is not a call to <see cref="WorkspaceRoleResolver.ResolveAsync"/>. Mirrors
    /// <c>WorkspaceRoleResolver</c>'s private <c>ResolveMembershipRoleAsync</c>
    /// (`WorkspaceRoleResolver.cs:85-119`) exactly — same tenant-scoped join, same
    /// highest-precedence-wins resolution via <see cref="WorkspaceRoleClaimResolver"/> — but keyed on
    /// <paramref name="callerIdentity"/> (already resolved by <see cref="ICallerIdentity"/>) instead
    /// of re-reading a header, and with no claim/header fallback at all. When
    /// `E14/F02/US02/T01` (phase 2) deletes <c>WorkspaceRoleResolver</c>'s header branch, folding
    /// this back into one shared membership lookup becomes possible without reopening this guard —
    /// not done here to stay inside this task's own "do not touch `WorkspaceRoleResolver.cs`"
    /// boundary.
    /// </summary>
    private static async Task<WorkspaceRoleName?> ResolveMembershipRoleAsync(
        IdentityWorkspaceDbContext dbContext,
        ITenantContext tenantContext,
        TenantId tenantId,
        string callerIdentity,
        CancellationToken cancellationToken)
    {
        using var tenantScope = tenantContext.BeginScope(tenantId);

        // One join, tenant-scoped on every leg (RLS backstops it — ADR-009), identical in shape to
        // WorkspaceRoleResolver.ResolveMembershipRoleAsync.
        var roleNames = await (
            from user in dbContext.WorkspaceUsers
            join membership in dbContext.WorkspaceMemberships on user.Id equals membership.WorkspaceUserId
            join role in dbContext.WorkspaceRoles on membership.WorkspaceRoleId equals role.Id
            where user.TenantId == tenantId
                && membership.TenantId == tenantId
                && role.TenantId == tenantId
                && (user.Email == callerIdentity || user.ExternalSubjectId == callerIdentity)
            select role.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return WorkspaceRoleClaimResolver.TryResolve(roleNames.Select(name => name.ToString()), out var resolved)
            ? resolved
            : null;
    }
}
