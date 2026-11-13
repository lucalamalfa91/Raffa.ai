using Raffa.Renewals.Application.CaseWork;

namespace Raffa.Renewals.Application.Outreach;

/// <summary>What a connected mailbox granted: the long-lived token, the address it sends from and the permissions.</summary>
public sealed record MailboxGrant(string RefreshToken, string Address, string? DisplayName, string? Scopes = null);

/// <summary>A short-lived access token, the rotated refresh token when the provider issued one, and the granted permissions.</summary>
public sealed record MailboxToken(string AccessToken, string? RotatedRefreshToken, string? Scopes);

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

/// <summary>What the provider says about a sent email: the conversation the supplier's replies will join.</summary>
public sealed record SentMail(string? ConversationId, string? InternetMessageId);

/// <summary>A message of a conversation in the person's mailbox (headers only).</summary>
public sealed record MailboxMessage(string Id, string InternetMessageId, string FromAddress, string FromName, string Subject, DateTimeOffset ReceivedAt);

/// <summary>A Teams channel the person belongs to.</summary>
public sealed record TeamsChannel(string TeamId, string TeamName, string ChannelId, string ChannelName);

/// <summary>
/// A company mailbox provider (Microsoft 365 via Graph; Google via the Gmail API, send only): the OAuth
/// authorization code flow, sending a message, reading the replies of the conversations Raffa started and,
/// for Microsoft, posting to a Teams channel. Implemented in the host (HTTP adapters).
/// </summary>
public interface IMailboxProvider
{
    /// <summary>microsoft | google.</summary>
    string Name { get; }

    bool IsConfigured { get; }

    string AuthorizeUrl(string state, string redirectUri, string? loginHint);

    Task<MailboxGrant> ExchangeCodeAsync(string code, string redirectUri, CancellationToken cancellationToken = default);

    /// <exception cref="MailboxSendException">The grant is gone (<see cref="MailboxSendException.Reconnect"/>).</exception>
    Task<MailboxToken> AccessTokenAsync(string refreshToken, CancellationToken cancellationToken = default);

    /// <summary>Sends <paramref name="mail"/> from the person's mailbox (it stays in their Sent folder).</summary>
    /// <exception cref="MailboxSendException">The provider refused.</exception>
    Task<SentMail> SendAsync(string accessToken, OutgoingMail mail, CancellationToken cancellationToken = default);

    /// <summary>Whether a connection with <paramref name="scopes"/> can read the replies of a conversation.</summary>
    bool CanReceive(string? scopes) => false;

    /// <summary>The messages of a conversation received after <paramref name="since"/>.</summary>
    Task<IReadOnlyList<MailboxMessage>> ConversationAsync(string accessToken, string conversationId, DateTimeOffset since, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>The raw MIME message.</summary>
    Task<byte[]?> RawAsync(string accessToken, string messageId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>Whether a connection with <paramref name="scopes"/> can post to Teams channels.</summary>
    bool CanPostToTeams(string? scopes) => false;

    Task<IReadOnlyList<TeamsChannel>> TeamsChannelsAsync(string accessToken, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    /// <summary>Posts an HTML message to a Teams channel as the person.</summary>
    Task PostToTeamsAsync(string accessToken, string teamId, string channelId, string html, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>A call the mailbox provider refused.</summary>
public sealed class MailboxSendException(string message, bool reconnect) : Exception(message)
{
    /// <summary>The grant was revoked or expired: the person must connect the mailbox again.</summary>
    public bool Reconnect { get; } = reconnect;
}
