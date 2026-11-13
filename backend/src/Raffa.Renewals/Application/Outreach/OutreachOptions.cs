namespace Raffa.Renewals.Application.Outreach;

/// <summary>
/// <c>CaseMail</c> configuration (renewal plan F2/F3): company mailboxes, case addresses and inbound mail.
/// Every part is optional: what is not configured stays off and the manual path (copy, paste, upload the
/// email file) keeps working.
/// </summary>
public sealed class CaseMailOptions
{
    public const string SectionName = "CaseMail";

    /// <summary>Base64 of 32 random bytes: encrypts refresh tokens and webhook targets, signs the OAuth state.
    /// A Key Vault secret (<c>CaseMail__TokenKey</c>).</summary>
    public string? TokenKey { get; set; }

    /// <summary>The domain case addresses live on (a Resend receiving domain), e.g. <c>in.example.com</c>.</summary>
    public string? InboundDomain { get; set; }

    /// <summary>Public https base of the API (the OAuth redirect lands on <c>/api/mailbox/callback/{provider}</c>).</summary>
    public string? ApiPublicBaseUrl { get; set; }

    /// <summary>Public https base of the web app (where the browser returns after connecting a mailbox).</summary>
    public string? WebPublicBaseUrl { get; set; }

    public OAuthClientOptions Microsoft { get; set; } = new();

    public OAuthClientOptions Google { get; set; } = new();

    public ResendOptions Resend { get; set; } = new();

    public bool HasTokenKey => TryKey(out _);

    public bool InboundEnabled => !string.IsNullOrWhiteSpace(InboundDomain);

    internal bool TryKey(out byte[] key)
    {
        key = [];
        if (string.IsNullOrWhiteSpace(TokenKey))
        {
            return false;
        }

        try
        {
            key = Convert.FromBase64String(TokenKey.Trim());
            return key.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public sealed class OAuthClientOptions
{
    public string? ClientId { get; set; }

    public string? ClientSecret { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
}

public sealed class ResendOptions
{
    /// <summary>API key with read access to received emails.</summary>
    public string? ApiKey { get; set; }

    /// <summary>The webhook signing secret (<c>whsec_…</c>).</summary>
    public string? WebhookSecret { get; set; }

    public string BaseUrl { get; set; } = "https://api.resend.com";
}

/// <summary><c>Notifications</c> configuration: Web Push (VAPID) keys and the sender of notification emails.</summary>
public sealed class NotificationOptions
{
    public const string SectionName = "Notifications";

    /// <summary>VAPID subject (<c>mailto:</c> or https URL).</summary>
    public string? VapidSubject { get; set; }

    public string? VapidPublicKey { get; set; }

    public string? VapidPrivateKey { get; set; }

    public bool WebPushEnabled =>
        !string.IsNullOrWhiteSpace(VapidSubject) && !string.IsNullOrWhiteSpace(VapidPublicKey) && !string.IsNullOrWhiteSpace(VapidPrivateKey);
}
