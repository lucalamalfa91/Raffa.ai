using Raffa.Api.Infrastructure;
using Raffa.Identity.Workspace.Application;
using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;
using Raffa.SharedKernel;

namespace Raffa.Api;

/// <summary>
/// The portal admin surface for Raffa staff (<see cref="PlatformAdminOptions"/>): onboard an external
/// customer by creating their workspace and inviting their first Admin.
///
/// <para>
/// It composes what already exists rather than adding a second path into a tenant. The workspace is
/// created through <see cref="WorkspaceProvisioningService"/>, so the platform admin becomes that
/// workspace's Admin exactly as any creator does (ADR-025 Rule D.2b): it shows up in their own
/// <c>GET /api/workspaces</c> list and they can enter it, empty, and try it the way the customer
/// will. The customer is then invited as <see cref="WorkspaceRoleName.Admin"/> through
/// <see cref="WorkspaceInvitationService.IssueAsync"/>, the same offer-not-grant lifecycle (guest
/// provisioning, mail, accept link) every other invitation uses. Nothing here reads across tenants,
/// so there is no RLS exception to justify.
/// </para>
///
/// <para>
/// Guard: 401 without an identity; <b>404</b> (never 403) for anyone who is not a platform admin, so
/// the admin surface is not discoverable (ADR-025 §B Rule B1).
/// </para>
/// </summary>
public static class PlatformAdminEndpointExtensions
{
    public static IEndpointRouteBuilder MapPlatformAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/admin/me", GetStatus);
        endpoints.MapPost("/api/admin/workspaces", CreateCustomerWorkspaceAsync);
        return endpoints;
    }

    /// <summary>`GET /api/admin/me` — lets the portal decide whether to show the Admin entry. 200
    /// <c>{ isPlatformAdmin }</c> for any signed-in caller; 401 when anonymous.</summary>
    private static IResult GetStatus(ICallerIdentity callerIdentity, PlatformAdminGuard guard) =>
        callerIdentity.Resolve() is null
            ? Results.Unauthorized()
            : Results.Ok(new { isPlatformAdmin = guard.IsPlatformAdmin() });

    private static async Task<IResult> CreateCustomerWorkspaceAsync(
        CreateCustomerWorkspaceRequest request,
        ICallerIdentity callerIdentity,
        PlatformAdminGuard guard,
        WorkspaceProvisioningService provisioningService,
        WorkspaceInvitationService invitationService,
        CancellationToken cancellationToken)
    {
        var identity = callerIdentity.Resolve();
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        if (!guard.IsPlatformAdmin())
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.CustomerEmail))
        {
            return Results.BadRequest("A 'customerEmail' is required.");
        }

        var email = callerIdentity.ResolveEmail();
        if (string.IsNullOrWhiteSpace(email))
        {
            return Results.BadRequest(
                "Your sign-in token carries no email address, so a workspace cannot be created for it. " +
                "Sign out and sign in again.");
        }

        var created = await provisioningService
            .CreateWorkspaceAsync(request.Name, email, request.Industry, request.Country, callerObjectId: identity, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (created.IsFailure)
        {
            return Results.BadRequest(created.Error);
        }

        var workspace = created.Value;
        var issued = await invitationService
            .IssueAsync(workspace.TenantId, request.CustomerEmail, WorkspaceRoleName.Admin, identity, cancellationToken)
            .ConfigureAwait(false);

        // The workspace exists whatever happens to the invitation, so the response always names it;
        // `invitation` is null and `invitationError` says why when the offer could not be issued
        // (the portal then offers to invite again against the same workspace via the normal members
        // screen). No half-state is hidden: the caller sees exactly what was and was not created.
        var invitation = issued.IsSuccess
            ? new
            {
                id = issued.Invitation!.Id.Value,
                email = issued.Invitation.Email,
                role = nameof(WorkspaceRoleName.Admin),
                expiresAt = issued.Invitation.ExpiresAt,
                acceptUrl = issued.AcceptUrl,
                deliveryOutcome = issued.DeliveryOutcome.ToWireValue(),
                identityProvisioned = issued.IdentityProvisioned,
            }
            : null;

        var invitationError = issued.IsSuccess
            ? null
            : issued.ProvisioningFailureReason is { } reason
                ? $"The customer's sign-in identity could not be prepared ({reason.ToWireValue()})."
                : issued.Error ?? "The invitation could not be issued.";

        return Results.Created($"/api/workspaces/{workspace.TenantId.Value}", new
        {
            workspace = new
            {
                id = workspace.TenantId.Value,
                name = workspace.Name,
                createdAt = workspace.CreatedAt,
            },
            invitation,
            invitationError,
        });
    }
}

/// <summary>Body of `POST /api/admin/workspaces`: the customer's workspace profile plus the address
/// of the person who becomes its first Admin.</summary>
public sealed record CreateCustomerWorkspaceRequest(
    string Name, string CustomerEmail, string? Industry = null, string? Country = null);
