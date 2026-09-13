using Raffa.Identity.Workspace.Domain;
using Raffa.Identity.Workspace.Infrastructure;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// Task E14/F02/US01/T01 (wave w14 "workspace is real"; ADR-025 §A "the identity seam", answers
/// OQ-w14-001): the <b>one</b> resolution point that returns the caller's interim identity. Every
/// endpoint added or changed in this wave consumes this seam instead of reading
/// <c>HttpRequest.Headers</c> directly — <see cref="WorkspaceEndpointExtensions"/>'s create handler
/// and <see cref="WorkspaceInvitesEndpointExtensions"/>'s invite guard are this task's own two
/// callers. ADR-025 Rule A1: "After the wave, Grep for 'X-User-Id' in backend/src must find it in
/// one file besides the document/conversation readers NW-05/NW-32 retire" — this is that file.
///
/// <para>
/// ADR-025 Rule A2: the identity is trusted for <b>one thing only</b> — which membership rows to
/// look up. It confers no role, no tenant and no scope. Rule A3 (retirement): W15 (NW-05) replaces
/// the header with the validated token subject by editing only <see cref="HeaderCallerIdentity"/>'s
/// body — no caller of <see cref="ICallerIdentity"/> changes.
/// </para>
///
/// <para>
/// This seam is deliberately narrower than the SharedKernel Tenancy identity-scope type
/// <c>E14/F01/US01/T01</c> adds in this same phase (the one whose GUC-backed scope is opened for
/// the first time in phase 2, by <c>E14/F03/US01/T01</c>'s workspace discovery — the wave's first
/// reader of <c>workspace_user</c> outside a tenant scope): naming that sibling type here would
/// leave this branch uncompilable until it merges, the e13 union-merge defect verbatim. Nothing in
/// phase 1 needs it — bootstrap writes inside the tenant scope it already opens, and the invite
/// guard resolves membership in the <b>route</b> tenant through
/// <see cref="WorkspaceRoleResolver"/>, which opens its own tenant scope.
/// </para>
/// </summary>
internal interface ICallerIdentity
{
    /// <summary>
    /// The caller's identity for the current request, or <see langword="null"/> when none was
    /// presented (or the header was present but blank) — the endpoint maps that to <b>401</b>
    /// (ADR-025 §B), never 400: absence of identity is an authentication failure.
    /// </summary>
    string? Resolve();
}

/// <summary>
/// The interim <see cref="ICallerIdentity"/> (ADR-022 / OQ-askv2-005 / OQ-w14-001): the MSAL account
/// username the web already sends as <c>X-User-Id</c> on every call (<c>client.ts:49-54</c>), off
/// <see cref="IHttpContextAccessor"/> rather than a bound <c>HttpContext</c> parameter so the same
/// seam can be injected into a handler that needs other request-scoped services too — the same shape
/// <see cref="WorkspaceProvisioningService"/>/<see cref="WorkspaceMembershipService"/> are already
/// injected with. Trims and lower-cases the header value: <see cref="WorkspaceRoleResolver"/>'s own
/// membership join matches identities by plain string equality, and
/// <see cref="WorkspaceMembershipFactory.CreateInvitedUser"/> (which this seam's own bootstrap
/// caller feeds straight through to) only trims, never lower-cases — so normalizing casing here, the
/// one seam every write and every read shares, is what makes a mixed-case header still find the row
/// a lower-cased creator identity wrote. W15 (NW-05) retires the header by rewriting this
/// class's body alone to read the validated token's <c>sub</c>/<c>oid</c> claim instead — every
/// caller of <see cref="ICallerIdentity"/> is unaffected (ADR-025 Rule A3).
/// </summary>
internal sealed class HeaderCallerIdentity(IHttpContextAccessor httpContextAccessor) : ICallerIdentity
{
    public const string UserIdHeaderName = "X-User-Id";

    public string? Resolve()
    {
        var request = httpContextAccessor.HttpContext?.Request;
        if (request is null || !request.Headers.TryGetValue(UserIdHeaderName, out var values))
        {
            return null;
        }

        var trimmed = values.ToString().Trim();
        return trimmed.Length == 0 ? null : trimmed.ToLowerInvariant();
    }
}
