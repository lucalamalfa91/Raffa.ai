namespace Raffa.Identity.Workspace.Application;

/// <summary>
/// The guest-provisioning seam (task E17/F01/US01/T01, wave w15; ADR-026 w15 footer §2, ADR-025
/// §J): exactly <see cref="IInvitationMailer"/>'s shape, in the same folder, for the same reason —
/// the wave lands whole whether or not the directory permission ships. The invite handler calls
/// <see cref="EnsureGuestAsync"/> <b>before</b> any row is written (ADR-025 §J.2a: guest first,
/// invitation row second — an inert orphan guest beats a live 256-bit token for an identity that
/// cannot sign in). The only implementation that talks to a directory is the host's
/// <c>Raffa.Api.Infrastructure.GraphGuestProvisioner</c>, the one and only Microsoft Graph call site
/// (ADR-002: a provider SDK lives in a host, never in this module); this module ships
/// <see cref="Infrastructure.NullGuestProvisioner"/>, whose answer is
/// <see cref="GuestProvisioningStatus.NotConfigured"/> and under which an invite behaves exactly as
/// it did before this wave — link-only, no directory write.
/// </summary>
public interface IGuestProvisioner
{
    /// <summary>
    /// Makes sure <paramref name="email"/> can sign in to this directory as a B2B guest. Never
    /// throws: a directory that refuses, is unreachable or is not consented comes back as
    /// <see cref="GuestProvisioningStatus.Failed"/> with a reason from the closed set
    /// (<see cref="GuestProvisioningFailureReason"/>) — the raw error body is never surfaced
    /// (ADR-025 §J.4b). <paramref name="workspaceName"/> is display context only; the invited
    /// address comes from the authenticated Admin's request body and nowhere else (§J.1c.3).
    /// </summary>
    Task<GuestProvisioningResult> EnsureGuestAsync(string email, string workspaceName, CancellationToken cancellationToken = default);
}

public enum GuestProvisioningStatus
{
    /// <summary>A guest object was created for the address.</summary>
    Provisioned,

    /// <summary>The address already had a guest object; nothing was created. Externally
    /// indistinguishable from <see cref="Provisioned"/> (ADR-025 §J.4a: the invite form must never
    /// become a directory-enumeration oracle).</summary>
    AlreadyPresent,

    /// <summary>No directory write is configured (<c>Invitations__GuestProvisioning__Enabled</c>
    /// is false): the invite is link-only, as it was before wave w15.</summary>
    NotConfigured,

    /// <summary>The directory refused or could not be reached; see <see cref="GuestProvisioningResult.FailureReason"/>.
    /// The invitation is aborted — no row, no token, no mail (ADR-026 w15 footer §3).</summary>
    Failed,
}

/// <summary>ADR-026 w15 footer §3's closed reason set — the wire values are the pane's copy keys
/// (ADR-020 w15 §3.5), so a value outside this set never reaches a screen.</summary>
public enum GuestProvisioningFailureReason
{
    /// <summary>The directory answered 401/403: the application permission (<c>User.Invite.All</c>)
    /// has not been consented, or a directory policy forbids invitations (ADR-025 §J.1b).</summary>
    ConsentMissing,

    /// <summary>The directory answered with a client error for this request — an address it will
    /// not add, a malformed request, a refused domain.</summary>
    ProvisioningFailed,

    /// <summary>The directory could not be reached, timed out, or failed on its side; the
    /// workload identity could not obtain a token.</summary>
    DirectoryUnavailable,
}

public static class GuestProvisioningFailureReasonExtensions
{
    /// <summary>The `snake_case` wire vocabulary the 502 body carries (ADR-026 w15 footer §3/§8).</summary>
    public static string ToWireValue(this GuestProvisioningFailureReason reason) => reason switch
    {
        GuestProvisioningFailureReason.ConsentMissing => "consent_missing",
        GuestProvisioningFailureReason.ProvisioningFailed => "provisioning_failed",
        GuestProvisioningFailureReason.DirectoryUnavailable => "directory_unavailable",
        _ => "provisioning_failed",
    };
}

/// <summary>
/// The outcome of <see cref="IGuestProvisioner.EnsureGuestAsync"/>. <see cref="GuestObjectId"/> is
/// the directory's object id for the guest — the <c>oid</c> that person's token will carry — and
/// is bound into <c>workspace_user.external_subject_id</c> at invite time (ADR-025 §J.3b), so the
/// accept matches on <c>oid</c> exactly and the mangled <c>#EXT#</c> UPN never enters an
/// authorization decision (ADR-010 w15 §2.3). <see cref="CorrelationId"/> is the directory's own
/// opaque request id, the one thing about a failure that may be logged and audited (§J.4b).
/// </summary>
public sealed record GuestProvisioningResult(
    GuestProvisioningStatus Status,
    string? GuestObjectId,
    GuestProvisioningFailureReason? FailureReason,
    string? CorrelationId)
{
    /// <summary>The 201's <c>identityProvisioned</c> (ADR-026 w15 footer §1): true for both
    /// <see cref="GuestProvisioningStatus.Provisioned"/> and <see cref="GuestProvisioningStatus.AlreadyPresent"/>,
    /// false when provisioning is not configured. A failure never reaches a 201 at all.</summary>
    public bool IdentityProvisioned => Status is GuestProvisioningStatus.Provisioned or GuestProvisioningStatus.AlreadyPresent;

    public static GuestProvisioningResult Provisioned(string guestObjectId) =>
        new(GuestProvisioningStatus.Provisioned, guestObjectId, null, null);

    public static GuestProvisioningResult AlreadyPresent(string guestObjectId) =>
        new(GuestProvisioningStatus.AlreadyPresent, guestObjectId, null, null);

    public static GuestProvisioningResult NotConfigured() =>
        new(GuestProvisioningStatus.NotConfigured, null, null, null);

    public static GuestProvisioningResult Failed(GuestProvisioningFailureReason reason, string? correlationId = null) =>
        new(GuestProvisioningStatus.Failed, null, reason, correlationId);
}
