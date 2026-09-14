namespace Raffa.Identity.Workspace.Application;

/// <summary>
/// The invitation policy the host hands this module (task E17/F01/US01/T01, wave w15). Bound by the
/// host from its own <c>Invitations</c> section — never <c>IOptions&lt;T&gt;</c> (absent from
/// <c>backend/src</c>) — and registered as a singleton <em>before</em> <c>AddIdentityWorkspaceModule</c>,
/// whose <c>TryAdd</c> default is this type's parameterless shape: no absolute base (the w14
/// site-relative link) and the default cap.
/// </summary>
public sealed class InvitationOptions
{
    /// <summary>ADR-025 Rule C9: a URL <b>fragment</b>, never a path or query string.</summary>
    public const string AcceptRoutePrefix = "/invite/accept#";

    /// <summary>ADR-025 §J.1c.4: the per-tenant cap on live (unaccepted, unrevoked) invitations,
    /// bounding <em>distinct</em> addresses — the directory-spam shape.</summary>
    public const int DefaultLiveInvitationCap = 100;

    /// <summary>
    /// <c>Invitations__AcceptUrlBase</c>: <c>https://</c> + this environment's own SPA host, supplied
    /// by Terraform and never derived from a request header (ADR-025 §J.6a — a host-header-injected
    /// base would mail a live token to an attacker-controlled origin). <see langword="null"/> keeps the
    /// w14 site-relative link (ADR-026 w15 footer §4).
    /// </summary>
    public string? AcceptUrlBase { get; set; }

    public int LiveInvitationCap { get; set; } = DefaultLiveInvitationCap;

    /// <summary><c>{base}/invite/accept#{token}</c> with a base, the w14 site-relative form without.
    /// The origin is the only thing the base changes: the token stays after the <c>#</c>.</summary>
    public string ComposeAcceptUrl(string token) =>
        IsUsableBase(AcceptUrlBase)
            ? AcceptUrlBase!.TrimEnd('/') + AcceptRoutePrefix + token
            : AcceptRoutePrefix + token;

    /// <summary>A usable base is an absolute <c>https://</c> origin with no query and no fragment.</summary>
    public static bool IsUsableBase(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrEmpty(uri.Query)
        && string.IsNullOrEmpty(uri.Fragment);
}
