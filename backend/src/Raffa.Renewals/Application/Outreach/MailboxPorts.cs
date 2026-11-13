using Raffa.Renewals.Application.CaseWork;

namespace Raffa.Renewals.Application.Outreach;

/// <summary>What a connected mailbox granted: the long-lived token and the address it sends from.</summary>
public sealed record MailboxGrant(string RefreshToken, string Address, string? DisplayName);

/// <summary>An email Raffa sends from a person's company mailbox.</summary>
public sealed record OutgoingMail(
    string FromAddress,
    string? FromName,
    IReadOnlyList<string> To,
    IReadOnlyList<string> Cc,
    IReadOnlyList<string> ReplyTo,
    string Subject,
    string Body,
    IReadOnlyList<CaseUpload> Attachments);

/// <summary>
/// A company mailbox provider (Microsoft 365 via Graph, Google via the Gmail API): the OAuth authorization
/// code flow with the send-only permission, and sending a message. Implemented in the host (HTTP adapters).
/// </summary>
public interface IMailboxProvider
{
    /// <summary>microsoft | google.</summary>
    string Name { get; }

    bool IsConfigured { get; }

    string AuthorizeUrl(string state, string redirectUri, string? loginHint);

    Task<MailboxGrant> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default);

    /// <summary>Sends <paramref name="mail"/>. Returns the rotated refresh token when the provider issued one.</summary>
    /// <exception cref="MailboxSendException">The provider refused (<see cref="MailboxSendException.Reconnect"/> when the grant is gone).</exception>
    Task<string?> SendAsync(string refreshToken, OutgoingMail mail, CancellationToken cancellationToken = default);
}

/// <summary>A send the mailbox provider refused.</summary>
public sealed class MailboxSendException(string message, bool reconnect) : Exception(message)
{
    /// <summary>The grant was revoked or expired: the person must connect the mailbox again.</summary>
    public bool Reconnect { get; } = reconnect;
}
