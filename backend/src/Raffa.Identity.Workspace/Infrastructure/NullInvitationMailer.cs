using Raffa.Identity.Workspace.Application;
using Raffa.Identity.Workspace.Domain;
using Microsoft.Extensions.Logging;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>
/// The default <see cref="IInvitationMailer"/> (task E15/F01/US01/T01, wave w14; ADR-026 §D6;
/// OQ-w14-002 "answered at the table: deferred"). Always returns <see langword="false"/> and logs —
/// never throws — so the invite endpoint's <c>mailDelivered</c> is an honest <see langword="false"/>
/// and the Members UI shows the copyable <c>acceptUrl</c> instead of claiming a mail was sent
/// (NW-58's own "must not #1"). No new Azure resource, no Terraform change, no new configuration key:
/// this type has no constructor dependency beyond <see cref="ILogger{TCategoryName}"/>, which every
/// host already provides.
/// </summary>
public sealed class NullInvitationMailer(ILogger<NullInvitationMailer> logger) : IInvitationMailer
{
    public Task<bool> TrySendAsync(
        string email,
        string workspaceName,
        WorkspaceRoleName role,
        string acceptUrl,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Invitation mail not sent (no transport configured in w14, OQ-w14-002): " +
            "{Email} invited to workspace '{WorkspaceName}' as {Role}. Accept link: {AcceptUrl}",
            email, workspaceName, role, acceptUrl);

        return Task.FromResult(false);
    }
}
