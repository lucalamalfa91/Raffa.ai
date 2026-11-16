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
    public async Task<bool> TrySendProspectAsync(
        string email,
        string acceptUrl,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(acceptUrl, UriKind.Absolute, out var acceptUri)
            || !string.Equals(acceptUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(options.Mail.SenderAddress))
        {
            logger.LogWarning("Prospect invitation mail not sent: the accept link is not an absolute https URL or no sender address is configured");
            return false;
        }

        var content = new EmailContent(ComposeProspectSubject())
        {
            PlainText = ComposeProspectBody(acceptUrl, expiresAt),
        };
        var logo = Logo();
        if (logo is not null)
        {
            content.Html = ComposeProspectHtml(acceptUrl, expiresAt, "cid:" + LogoContentId);
        }
        else
        {
            content.Html = ComposeProspectHtml(acceptUrl, expiresAt);
        }

        var message = new EmailMessage(options.Mail.SenderAddress, email, content);
        if (logo is not null)
        {
            message.Attachments.Add(new EmailAttachment("raffa-logo.png", "image/png", BinaryData.FromBytes(logo)) { ContentId = LogoContentId });
        }

        try
        {
            var operation = await client
                .SendAsync(WaitUntil.Started, message, cancellationToken)
                .ConfigureAwait(false);
            logger.LogInformation("Prospect invitation mail accepted for delivery (ACS operation {OperationId})", operation.Id);
            return true;
        }
        catch (RequestFailedException exception)
        {
            logger.LogWarning("Prospect invitation mail refused by ACS: status {Status}, error code {ErrorCode}", exception.Status, exception.ErrorCode ?? "-");
            return false;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Prospect invitation mail could not be sent");
            return false;
        }
    }

    /// <summary>Content id of the inline logo (<c>cid:raffa-logo</c>).</summary>
    internal const string LogoContentId = "raffa-logo";

    private static byte[]? logoBytes;

    /// <summary>The logo PNG embedded in the assembly, or null when it is missing.</summary>
    internal static byte[]? Logo()
    {
        if (logoBytes is not null)
        {
            return logoBytes;
        }

        using var stream = typeof(AcsInvitationMailer).Assembly.GetManifestResourceStream("Raffa.Api.Resources.raffa-logo.png");
        if (stream is null)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return logoBytes = buffer.ToArray();
    }

    /// <summary>
    /// The invitation as a designed, table-based HTML mail (inline styles only; every variable part is HTML-encoded):
    /// the logo, a large friendly headline, a short warm line about what Raffa.ai does, one big button, three
    /// reasons to try it, and an honest footer saying who sends it and why. The logo is attached inline
    /// (<c>cid:raffa-logo</c>) so it shows even where remote images are blocked; <paramref name="logoSource"/> is
    /// that cid, or an absolute URL when the mail is composed without the attachment.
    /// </summary>
    public static string ComposeProspectHtml(string acceptUrl, DateTimeOffset expiresAt, string? logoSource = null)
    {
        var expiry = expiresAt.UtcDateTime.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        var link = System.Net.WebUtility.HtmlEncode(acceptUrl);
        var logo = logoSource ?? (Uri.TryCreate(acceptUrl, UriKind.Absolute, out var uri) ? $"{uri.Scheme}://{uri.Authority}/brand/raffa-logo.png" : string.Empty);
        var logoBlock = logo.Length == 0
            ? "<span style=\"font-size:24px;font-weight:bold;color:#201e1d\">Raffa.ai</span>"
            : $"<img src=\"{System.Net.WebUtility.HtmlEncode(logo)}\" width=\"150\" height=\"30\" alt=\"Raffa.ai\" style=\"display:block;border:0;outline:none;height:30px;width:150px\">";
        const string point = "padding:0 0 18px 0;font-size:15px;line-height:22px;color:#444444";
        return $"""
            <!doctype html>
            <html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><meta name="color-scheme" content="light"><title>Your invitation to Raffa.ai</title></head>
            <body style="margin:0;padding:0;background:#f3f2f2;font-family:Arial,Helvetica,sans-serif;color:#201e1d">
            <div style="display:none;max-height:0;overflow:hidden;opacity:0;font-size:1px;line-height:1px">Raffa.ai reads your supplier contracts and tells you what renews, and when to act. Your invitation works once and expires {expiry}.</div>
            <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f3f2f2"><tr><td align="center" style="padding:24px 12px">
            <table role="presentation" width="560" cellpadding="0" cellspacing="0" style="width:100%;max-width:560px;background:#ffffff">
            <tr><td style="padding:28px 40px 20px 40px">{logoBlock}</td></tr>
            <tr><td style="padding:0 40px"><div style="height:3px;background:#ec3013;line-height:3px;font-size:3px">&nbsp;</div></td></tr>
            <tr><td style="padding:36px 40px 8px 40px;font-size:32px;line-height:38px;font-weight:bold;color:#201e1d">Never miss a contract renewal again.</td></tr>
            <tr><td style="padding:12px 40px 8px 40px;font-size:17px;line-height:26px;color:#444444">Hello, you are invited to try Raffa.ai. Upload your supplier contracts and Raffa tells you what renews, when you have to act, and where you can save.</td></tr>
            <tr><td style="padding:24px 40px 12px 40px"><a href="{link}" style="display:inline-block;background:#ee2d12;color:#ffffff;text-decoration:none;font-weight:bold;font-size:17px;line-height:20px;padding:16px 32px">Create my workspace</a></td></tr>
            <tr><td style="padding:0 40px 28px 40px;font-size:13px;line-height:19px;color:#6a6666">Takes about two minutes. You sign in with this email address — Microsoft sends you a one-time code, no new password.</td></tr>
            <tr><td style="padding:8px 40px 0 40px;font-size:13px;letter-spacing:1px;font-weight:bold;color:#ec3013">WHY TRY IT</td></tr>
            <tr><td style="padding:12px 40px 0 40px"><table role="presentation" width="100%" cellpadding="0" cellspacing="0">
            <tr><td style="{point}"><strong style="color:#201e1d">Know every deadline.</strong> Raffa reads your contracts and flags notice dates before they pass.</td></tr>
            <tr><td style="{point}"><strong style="color:#201e1d">Negotiate with facts.</strong> See what you pay today and where a renewal could cost less.</td></tr>
            <tr><td style="{point}"><strong style="color:#201e1d">Stay in control.</strong> Your documents stay in your own workspace, and nothing is sent to a supplier without your approval.</td></tr>
            </table></td></tr>
            <tr><td style="padding:12px 40px 28px 40px;font-size:12px;line-height:18px;color:#6a6666">The button works once and the invitation expires on {expiry}. If it does not open, paste this address into your browser:<br><span style="word-break:break-all">{link}</span></td></tr>
            <tr><td style="padding:20px 40px 28px 40px;border-top:1px solid #dddddd;font-size:12px;line-height:18px;color:#6a6666">You are receiving this single message because an administrator of Raffa.ai entered your address to invite you. If you do not know Raffa.ai, ignore it: nothing happens unless you open the link, and you will not be contacted again.<br><br>Raffa.ai · askraffa.com</td></tr>
            </table></td></tr></table></body></html>
            """;
    }

    public static string ComposeProspectSubject() => "Your invitation to Raffa.ai";

    public static string ComposeProspectBody(string acceptUrl, DateTimeOffset expiresAt)
    {
        var expiry = expiresAt.UtcDateTime.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return string.Join(
            "\r\n\r\n",
            "Never miss a contract renewal again.",
            "Hello, you are invited to try Raffa.ai. Upload your supplier contracts and Raffa tells you what renews, when you have to act, and where you can save.",
            $"Create my workspace: {acceptUrl}",
            "Takes about two minutes. You sign in with this email address — Microsoft sends you a one-time code, no new password.",
            "- Know every deadline: Raffa flags notice dates before they pass.\r\n- Negotiate with facts: see what you pay today and where a renewal could cost less.\r\n- Stay in control: nothing is sent to a supplier without your approval.",
            $"The link works once and the invitation expires on {expiry}.",
            "You are receiving this single message because an administrator of Raffa.ai entered your address to invite you. If you do not know Raffa.ai, ignore it: nothing happens unless you open the link, and you will not be contacted again.",
            "Raffa.ai · askraffa.com");
    }

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
