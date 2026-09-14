using Raffa.Identity.Workspace.Application;
using Raffa.Identity.Workspace.Domain;
using Microsoft.Extensions.Logging;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>
/// The default <see cref="IInvitationMailer"/> (task E15/F01/US01/T01, wave w14; ADR-026 §D6): no
/// transport. Always returns <see langword="false"/> and logs — never throws — so the invite
/// endpoint's <c>mailDelivered</c> is an honest <see langword="false"/>, its <c>deliveryOutcome</c>
/// is <c>no_transport</c>, and the Members UI shows the copyable <c>acceptUrl</c> instead of claiming
/// a mail was sent. Since task E17/F01/US01/T01 (wave w15, NW-68) this is the default only: the host
/// registers <c>AcsInvitationMailer</c> ahead of this module when <c>Invitations__Mail__Enabled</c>
/// is true, and the mail is then a second channel for the link beside the 201 body.
/// </summary>
public sealed class NullInvitationMailer(ILogger<NullInvitationMailer> logger) : IInvitationMailer
{
    public bool IsConfigured => false;

    public Task<bool> TrySendAsync(
        string email,
        string workspaceName,
        WorkspaceRoleName role,
        string acceptUrl,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        // AC-10 / Rule C10 / ADR-025 §J.7b: the token -- and therefore acceptUrl, which embeds it
        // verbatim after the fragment -- appears in no audit row and no log sink, this one included;
        // and the application log is a cross-tenant sink, so the recipient address stays out of it
        // too. Both parameters stay part of the signature (a real mailer needs them to deliver) and
        // are deliberately never interpolated here.
        logger.LogInformation(
            "Invitation mail not sent: no transport configured (Invitations__Mail__Enabled is false). " +
            "Workspace '{WorkspaceName}', role {Role}; the accept link is in the caller's 201 response only.",
            workspaceName, role);

        return Task.FromResult(false);
    }
}
