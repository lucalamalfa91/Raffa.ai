using Raffa.Api.Infrastructure;
using Raffa.Identity.Workspace.Infrastructure;

namespace Raffa.Api;

/// <summary>
/// Maps `GET /api/invites` and `POST /api/invites/accept` (task E15/F01/US01/T01, wave w14;
/// ADR-025 §D.1/§D.3, ADR-026 §D5). Neither route is parameterised on <c>{token}</c> — the token
/// travels in the <c>X-Invitation-Token</c> header, <b>never</b> a path or query string (ADR-025
/// Rule C9: query strings land in access logs, <c>Referer</c> headers and browser history), which
/// is exactly why these two routes carry no route value at all beyond the fixed path. All of the
/// actual token parsing/hashing/scoping/state-machine work is
/// <see cref="WorkspaceInvitationService"/>'s (<see cref="WorkspaceInvitationService.PreAcceptAsync"/>/
/// <see cref="WorkspaceInvitationService.AcceptAsync"/>); this file only maps HTTP concepts onto
/// that service's own <see cref="MembershipOperationStatus"/> outcomes, the same thin-composition
/// role every other endpoint file in this host already plays (ADR-002).
/// </summary>
public static class InvitationsEndpointExtensions
{
    private const string InvitationTokenHeaderName = "X-Invitation-Token";

    public static IEndpointRouteBuilder MapInvitationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/invites", GetInvitationAsync);
        endpoints.MapPost("/api/invites/accept", AcceptInvitationAsync);
        return endpoints;
    }

    /// <summary>
    /// Pre-accept (ADR-025 Rule D.3e): `200 { workspaceName, role, expiresAt }` and nothing else,
    /// ever — not the invited email (echoing it turns a leaked link into an address-discovery
    /// tool), no roster, no counts. A token holder is not yet a member. Expired → 410; unknown,
    /// revoked, accepted or malformed → 404 (one indistinguishable answer for every "not yours"
    /// case, §B).
    /// </summary>
    private static async Task<IResult> GetInvitationAsync(
        HttpRequest request,
        WorkspaceInvitationService invitationService,
        CancellationToken cancellationToken)
    {
        if (!TryResolveToken(request, out var token))
        {
            return Results.NotFound();
        }

        var result = await invitationService.PreAcceptAsync(token, cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            MembershipOperationStatus.Success => Results.Ok(new
            {
                workspaceName = result.WorkspaceName,
                role = result.Role!.Value.ToString(),
                expiresAt = result.ExpiresAt,
            }),
            MembershipOperationStatus.Expired => Results.StatusCode(StatusCodes.Status410Gone),
            _ => Results.NotFound(),
        };
    }

    /// <summary>
    /// Accept (ADR-025 Rule D.3a-d): requires a presented identity (401 without — there would
    /// otherwise be no subject to bind); `200 { workspaceId, workspaceName, role }` on success.
    /// Email mismatch → 403 (reason never echoes the invited address); expired → 410; second accept
    /// by the same identity → 409; unknown/revoked/malformed/accepted-by-someone-else → 404.
    /// </summary>
    private static async Task<IResult> AcceptInvitationAsync(
        HttpRequest request,
        ICallerIdentity callerIdentity,
        WorkspaceInvitationService invitationService,
        CancellationToken cancellationToken)
    {
        var identity = callerIdentity.Resolve();
        if (identity is null)
        {
            return Results.Unauthorized();
        }

        if (!TryResolveToken(request, out var token))
        {
            return Results.NotFound();
        }

        var result = await invitationService.AcceptAsync(token, identity, cancellationToken).ConfigureAwait(false);

        return result.Status switch
        {
            MembershipOperationStatus.Success => Results.Ok(new
            {
                workspaceId = result.WorkspaceId.Value,
                workspaceName = result.WorkspaceName,
                role = result.Role!.Value.ToString(),
            }),
            MembershipOperationStatus.Expired => Results.StatusCode(StatusCodes.Status410Gone),
            MembershipOperationStatus.Conflict => Results.Conflict(result.Error),
            MembershipOperationStatus.Forbidden => Results.StatusCode(StatusCodes.Status403Forbidden),
            _ => Results.NotFound(),
        };
    }

    /// <summary>
    /// ADR-025 Rule C9: the header, never a route or query parameter. A missing or blank header is
    /// treated identically to an unparsable token — both endpoints answer 404 either way, so there
    /// is nothing for a caller to distinguish by omitting it versus sending garbage.
    /// </summary>
    private static bool TryResolveToken(HttpRequest request, out string token)
    {
        token = string.Empty;
        if (!request.Headers.TryGetValue(InvitationTokenHeaderName, out var values))
        {
            return false;
        }

        var trimmed = values.ToString().Trim();
        if (trimmed.Length == 0)
        {
            return false;
        }

        token = trimmed;
        return true;
    }
}
