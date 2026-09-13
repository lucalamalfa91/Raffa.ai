using Raffa.Identity.Workspace.Domain;

namespace Raffa.Identity.Workspace.Application;

/// <summary>
/// The mailer seam (task E15/F01/US01/T01, wave w14; ADR-026 §D6): keeps the "is a real email
/// transport wired up" question out of the invitation lifecycle's critical path. OQ-w14-002 answers
/// at the table that no transport ships in w14 — <see cref="Infrastructure.NullInvitationMailer"/>
/// is the only implementation registered this wave, and <see cref="TrySendAsync"/>'s
/// <see langword="bool"/> result becomes the invite response's own <c>mailDelivered</c> field, so
/// "must not say sent unless it left" is a server fact in code rather than a convention
/// (<see cref="Infrastructure.WorkspaceInvitationService.IssueAsync"/> is the one caller). A future
/// transport (ACS Email + Azure Managed Domain, per ADR-005's w14 footer) is a new DI registration
/// plus a configuration binding — not a redesign of this interface or of anything upstream of it.
/// </summary>
public interface IInvitationMailer
{
    /// <summary>
    /// Attempts to deliver the invitation. Returns <see langword="false"/> — never throws — when no
    /// transport is configured or delivery fails; the caller treats that as an honest "not sent",
    /// never an error. <paramref name="acceptUrl"/> is the already-built, site-relative
    /// <c>/invite/accept#&lt;token&gt;</c> link (ADR-025 Rule C9) — the mailer is the one place that
    /// may resolve it against an absolute origin, since it has no browser to resolve a relative path
    /// against; the invitation itself carries no absolute-URL configuration key (w14's zero
    /// Azure/Terraform delta).
    /// </summary>
    Task<bool> TrySendAsync(
        string email,
        string workspaceName,
        WorkspaceRoleName role,
        string acceptUrl,
        CancellationToken cancellationToken = default);
}
