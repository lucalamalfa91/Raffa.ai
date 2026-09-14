using Azure.Identity;
using Raffa.Identity.Workspace.Application;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace Raffa.Api.Infrastructure;

/// <summary>
/// The one and only Microsoft Graph call site in the solution (task E17/F01/US01/T01, wave w15,
/// NW-67; ADR-025 §J.1, ADR-026 w15 footer §2): <c>POST /invitations</c> with
/// <c>sendInvitationMessage: false</c>, authenticated as the existing per-environment workload
/// managed identity (<c>DefaultAzureCredential</c> resolves it through <c>AZURE_CLIENT_ID</c> in
/// Container Apps; a developer's own login locally), which Terraform grants the
/// <c>User.Invite.All</c> application permission — not the Guest Inviter directory role, and never
/// <c>User.ReadWrite.All</c> (§J.1a). Registered by <c>Program.cs</c> only while
/// <c>Invitations__GuestProvisioning__Enabled</c> is true; the module's own
/// <see cref="Raffa.Identity.Workspace.Infrastructure.NullGuestProvisioner"/> answers otherwise.
///
/// <para>
/// <b>What is never surfaced.</b> Graph returns a redeem URL — a credential-shaped URL that would
/// redeem the guest with no Raffa invitation row, no token and no membership (§J.1e).
/// It is never read, mailed, returned, stored, logged or audited: Raffa's own
/// <c>/invite/accept#&lt;token&gt;</c> stays the one accept channel. A Graph error body can carry
/// directory policy and other users' data (§J.4b), so a failure leaves this type as a named reason
/// from the closed set plus the response's own opaque request id — never the raw body.
/// </para>
/// <para>
/// <b>Two outcomes, on purpose.</b> A fresh guest and an already-present one both come back as
/// <c>identityProvisioned: true</c> with the guest's object id (§J.4a): the invite form must not
/// become a directory-enumeration oracle, and A15-5's no-op must be indistinguishable in shape from
/// a fresh provision. The object id is the <c>oid</c> that person's token will carry, bound into
/// <c>workspace_user.external_subject_id</c> at invite time (§J.3b).
/// </para>
/// </summary>
internal sealed class GraphGuestProvisioner(
    GraphServiceClient graph,
    InvitationHostOptions options,
    ILogger<GraphGuestProvisioner> logger) : IGuestProvisioner
{
    /// <summary>Graph's own scope for an application permission held by the workload identity.</summary>
    public static readonly string[] Scopes = ["https://graph.microsoft.com/.default"];

    /// <summary>Graph's <c>invitation.status</c> for an address that already redeemed a guest object.</summary>
    private const string CompletedStatus = "Completed";

    public async Task<GuestProvisioningResult> EnsureGuestAsync(string email, string workspaceName, CancellationToken cancellationToken = default)
    {
        // `inviteRedirectUrl` is mandatory on the Graph side and only ever appears inside the redeem
        // URL this type never reads (§J.1e); this environment's own SPA origin is the honest value,
        // with Microsoft's sign-in origin as the fallback when no accept base is configured.
        var redirectUrl = InvitationOptions.IsUsableBase(options.AcceptUrlBase)
            ? options.AcceptUrlBase!
            : "https://login.microsoftonline.com";

        var request = new Invitation
        {
            InvitedUserEmailAddress = email,
            InviteRedirectUrl = redirectUrl,
            SendInvitationMessage = false,
            InvitedUserType = "Guest",
            InvitedUserMessageInfo = null,
        };

        try
        {
            var response = await graph.Invitations.PostAsync(request, cancellationToken: cancellationToken).ConfigureAwait(false);
            var guestObjectId = response?.InvitedUser?.Id;
            if (string.IsNullOrWhiteSpace(guestObjectId))
            {
                logger.LogWarning("Graph POST /invitations answered without an invitedUser id for workspace '{WorkspaceName}'", workspaceName);
                return GuestProvisioningResult.Failed(GuestProvisioningFailureReason.ProvisioningFailed);
            }

            // Graph's own `status` is the only thing that distinguishes "created" from "already
            // present" -- and it is deliberately collapsed into one externally visible outcome.
            var alreadyPresent = string.Equals(response!.Status, CompletedStatus, StringComparison.OrdinalIgnoreCase);
            logger.LogInformation(
                "Guest provisioning {Outcome} for workspace '{WorkspaceName}' (guest object {GuestObjectId})",
                alreadyPresent ? "confirmed an existing guest" : "created a guest", workspaceName, guestObjectId);

            return alreadyPresent
                ? GuestProvisioningResult.AlreadyPresent(guestObjectId)
                : GuestProvisioningResult.Provisioned(guestObjectId);
        }
        catch (ODataError error)
        {
            // §J.4b: the named reason and the opaque request id, never the error body.
            var reason = error.ResponseStatusCode switch
            {
                401 or 403 => GuestProvisioningFailureReason.ConsentMissing,
                >= 500 => GuestProvisioningFailureReason.DirectoryUnavailable,
                _ => GuestProvisioningFailureReason.ProvisioningFailed,
            };
            var requestId = ReadRequestId(error);
            logger.LogWarning(
                "Guest provisioning failed for workspace '{WorkspaceName}': {Reason} (Graph status {Status}, code {Code}, request-id {RequestId})",
                workspaceName, reason.ToWireValue(), error.ResponseStatusCode, error.Error?.Code ?? "-", requestId ?? "-");
            return GuestProvisioningResult.Failed(reason, requestId);
        }
        catch (CredentialUnavailableException exception)
        {
            logger.LogWarning(exception, "Guest provisioning could not obtain a workload identity token for workspace '{WorkspaceName}'", workspaceName);
            return GuestProvisioningResult.Failed(GuestProvisioningFailureReason.DirectoryUnavailable);
        }
        catch (AuthenticationFailedException exception)
        {
            logger.LogWarning(exception, "Guest provisioning could not authenticate the workload identity for workspace '{WorkspaceName}'", workspaceName);
            return GuestProvisioningResult.Failed(GuestProvisioningFailureReason.DirectoryUnavailable);
        }
        catch (HttpRequestException exception)
        {
            logger.LogWarning(exception, "Guest provisioning could not reach the directory for workspace '{WorkspaceName}'", workspaceName);
            return GuestProvisioningResult.Failed(GuestProvisioningFailureReason.DirectoryUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Guest provisioning timed out against the directory for workspace '{WorkspaceName}'", workspaceName);
            return GuestProvisioningResult.Failed(GuestProvisioningFailureReason.DirectoryUnavailable);
        }
    }

    private static string? ReadRequestId(ODataError error)
    {
        if (error.ResponseHeaders is not null
            && error.ResponseHeaders.TryGetValue("request-id", out var values))
        {
            return values.FirstOrDefault();
        }

        return error.Error?.InnerError?.AdditionalData is { } data
            && data.TryGetValue("request-id", out var value)
            ? value?.ToString()
            : null;
    }
}
