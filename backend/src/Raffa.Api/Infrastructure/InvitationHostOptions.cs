using Raffa.Identity.Workspace.Application;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// Binds the <c>Invitations</c> configuration section — the keys <c>infra/modules/containerapps</c>
/// publishes onto the API container app (task E16/F01/US01/T01) and task E17/F01/US01/T01 (wave w15,
/// NW-67/NW-68) consumes: <c>Invitations__Mail__Enabled</c>, <c>Invitations__Mail__SenderAddress</c>,
/// <c>Invitations__Mail__ConnectionString</c> (a Key Vault secret), <c>Invitations__AcceptUrlBase</c>,
/// <c>Invitations__GuestProvisioning__Enabled</c> and <c>Invitations__GuestProvisioning__TenantId</c>.
///
/// <para>
/// Every property is optional at bind time (ADR-026 §D6: the binding never <c>?? throw</c>s on an
/// unbound key — a dev box or a container deployed before the Terraform apply that publishes these
/// keys still boots). What does fail closed at startup is the <em>composed pair</em>
/// <see cref="ValidateOrThrow"/> checks (ADR-025 §J.6b): mail enabled with no usable
/// <c>https://</c> accept base, sender or connection string is the one combination that must never
/// run, because it would mail a fragment with no origin — or nothing at all — while the 201 claimed
/// <c>sent</c>.
/// </para>
/// <para>
/// <c>internal</c>, like <see cref="AzureAdOptions"/>: a host type whose name carries no
/// "Program"/"Startup"/"Extensions" must not be <c>public</c>
/// (<c>Raffa.ArchitectureTests.DependencyDirectionTests.Host_must_not_contain_domain_types</c>).
/// </para>
/// </summary>
internal sealed class InvitationHostOptions
{
    public const string SectionName = "Invitations";

    public MailOptions Mail { get; set; } = new();

    /// <summary>See <see cref="InvitationOptions.AcceptUrlBase"/>: Terraform's <c>https://{spa_host_name}</c>, never a request value.</summary>
    public string? AcceptUrlBase { get; set; }

    public GuestProvisioningOptions GuestProvisioning { get; set; } = new();

    public sealed class MailOptions
    {
        /// <summary>A product switch (ADR-005 w15 footer §5 rule 3): a working connection string
        /// behind <see langword="false"/> is harmless and the mailer stays the Null default.</summary>
        public bool Enabled { get; set; }

        /// <summary>The Azure Communication Services managed-domain sender (<c>infra/modules/communication</c>).</summary>
        public string? SenderAddress { get; set; }

        /// <summary>The ACS connection string, sourced from Key Vault (<c>acs-cs</c>); never logged.</summary>
        public string? ConnectionString { get; set; }
    }

    public sealed class GuestProvisioningOptions
    {
        /// <summary>Gates the Graph adapter (ADR-026 w15 footer §2; default off ⇒ <c>NotConfigured</c>).</summary>
        public bool Enabled { get; set; }

        /// <summary>The Entra directory the guest is invited into — bound for completeness; the
        /// workload identity's own directory is where <c>POST /invitations</c> lands.</summary>
        public string? TenantId { get; set; }
    }

    /// <summary>ADR-025 §J.6b: fail closed at startup on the composed pair, and only on it.</summary>
    public void ValidateOrThrow()
    {
        if (!Mail.Enabled)
        {
            return;
        }

        if (!InvitationOptions.IsUsableBase(AcceptUrlBase))
        {
            throw new InvalidOperationException(
                "Invitations__Mail__Enabled is true but Invitations__AcceptUrlBase is missing or is not an " +
                "absolute https:// origin. Refusing to start: a mailed invitation link must carry this " +
                "environment's own SPA origin (ADR-025 §J.6a/§J.6b), never a fragment with no origin.");
        }

        if (string.IsNullOrWhiteSpace(Mail.SenderAddress) || string.IsNullOrWhiteSpace(Mail.ConnectionString))
        {
            throw new InvalidOperationException(
                "Invitations__Mail__Enabled is true but Invitations__Mail__SenderAddress or " +
                "Invitations__Mail__ConnectionString is missing. Refusing to start: a mailer that cannot " +
                "send must not be registered as the transport (ADR-025 §J.6b).");
        }
    }
}
