using System.Security.Claims;
using Microsoft.Identity.Web;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// Task E14/F02/US01/T01 (wave w14 "workspace is real"; ADR-025 §A "the identity seam", answers
/// OQ-w14-001), retired to the validated token by task E18/F01/US01/T01 (wave w15, NW-05; ADR-010
/// w15 footer §2.1/S15-6; ADR-022 w15 footer clause 1): the <b>one</b> resolution point that returns
/// the caller's identity. Every tenant-scoped endpoint consumes this seam (directly, or through
/// <see cref="CallerContext"/>) instead of reading <c>HttpContext.User</c>/request headers directly.
///
/// <para>
/// The identity is trusted for <b>one thing only</b> — which membership rows to look up. It confers
/// no role, no tenant and no scope of its own (ADR-010 w15 footer §1.5/S15-5: a present scope grants
/// nothing either). <see cref="Resolve"/> maps to <b>401</b> at every call site when it returns
/// <see langword="null"/> (ADR-025 §B), never 400: absence of identity is an authentication failure.
/// </para>
/// </summary>
internal interface ICallerIdentity
{
    /// <summary>
    /// The caller's identity for the current request, or <see langword="null"/> when none was
    /// presented — no bearer token, one that failed validation, or one with no <c>oid</c> claim
    /// (S15-1: an app-only token, which this API should never see). The endpoint maps that to
    /// <b>401</b> (ADR-025 §B), never 400: absence of identity is an authentication failure.
    /// </summary>
    string? Resolve();

    /// <summary>
    /// The token's own <c>email</c> claim, or <see langword="null"/> when the token carries none.
    /// Task E17/F01/US01/T01 (ADR-010 w15 footer §2.1/§2.3): a <b>binding aid, never the standing
    /// key</b> — read by the invitation accept alone, as the second step of its resolution order
    /// (the <c>oid</c> bound at invite → this claim → refuse), so that a guest whose <c>oid</c> was
    /// not bound at invite time can still be matched to the invited address without anyone parsing
    /// the mangled <c>#EXT#</c> UPN. Terraform requests the claim on the API's access tokens
    /// (<c>optional_claims</c>, S15-9); its absence degrades to a 403 and a re-invite, never a grant.
    /// </summary>
    string? ResolveEmail();
}

/// <summary>
/// The validated <see cref="ICallerIdentity"/> (ADR-010 w15 footer §2.1/S15-6; NW-05): the bearer
/// token's <c>oid</c> claim — the caller's Entra object id. Replaces the retired interim
/// header-reading implementation outright (ADR-022 w15 footer clause 1: "the header must stop being
/// read at all" — precedence is not enough, a header that is read and then overridden is one
/// refactor away from being read and honoured), off <see cref="IHttpContextAccessor"/> for the same
/// reason the retired implementation was: so the same seam can be injected into a handler that needs
/// other request-scoped services too — the same shape <see cref="WorkspaceProvisioningService"/>/
/// <see cref="WorkspaceMembershipService"/> are already injected with.
///
/// <para>
/// Uses <see cref="ClaimsPrincipalExtensions.GetObjectId"/> (Microsoft.Identity.Web) rather than a
/// hand-rolled <c>FindFirst("oid")</c>: it checks both the bare <c>oid</c> claim type and the long-
/// form <c>http://schemas.microsoft.com/identity/claims/objectidentifier</c> URI JwtBearer's own
/// inbound-claim mapping can rewrite it to, so this seam does not silently break if that mapping
/// setting ever changes.
/// </para>
///
/// <para>
/// Deliberately <b>not</b> lower-cased the way the retired implementation lower-cased an email:
/// <c>oid</c> is an opaque, case-sensitive Entra object id, matched by ordinal equality everywhere
/// <see cref="Raffa.Identity.Workspace.Domain.WorkspaceUser.ExternalSubjectId"/> is compared
/// (<see cref="WorkspaceRoleResolver"/>, <see cref="CallerContext"/>,
/// <see cref="WorkspaceMembersEndpointExtensions"/>) — lower-casing it here would silently stop
/// matching every row already bound at invite time. ADR-010 w15 footer §2.1/S15-6: "the swap needs
/// no query change" is exactly this — no consumer of <see cref="ICallerIdentity"/> changes.
/// </para>
/// </summary>
internal sealed class TokenCallerIdentity(IHttpContextAccessor httpContextAccessor) : ICallerIdentity
{
    public string? Resolve()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null || user.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        var objectId = user.GetObjectId();
        return string.IsNullOrWhiteSpace(objectId) ? null : objectId;
    }

    public string? ResolveEmail()
    {
        var user = httpContextAccessor.HttpContext?.User;
        if (user is null || user.Identity is not { IsAuthenticated: true })
        {
            return null;
        }

        // The bare `email` claim (JwtBearer's inbound mapping is switched off in Program.cs, so
        // the token's own claim name survives) and its mapped URI form, in that order -- never
        // `preferred_username`/`upn`, which is the mangled #EXT# value for a B2B guest (S15-8).
        var email = user.FindFirst("email")?.Value ?? user.FindFirst(ClaimTypes.Email)?.Value;
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }
}
