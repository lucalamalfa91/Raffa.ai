using System.Globalization;
using Azure;
using Azure.Communication.Email;
using Raffa.Identity.Workspace.Application;
using Raffa.Identity.Workspace.Domain;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// The real <see cref="IInvitationMailer"/> (task E17/F01/US01/T01, wave w15, NW-68; ADR-026 w15
/// footer §5, ADR-025 §J.6, ADR-020 w15 §4 "surface 12"): Azure Communication Services Email over
/// the managed domain Terraform provisions (<c>infra/modules/communication</c>), sending from
/// <c>Invitations__Mail__SenderAddress</c>. Registered by <c>Program.cs</c> ahead of
/// <c>AddIdentityWorkspaceModule</c> only while <c>Invitations__Mail__Enabled</c> is true, so the
/// module's <c>TryAdd</c> <see cref="Raffa.Identity.Workspace.Infrastructure.NullInvitationMailer"/>
/// default never wins over it. A host adapter, not a module member: the ACS SDK is a provider SDK
/// (ADR-002), exactly as <c>Raffa.Storage</c> holds the blob SDK.
///
/// <para>
/// <b>The mail, verbatim from ADR-020 w15 §4.</b> Plain text is the body of record — one column, no
/// image, no logo, no web font, no external stylesheet, no HTML part. It carries the workspace
/// name, the offered role and the expiry and nothing else (no counts, no roster, no supplier names:
/// a mail can be forwarded by anyone, forever); its two middle lines are screen 11 state 2 verbatim
/// so the mail and the landing page say the same thing; the one-time-code line is what turns
/// Microsoft's unexpected second message into an expected step; and it never claims an account was
/// created. The accept link is a plain visible absolute URL. One recipient, no CC/BCC, no Reply-To
/// (§J.6d).
/// </para>
/// <para>
/// <b><c>true</c> means accepted for delivery, never delivered</b> (§J.6e) — the bool is the
/// invite's own <c>mailDelivered</c>. A managed domain sends from <c>…azurecomm.net</c>, which
/// corporate filters treat harshly, so <see langword="false"/> is a likely branch and the pane's
/// "could not be sent" state with the copyable link must stay fully functional. The log carries the
/// ACS operation id and a named outcome; never the recipient, the body, the subject or the link
/// (§J.7a/§J.7b).
/// </para>
/// </summary>
internal sealed class AcsInvitationMailer(
    EmailClient client,
    InvitationHostOptions options,
    ILogger<AcsInvitationMailer> logger) : IInvitationMailer
{
    public bool IsConfigured => true;

    public async Task<bool> TrySendAsync(
        string email,
        string workspaceName,
        WorkspaceRoleName role,
        string acceptUrl,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        // ADR-026 w15 footer §4: a site-relative link is unusable in a mail body. Startup already
        // refuses the "enabled with no base" pair (InvitationHostOptions.ValidateOrThrow); this is
        // the honest answer if the composed link is somehow still relative -- refuse, return false,
        // and let the pane show the copyable link.
        if (!Uri.TryCreate(acceptUrl, UriKind.Absolute, out var acceptUri) || !string.Equals(acceptUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogWarning("Invitation mail not sent for workspace '{WorkspaceName}': the accept link is not an absolute https URL", workspaceName);
            return false;
        }

        if (string.IsNullOrWhiteSpace(options.Mail.SenderAddress))
        {
            logger.LogWarning("Invitation mail not sent for workspace '{WorkspaceName}': no sender address is configured", workspaceName);
            return false;
        }

        var content = new EmailContent(ComposeSubject(workspaceName))
        {
            PlainText = ComposeBody(workspaceName, role, acceptUrl, expiresAt),
        };
        var message = new EmailMessage(options.Mail.SenderAddress, email, content);

        try
        {
            // WaitUntil.Started: "accepted for delivery" is the fact this bool reports; waiting for
            // completion would block the invite request on a mail pipeline it cannot influence.
            var operation = await client.SendAsync(WaitUntil.Started, message, cancellationToken).ConfigureAwait(false);
            logger.LogInformation(
                "Invitation mail accepted for delivery for workspace '{WorkspaceName}' (ACS operation {OperationId})",
                workspaceName, operation.Id);
            return true;
        }
        catch (RequestFailedException exception)
        {
            logger.LogWarning(
                "Invitation mail refused by ACS for workspace '{WorkspaceName}': status {Status}, error code {ErrorCode}",
                workspaceName, exception.Status, exception.ErrorCode ?? "-");
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Invitation mail could not be sent for workspace '{WorkspaceName}'", workspaceName);
            return false;
        }
    }

    /// <summary>ADR-020 w15 §4: the subject carries the product's own name, "Raffa.ai".</summary>
    public static string ComposeSubject(string workspaceName) => $"You've been invited to {workspaceName} on Raffa.ai";

    /// <summary>ADR-020 w15 §4, in order and nothing else: the heading, the role line (screen 11 state 2
    /// verbatim), the link as a visible absolute URL, the one-time-code line (screen 11 state 2
    /// verbatim, second person), the expiry, the ignore line.</summary>
    public static string ComposeBody(string workspaceName, WorkspaceRoleName role, string acceptUrl, DateTimeOffset expiresAt)
    {
        var expiry = expiresAt.UtcDateTime.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return string.Join(
            "\r\n\r\n",
            $"Join {workspaceName}",
            $"You have been invited as {RoleLabel(role)}.",
            $"Open your invitation: {acceptUrl}",
            "Signing in uses your email address — Microsoft will send you a one-time code.",
            $"This link expires {expiry} and can be used once.",
            "If you were not expecting this, you can ignore this message.");
    }

    /// <summary>The same labels screen 11 renders (`workspaceRoleLabel`): "Workspace Admin" for Admin, the role's own name otherwise.</summary>
    private static string RoleLabel(WorkspaceRoleName role) => role == WorkspaceRoleName.Admin ? "Workspace Admin" : role.ToString();
}
