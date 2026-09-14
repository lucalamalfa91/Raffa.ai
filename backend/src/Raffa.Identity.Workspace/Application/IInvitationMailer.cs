using Raffa.Identity.Workspace.Domain;

namespace Raffa.Identity.Workspace.Application;

/// <summary>
/// The mailer seam (task E15/F01/US01/T01, wave w14; ADR-026 §D6): keeps the "is a real email
/// transport wired up" question out of the invitation lifecycle's critical path.
/// <see cref="Infrastructure.NullInvitationMailer"/> is this module's own provider-SDK-free default
/// (no transport); task E17/F01/US01/T01 (wave w15, NW-68) adds the real transport as the host's
/// <c>Raffa.Api.Infrastructure.AcsInvitationMailer</c>, registered <em>before</em>
/// <c>AddIdentityWorkspaceModule</c> so its <c>TryAdd</c> default never wins (ADR-026 w15 footer §5).
/// <see cref="TrySendAsync"/>'s <see langword="bool"/> result becomes the invite response's own
/// <c>mailDelivered</c> — <em>accepted for delivery</em>, never a receipt (ADR-025 §J.6e) — and,
/// together with <see cref="IsConfigured"/>, the 201's <c>deliveryOutcome</c>
/// (<c>sent</c> | <c>mail_failed</c> | <c>no_transport</c>), computed in one place from one call
/// (<see cref="Infrastructure.WorkspaceInvitationService.IssueAsync"/>) so the two can never
/// diverge (ADR-026 w15 footer §8).
/// </summary>
public interface IInvitationMailer
{
    /// <summary>Whether a real transport is behind this mailer. <see langword="false"/> is the
    /// <c>no_transport</c> outcome: the copyable link is the only channel and nothing is attempted.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Attempts to deliver the invitation. Returns <see langword="false"/> — never throws — when no
    /// transport is configured or delivery was not accepted; the caller treats that as an honest
    /// "not sent", never an error. <paramref name="acceptUrl"/> is the already-composed accept link:
    /// absolute (<c>{Invitations__AcceptUrlBase}/invite/accept#&lt;token&gt;</c>) when a base is
    /// configured, site-relative otherwise — in which case a real mailer must refuse rather than
    /// mail a link nobody can open (ADR-026 w15 footer §4). The token stays after the <c>#</c>
    /// (ADR-025 Rule C9/§J.6c) and is never logged, audited or stored by any implementation.
    /// </summary>
    Task<bool> TrySendAsync(
        string email,
        string workspaceName,
        WorkspaceRoleName role,
        string acceptUrl,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default);
}
