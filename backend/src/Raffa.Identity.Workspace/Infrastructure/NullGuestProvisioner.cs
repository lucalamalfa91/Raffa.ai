using Raffa.Identity.Workspace.Application;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>
/// The default <see cref="IGuestProvisioner"/> (task E17/F01/US01/T01, wave w15; ADR-026 w15 footer
/// §2): always <see cref="GuestProvisioningStatus.NotConfigured"/>, never a directory call. Under it
/// the invite behaves exactly as it did on <c>main</c> before this wave — link-only, no directory
/// write — and the 201's <c>identityProvisioned</c> is an honest <see langword="false"/>. The host
/// replaces it with the Graph adapter when <c>Invitations__GuestProvisioning__Enabled</c> is true.
/// </summary>
public sealed class NullGuestProvisioner : IGuestProvisioner
{
    public Task<GuestProvisioningResult> EnsureGuestAsync(string email, string workspaceName, CancellationToken cancellationToken = default) =>
        Task.FromResult(GuestProvisioningResult.NotConfigured());
}
