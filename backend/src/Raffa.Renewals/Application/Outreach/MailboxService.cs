using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Raffa.Renewals.Domain.Cases;
using Raffa.Renewals.Infrastructure;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;

namespace Raffa.Renewals.Application.Outreach;

/// <summary>A person's connected mailbox as the screens show it (never the token).</summary>
public sealed record MailboxInfo(string Provider, string Address, string? DisplayName, DateTimeOffset ConnectedAt, DateTimeOffset? LastUsedAt, bool NeedsReconnect);

/// <summary><c>GET /api/mailbox</c>: the caller's mailbox and the providers this deployment offers.</summary>
public sealed record MailboxStatus(MailboxInfo? Mailbox, IReadOnlyList<string> Providers, bool Available);

/// <summary>The outcome of the OAuth callback: where to send the browser back.</summary>
public sealed record MailboxConnectOutcome(bool Connected, string RedirectUrl, string? Error);

/// <summary>
/// Company mailboxes (plan F2): a person connects their Microsoft 365 or Google mailbox with the send-only
/// permission, and Raffa sends the approved case emails from it — the supplier always sees the company
/// address. The OAuth state is signed and carries the workspace and the person (the callback is a browser
/// redirect without the API bearer token) and expires after 10 minutes.
/// </summary>
public sealed class MailboxService(
    RenewalsDbContext db,
    ITenantContext tenantContext,
    IClock clock,
    CaseMailOptions options,
    IEnumerable<IMailboxProvider> providers)
{
    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
    private readonly SecretProtector? protector = SecretProtector.From(options);

    private IMailboxProvider? Provider(string name) =>
        providers.FirstOrDefault(p => p.IsConfigured && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    public bool Available => protector is not null && !string.IsNullOrWhiteSpace(options.ApiPublicBaseUrl) && providers.Any(p => p.IsConfigured);

    public async Task<MailboxStatus> StatusAsync(TenantId tenantId, string identity, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var row = await db.MailboxConnections.AsNoTracking()
            .FirstOrDefaultAsync(x => x.UserIdentity == identity, ct).ConfigureAwait(false);
        return new MailboxStatus(
            row is null ? null : Info(row),
            Available ? providers.Where(p => p.IsConfigured).Select(p => p.Name).ToList() : [],
            Available);
    }

    internal static MailboxInfo Info(MailboxConnectionRecord row) =>
        new(row.Provider, row.Address, row.DisplayName, row.ConnectedAt, row.LastUsedAt, row.LastError == "reconnect");

    /// <summary><c>POST /api/mailbox/connect</c>: the provider's consent page URL, or null when unavailable.</summary>
    public string? BeginConnect(TenantId tenantId, string identity, string provider, string? loginHint)
    {
        if (!Available || Provider(provider) is not { } p)
        {
            return null;
        }

        var state = protector!.Sign(JsonSerializer.Serialize(new OAuthState(
            tenantId.Value, identity, p.Name, clock.UtcNow.Add(StateLifetime).ToUnixTimeSeconds())));
        return p.AuthorizeUrl(state, RedirectUri(p.Name), loginHint);
    }

    /// <summary><c>GET /api/mailbox/callback/{provider}</c>: exchanges the code and stores the connection.</summary>
    public async Task<MailboxConnectOutcome> CompleteConnectAsync(string provider, string? state, string? code, string? error, CancellationToken ct = default)
    {
        var back = (string.IsNullOrWhiteSpace(options.WebPublicBaseUrl) ? string.Empty : options.WebPublicBaseUrl.TrimEnd('/')) + "/settings/notifications";
        if (protector is null || Provider(provider) is not { } p)
        {
            return new(false, back + "?mailbox=unavailable", "unavailable");
        }

        var payload = string.IsNullOrEmpty(state) ? null : protector.Verify(state);
        var s = payload is null ? null : JsonSerializer.Deserialize<OAuthState>(payload);
        if (s is null || s.Provider != p.Name || DateTimeOffset.FromUnixTimeSeconds(s.Exp) < clock.UtcNow)
        {
            return new(false, back + "?mailbox=expired", "invalid_state");
        }

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
        {
            return new(false, back + "?mailbox=denied", error ?? "no_code");
        }

        MailboxGrant grant;
        try
        {
            grant = await p.ExchangeCodeAsync(code, RedirectUri(p.Name), ct).ConfigureAwait(false);
        }
        catch (MailboxSendException)
        {
            return new(false, back + "?mailbox=failed", "exchange_failed");
        }

        var tenantId = new TenantId(s.Tenant);
        using var scope = tenantContext.BeginScope(tenantId);
        var row = await db.MailboxConnections.FirstOrDefaultAsync(x => x.UserIdentity == s.Identity, ct).ConfigureAwait(false);
        if (row is null)
        {
            row = new MailboxConnectionRecord
            {
                TenantId = tenantId,
                UserIdentity = s.Identity,
                Provider = p.Name,
                Address = grant.Address,
                RefreshTokenProtected = protector.Protect(grant.RefreshToken),
                ConnectedAt = clock.UtcNow,
            };
            db.MailboxConnections.Add(row);
        }

        row.Provider = p.Name;
        row.Address = grant.Address;
        row.DisplayName = grant.DisplayName;
        row.RefreshTokenProtected = protector.Protect(grant.RefreshToken);
        row.ConnectedAt = clock.UtcNow;
        row.LastError = null;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return new(true, back + "?mailbox=connected", null);
    }

    public async Task<bool> DisconnectAsync(TenantId tenantId, string identity, CancellationToken ct = default)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        var row = await db.MailboxConnections.FirstOrDefaultAsync(x => x.UserIdentity == identity, ct).ConfigureAwait(false);
        if (row is null)
        {
            return false;
        }

        db.MailboxConnections.Remove(row);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    /// <summary>The caller's usable connection, or null.</summary>
    internal async Task<MailboxConnectionRecord?> ConnectionAsync(TenantId tenantId, string identity, CancellationToken ct)
    {
        using var scope = tenantContext.BeginScope(tenantId);
        return await db.MailboxConnections.FirstOrDefaultAsync(x => x.UserIdentity == identity, ct).ConfigureAwait(false);
    }

    /// <summary>Sends from the connection; keeps a rotated token and records a revoked grant.</summary>
    internal async Task SendAsync(TenantId tenantId, MailboxConnectionRecord row, OutgoingMail mail, CancellationToken ct)
    {
        var p = Provider(row.Provider) ?? throw new MailboxSendException("This mailbox provider is not available.", false);
        var token = protector?.Unprotect(row.RefreshTokenProtected) ?? throw new MailboxSendException("The mailbox must be connected again.", true);
        using var scope = tenantContext.BeginScope(tenantId);
        try
        {
            var rotated = await p.SendAsync(token, mail, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(rotated) && rotated != token)
            {
                row.RefreshTokenProtected = protector!.Protect(rotated);
            }

            row.LastUsedAt = clock.UtcNow;
            row.LastError = null;
        }
        catch (MailboxSendException ex)
        {
            row.LastError = ex.Reconnect ? "reconnect" : "refused";
            await db.SaveChangesAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private string RedirectUri(string provider) => options.ApiPublicBaseUrl!.TrimEnd('/') + "/api/mailbox/callback/" + provider;

    private sealed record OAuthState(Guid Tenant, string Identity, string Provider, long Exp);
}
