using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Raffa.Identity.Workspace.Application;
using Raffa.Identity.Workspace.Domain;
using Raffa.SharedKernel;
using Raffa.SharedKernel.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <summary>
/// The token lifecycle (task E15/F01/US01/T01, wave w14; ADR-025 §C, §D.1/§D.3/§D.5): mint, hash,
/// verify, issue, pre-accept, accept, revoke. Owns everything about the token's own shape and the
/// tenant-scope discipline that shape makes safe (Rules C4/C5); delegates the actual
/// <see cref="WorkspaceUser"/>/<see cref="WorkspaceMembership"/>/<see cref="WorkspaceInvitation"/>
/// writes to <see cref="WorkspaceMembershipService"/> (<see cref="WorkspaceMembershipService.InviteAsync"/>
/// /<see cref="WorkspaceMembershipService.AcceptInvitationAsync"/>), the single writer of those
/// tables, resolved via DI from the same request scope so both services share one
/// <see cref="IdentityWorkspaceDbContext"/> and one ambient tenant scope.
///
/// <para>
/// <b>The token</b> (Rule C1-C10): <c>{tenantId:N}.{secret}</c>, where <c>secret</c> is a 256-bit
/// CSPRNG value, base64url-encoded (<see cref="RandomNumberGenerator"/> / <see cref="Base64Url"/>).
/// Only <see cref="ComputeHash"/>'s SHA-256 digest of the secret half is ever persisted
/// (<see cref="WorkspaceInvitation.TokenHash"/>) — the token itself is never stored, logged or
/// audited anywhere in this type. <see cref="TryParseToken"/> is a pure, static, no-scope parse:
/// <see cref="Guid.TryParseExact(string?, string, out Guid)"/> on the prefix <b>before</b> anything
/// else runs (Rule C4) — a parse failure returns <see langword="false"/> with no
/// <see cref="ITenantContext.BeginScope"/> ever entered, which is exactly what keeps
/// <c>TenantRlsConnectionInterceptor.BuildSetCommandText</c>'s own "the tenant is always a parsed
/// Guid" justification true (that type's own doc comment, ADR-025 Rule C4). No
/// string-taking overload is added to <see cref="TenantId"/> anywhere in this file.
/// </para>
///
/// <para>
/// <b>No constant-time compare</b> (Rule C6): every lookup below is a plain <c>==</c> against
/// <see cref="WorkspaceInvitation.TokenHash"/>, which EF Core translates to an index seek on
/// <c>ix_workspace_invitation_tenant_id_token_hash</c> — an index lookup, not a secret-to-secret
/// comparison, so there is nothing to time-equalize. Do not "harden" this into a table scan.
/// </para>
///
/// <para>
/// <b>Task E17/F01/US01/T01 (wave w15, NW-67/NW-68; ADR-025 §J, ADR-026 w15 footers §1–§9).</b>
/// <see cref="IssueAsync"/> now runs in this order: the per-tenant cap (§J.1c.4) → the
/// <see cref="IGuestProvisioner"/> (<b>guest first, invitation row second</b>, §J.2a: a failure
/// leaves no row, no token and no mail, and comes back as a 502 with a reason from a closed set) →
/// the rows, with the guest's object id bound into <see cref="WorkspaceUser.ExternalSubjectId"/>
/// (§J.3b, so the accept matches on <c>oid</c> exactly) → the absolute accept link
/// (<see cref="InvitationOptions.ComposeAcceptUrl"/>) → the mailer, whose one call yields both
/// <c>mailDelivered</c> and <c>deliveryOutcome</c> (ADR-026 §8's biconditional, computed here and
/// nowhere else). <see cref="AcceptAsync"/> matches the signed-in identity in ADR-010 w15 §2.3's
/// order — the <c>oid</c> bound at invite → the token's <c>email</c> claim → refuse — and never
/// parses a <c>#EXT#</c> UPN.
/// </para>
/// </summary>
public sealed class WorkspaceInvitationService(
    IdentityWorkspaceDbContext db,
    ITenantContext tenantContext,
    IClock clock,
    IAuditWriter auditWriter,
    IInvitationMailer mailer,
    WorkspaceMembershipService membershipService,
    IGuestProvisioner guestProvisioner,
    InvitationOptions options)
{
    /// <summary>ADR-025 Rule C7: 7-day absolute expiry, evaluated server-side against <see cref="IClock"/>.</summary>
    public const int TokenExpiryDays = 7;

    /// <summary>ADR-025 §J.7's five additive audit verbs (free-form strings, no schema change).</summary>
    public const string GuestProvisionedAuditAction = "workspace.guest.provisioned";
    public const string GuestProvisioningFailedAuditAction = "workspace.guest.provisioning_failed";
    public const string CapReachedAuditAction = "workspace.invitation.cap_reached";
    public const string MailSentAuditAction = "workspace.invitation.mail_sent";
    public const string MailFailedAuditAction = "workspace.invitation.mail_failed";

    /// <summary>256 bits (ADR-025 Rule C1).</summary>
    private const int SecretByteLength = 32;

    /// <summary>
    /// Issue (ADR-025 §D.1, ADR-026 §D5, w15: §J.1c.4/§J.2a/§J.3b/§J.6): cap → provision the guest →
    /// mint a fresh token and delegate the <see cref="WorkspaceUser"/>/<see cref="WorkspaceInvitation"/>
    /// write to <see cref="WorkspaceMembershipService.InviteAsync"/> (which replaces a still-live
    /// invitation for the same address in one transaction, §J.2b) → attempt delivery. The plaintext
    /// token exists only in this call's own stack and in the returned
    /// <see cref="InvitationIssueResult.AcceptUrl"/> — never persisted (Rule C10), and the 201
    /// response carries it exactly once (the mail is the second channel, §J.6).
    /// </summary>
    public async Task<InvitationIssueResult> IssueAsync(
        TenantId tenantId, string email, WorkspaceRoleName role, string invitedBy, CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;
        var now = clock.UtcNow;

        WorkspaceTenant? workspace;
        int liveInvitations;
        bool replacesLiveInvitation;
        using (tenantContext.BeginScope(tenantId))
        {
            workspace = await db.Workspaces.AsNoTracking()
                .SingleOrDefaultAsync(w => w.TenantId == tenantId, cancellationToken)
                .ConfigureAwait(false);
            liveInvitations = await db.WorkspaceInvitations
                .CountAsync(i => i.TenantId == tenantId && i.AcceptedAt == null && i.RevokedAt == null, cancellationToken)
                .ConfigureAwait(false);
            replacesLiveInvitation = await db.WorkspaceInvitations
                .AnyAsync(
                    i => i.TenantId == tenantId && i.Email == normalizedEmail && i.AcceptedAt == null && i.RevokedAt == null,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        // ADR-025 §J.1c.4: the cap bounds DISTINCT live addresses (the directory-spam shape) -- a
        // re-issue for an address that already holds a live invitation replaces it and adds none.
        if (!replacesLiveInvitation && liveInvitations >= options.LiveInvitationCap)
        {
            await auditWriter.WriteAsync(
                new AuditEntry(
                    tenantId, invitedBy, CapReachedAuditAction, "Workspace", options.LiveInvitationCap.ToString(),
                    now, $"cap={options.LiveInvitationCap}; live={liveInvitations}"),
                cancellationToken).ConfigureAwait(false);

            return InvitationIssueResult.Failure(
                MembershipOperationStatus.Conflict,
                $"this workspace has reached its limit of {options.LiveInvitationCap} pending invitations; " +
                "revoke one before inviting another address.");
        }

        // ADR-025 §J.2a: guest first, invitation row second. Nothing is written before this answers.
        var provisioning = await guestProvisioner
            .EnsureGuestAsync(normalizedEmail, workspace?.Name ?? string.Empty, cancellationToken)
            .ConfigureAwait(false);

        if (provisioning.Status == GuestProvisioningStatus.Failed)
        {
            var reason = provisioning.FailureReason ?? GuestProvisioningFailureReason.ProvisioningFailed;
            // §J.7: the named reason and the directory's opaque request id -- never the raw body.
            await auditWriter.WriteAsync(
                new AuditEntry(
                    tenantId, invitedBy, GuestProvisioningFailedAuditAction, "WorkspaceInvitation", "not-issued", now,
                    $"email={normalizedEmail}; reason={reason.ToWireValue()}; request-id={provisioning.CorrelationId ?? "-"}"),
                cancellationToken).ConfigureAwait(false);

            return InvitationIssueResult.ProvisioningFailed(reason);
        }

        var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretByteLength));
        var token = $"{tenantId.Value:N}.{secret}";
        var tokenHash = ComputeHash(secret);
        var expiresAt = now.AddDays(TokenExpiryDays);

        var inviteResult = await membershipService
            .InviteAsync(tenantId, normalizedEmail, role, invitedBy, tokenHash, expiresAt, cancellationToken, provisioning.GuestObjectId)
            .ConfigureAwait(false);

        if (!inviteResult.IsSuccess)
        {
            return InvitationIssueResult.Failure(inviteResult.Status, inviteResult.Error);
        }

        var invitation = inviteResult.Invitation!;

        if (provisioning.IdentityProvisioned)
        {
            await auditWriter.WriteAsync(
                new AuditEntry(
                    tenantId, invitedBy, GuestProvisionedAuditAction, "WorkspaceInvitation", invitation.Id.Value.ToString(), now,
                    $"email={normalizedEmail}; role={role}; guest={provisioning.GuestObjectId}"),
                cancellationToken).ConfigureAwait(false);
        }

        var acceptUrl = options.ComposeAcceptUrl(token);

        // ADR-026 §D6/§8: mailDelivered IS the mailer's bool, and deliveryOutcome is computed from
        // the same call, here and nowhere else -- `sent` if and only if `mailDelivered`.
        var deliveryOutcome = InvitationDeliveryOutcome.NoTransport;
        var mailDelivered = false;
        if (mailer.IsConfigured)
        {
            mailDelivered = await mailer
                .TrySendAsync(invitation.Email, workspace?.Name ?? string.Empty, role, acceptUrl, invitation.ExpiresAt, cancellationToken)
                .ConfigureAwait(false);
            deliveryOutcome = mailDelivered ? InvitationDeliveryOutcome.Sent : InvitationDeliveryOutcome.MailFailed;

            await auditWriter.WriteAsync(
                new AuditEntry(
                    tenantId, invitedBy, mailDelivered ? MailSentAuditAction : MailFailedAuditAction, "WorkspaceInvitation",
                    invitation.Id.Value.ToString(), now, $"email={normalizedEmail}"),
                cancellationToken).ConfigureAwait(false);
        }

        return InvitationIssueResult.Success(invitation, acceptUrl, mailDelivered, deliveryOutcome, provisioning.IdentityProvisioned);
    }

    /// <summary>
    /// Pre-accept (`GET /api/invites`, ADR-025 §D.3e / Rule C4/C5): parses the token prefix before
    /// any scope entry, enters that tenant's scope, matches the hash <b>first</b> — nothing else is
    /// read until it succeeds, or this becomes a workspace-name oracle for any guessed tenant id —
    /// and returns only workspace name, role and expiry. Never the invited email (echoing it turns
    /// a leaked link into an address-discovery tool), no roster, no counts.
    /// </summary>
    public async Task<InvitationPreviewResult> PreAcceptAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (!TryParseToken(rawToken, out var tenantId, out var secret))
        {
            return InvitationPreviewResult.Failure(MembershipOperationStatus.NotFound);
        }

        using var _ = tenantContext.BeginScope(tenantId);

        var tokenHash = ComputeHash(secret);
        var invitation = await db.WorkspaceInvitations
            .SingleOrDefaultAsync(i => i.TenantId == tenantId && i.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);

        if (invitation is null || invitation.RevokedAt is not null || invitation.AcceptedAt is not null)
        {
            return InvitationPreviewResult.Failure(MembershipOperationStatus.NotFound);
        }

        if (invitation.ExpiresAt <= clock.UtcNow)
        {
            return InvitationPreviewResult.Failure(MembershipOperationStatus.Expired);
        }

        var roleRow = await db.WorkspaceRoles.AsNoTracking()
            .SingleOrDefaultAsync(r => r.TenantId == tenantId && r.Id == invitation.WorkspaceRoleId, cancellationToken)
            .ConfigureAwait(false);

        var workspace = await db.Workspaces.AsNoTracking()
            .SingleOrDefaultAsync(w => w.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        if (roleRow is null || workspace is null)
        {
            return InvitationPreviewResult.Failure(MembershipOperationStatus.NotFound);
        }

        return InvitationPreviewResult.Success(workspace.Name, roleRow.Name, invitation.ExpiresAt);
    }

    /// <summary>
    /// Accept (`POST /api/invites/accept`, ADR-025 Rule D.3b-d): same parse-before-scope and
    /// hash-first discipline as <see cref="PreAcceptAsync"/>, then the state-machine order §B fixes
    /// — revoked/unknown/malformed → 404 (one indistinguishable answer for every "not yours" case);
    /// expired → 410 (safe because reaching this branch already proves possession of the 256-bit
    /// secret); already accepted by <b>this</b> identity → 409 (idempotency signal); by any other →
    /// 404; identity mismatch → 403 with a reason that never echoes the invited address — before
    /// delegating the actual grant to <see cref="WorkspaceMembershipService.AcceptInvitationAsync"/>.
    ///
    /// <para>
    /// <b>Who counts as the invited person</b> (ADR-010 w15 §2.1/§2.3, ADR-025 §J.3b): the
    /// <see cref="WorkspaceUser.ExternalSubjectId"/> bound at invite time (the guest's <c>oid</c>) or
    /// at a previous sign-in must equal <paramref name="signedInIdentity"/>; failing a bound subject,
    /// the token's own <paramref name="signedInEmail"/> claim must equal the invited address; failing
    /// that, an identity that <em>is</em> the invited address (the interim header-bridged hosts) is
    /// accepted. Never a mangled <c>#EXT#</c> UPN, which is why a guest signed in with no bound
    /// subject and no <c>email</c> claim is refused and told to be re-invited rather than de-mangled.
    /// </para>
    /// </summary>
    public async Task<InvitationAcceptResult> AcceptAsync(
        string rawToken, string signedInIdentity, string? signedInEmail = null, CancellationToken cancellationToken = default)
    {
        if (!TryParseToken(rawToken, out var tenantId, out var secret))
        {
            return InvitationAcceptResult.Failure(MembershipOperationStatus.NotFound, null);
        }

        using var _ = tenantContext.BeginScope(tenantId);

        var tokenHash = ComputeHash(secret);
        var invitation = await db.WorkspaceInvitations
            .SingleOrDefaultAsync(i => i.TenantId == tenantId && i.TokenHash == tokenHash, cancellationToken)
            .ConfigureAwait(false);

        if (invitation is null || invitation.RevokedAt is not null)
        {
            return InvitationAcceptResult.Failure(MembershipOperationStatus.NotFound, null);
        }

        var invitedUser = await db.WorkspaceUsers.AsNoTracking()
            .SingleOrDefaultAsync(u => u.TenantId == tenantId && u.Email == invitation.Email, cancellationToken)
            .ConfigureAwait(false);
        var isInvitedPerson = MatchesInvitedPerson(invitation, invitedUser, signedInIdentity, signedInEmail);

        if (invitation.AcceptedAt is not null)
        {
            // Rule D.3d: 409 for the same identity (idempotency signal), 404 for any other (the
            // same "one indistinguishable answer" posture as an unknown token).
            return isInvitedPerson
                ? InvitationAcceptResult.Failure(MembershipOperationStatus.Conflict, "this invitation has already been accepted.")
                : InvitationAcceptResult.Failure(MembershipOperationStatus.NotFound, null);
        }

        if (invitation.ExpiresAt <= clock.UtcNow)
        {
            return InvitationAcceptResult.Failure(MembershipOperationStatus.Expired, null);
        }

        if (!isInvitedPerson)
        {
            // Rule D.3b: never echo the invited address in the HTTP response below. The audit
            // Detail is a different channel -- coding objective #8's own "Detail may carry the
            // invited email and the role" allowance -- and .rejected is one of the nine named
            // actions (ADR-025 §G): a mismatched accept is the security-relevant event of someone
            // attempting to accept an invitation that was not theirs, and it must not go unaudited.
            await auditWriter.WriteAsync(
                new AuditEntry(
                    tenantId, signedInIdentity, "workspace.invitation.rejected", "WorkspaceInvitation",
                    invitation.Id.Value.ToString(), clock.UtcNow, $"email={invitation.Email}"),
                cancellationToken).ConfigureAwait(false);

            return InvitationAcceptResult.Failure(
                MembershipOperationStatus.Forbidden, "the signed-in identity does not match the invited address.");
        }

        var acceptResult = await membershipService
            .AcceptInvitationAsync(tenantId, invitation, signedInIdentity, cancellationToken)
            .ConfigureAwait(false);

        if (!acceptResult.IsSuccess)
        {
            return InvitationAcceptResult.Failure(acceptResult.Status, acceptResult.Error);
        }

        var workspace = await db.Workspaces.AsNoTracking()
            .SingleOrDefaultAsync(w => w.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        return InvitationAcceptResult.Success(tenantId, workspace?.Name ?? string.Empty, acceptResult.Role!.Value);
    }

    /// <summary>
    /// Accept by identity, no token (`POST /api/workspaces/{tenantId}/invites/accept`, fix
    /// 2026-09-14). Extends ADR-025 §F.3 Exception 2 ("accept enters a scope from a caller-supplied
    /// tenant id with no membership check — the only place in the product that does so") with a
    /// second entry point into that <b>same</b> already-sanctioned shape, not a new exception: still
    /// one tenant, still no membership check (the caller is not a member yet, by definition), still a
    /// same-request re-verification before any write. Only the evidence changes — from "possession of
    /// the 256-bit secret" (<see cref="AcceptAsync"/>'s Rule C5 hash match) to "a live invitation
    /// whose email belongs to this signed-in identity", read as the first statement inside the scope,
    /// nothing else about the tenant read on a miss, mirroring Rule C5's own discipline.
    ///
    /// <para>
    /// <b>Why this exists.</b> The token-bearing accept screen (<c>/invite/accept</c>) must complete
    /// inside one browser tab/popup with no page navigation, because Rule C10 forbids persisting the
    /// token to any browser storage — <c>loginPopup</c> is the only same-page path, and in practice it
    /// is fragile (a blocked or non-closing popup strands the invitee signed in with no memory of the
    /// invitation, on a generic sign-in that shows "create a workspace" instead). This method is
    /// `GET /api/workspaces`'s own follow-up call for exactly that caller: signed in, no live
    /// membership anywhere, but a live invitation <see cref="WorkspaceDirectoryService.DiscoverForIdentityAsync"/>
    /// already found for them. It lands them in their workspace regardless of which login path they
    /// took, with no dependency on the token ever having survived the trip.
    /// </para>
    /// <para>
    /// <b>Never an oracle.</b> "No matching <see cref="WorkspaceUser"/> for this identity" and "a
    /// matching user but no live invitation" both answer <see cref="MembershipOperationStatus.NotFound"/>
    /// — identical to a caller-supplied tenant they have no relationship to at all (the same Rule B1
    /// posture <see cref="WorkspaceInvitesEndpointExtensions"/>'s membership guard already takes). The
    /// <paramref name="tenantId"/> this runs against is never invented by the caller in a vacuum — the
    /// client only ever has one because this same identity's own <c>GET /api/workspaces</c> call
    /// surfaced it moments earlier — but this method trusts that provenance no more than
    /// <see cref="AcceptAsync"/> trusts a token's tenant prefix: both re-verify from scratch, inside
    /// the scope, before any write.
    /// </para>
    /// <para>
    /// <b>Deliberately no 409.</b> <see cref="AcceptAsync"/> answers a second accept by the same
    /// identity with 409 (an idempotency signal a token holder can act on). This method has no token
    /// to re-present after success — the live-invitation read below simply finds nothing once
    /// <see cref="WorkspaceInvitation.AcceptedAt"/> is set, so a second call is <see cref="MembershipOperationStatus.NotFound"/>,
    /// not a new outcome to design for: the client only ever calls this once, immediately after
    /// discovery, and never retries blindly.
    /// </para>
    /// </summary>
    public async Task<InvitationAcceptResult> AcceptForIdentityAsync(
        TenantId tenantId, string signedInIdentity, string? signedInEmail = null, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);

        // WorkspaceUser.Email is always stored lower-cased (InviteAsync's own normalization) -- the
        // email-shaped comparisons below lower-case to match it, the same convention
        // WorkspaceDirectoryService.DiscoverForIdentityAsync already applies. The ExternalSubjectId
        // comparison stays ordinal/as-is, mirroring MatchesInvitedPerson's own bound-subject check.
        var normalizedIdentity = signedInIdentity.Trim().ToLowerInvariant();
        var normalizedEmail = signedInEmail?.Trim().ToLowerInvariant();

        // The first read inside this tenant's scope, same discipline as AcceptAsync's own hash match:
        // translates the signed-in identity (an oid, or -- pre-directory-provisioning/legacy rows --
        // an email) to the WorkspaceUser row InviteAsync wrote at invite time, so the invitation query
        // below can match on that row's own stored email rather than guessing at casing.
        var invitedUser = await db.WorkspaceUsers.AsNoTracking()
            .SingleOrDefaultAsync(
                u => u.TenantId == tenantId
                    && (u.ExternalSubjectId == signedInIdentity
                        || u.Email.ToLower() == normalizedIdentity
                        || (normalizedEmail != null && u.Email.ToLower() == normalizedEmail)),
                cancellationToken)
            .ConfigureAwait(false);

        if (invitedUser is null)
        {
            return InvitationAcceptResult.Failure(MembershipOperationStatus.NotFound, null);
        }

        var invitation = await db.WorkspaceInvitations
            .SingleOrDefaultAsync(
                i => i.TenantId == tenantId && i.Email == invitedUser.Email
                    && i.AcceptedAt == null && i.RevokedAt == null,
                cancellationToken)
            .ConfigureAwait(false);

        if (invitation is null)
        {
            return InvitationAcceptResult.Failure(MembershipOperationStatus.NotFound, null);
        }

        if (invitation.ExpiresAt <= clock.UtcNow)
        {
            return InvitationAcceptResult.Failure(MembershipOperationStatus.Expired, null);
        }

        // The identical transactional grant AcceptAsync uses -- same audit actions
        // (workspace.membership.granted, workspace.invitation.accepted), same unique-index race
        // translated to 409 (unreachable here in practice: this method itself is this identity's only
        // path to a second acceptance, and it no longer finds a live invitation once the first
        // succeeds).
        var acceptResult = await membershipService
            .AcceptInvitationAsync(tenantId, invitation, signedInIdentity, cancellationToken)
            .ConfigureAwait(false);

        if (!acceptResult.IsSuccess)
        {
            return InvitationAcceptResult.Failure(acceptResult.Status, acceptResult.Error);
        }

        var workspace = await db.Workspaces.AsNoTracking()
            .SingleOrDefaultAsync(w => w.TenantId == tenantId, cancellationToken)
            .ConfigureAwait(false);

        return InvitationAcceptResult.Success(tenantId, workspace?.Name ?? string.Empty, acceptResult.Role!.Value);
    }

    /// <summary>
    /// Revoke (`DELETE /api/workspaces/{tenantId}/invites/{id}`, ADR-025 §D.5c precedent / AC-11):
    /// stamps <see cref="WorkspaceInvitation.RevokedAt"/> on a still-live invitation. Idempotently
    /// refuses (as <see cref="MembershipOperationStatus.NotFound"/>, mapped to the endpoint's own
    /// 404) an unknown id or one already accepted/revoked — there is nothing left to revoke either
    /// way, and disclosing which of the three it was would be a needless distinction.
    /// </summary>
    public async Task<MembershipOperationStatus> RevokeAsync(
        TenantId tenantId, EntityId invitationId, string revokedBy, CancellationToken cancellationToken = default)
    {
        using var _ = tenantContext.BeginScope(tenantId);

        var invitation = await db.WorkspaceInvitations
            .SingleOrDefaultAsync(i => i.TenantId == tenantId && i.Id == invitationId, cancellationToken)
            .ConfigureAwait(false);

        if (invitation is null || invitation.AcceptedAt is not null || invitation.RevokedAt is not null)
        {
            return MembershipOperationStatus.NotFound;
        }

        var now = clock.UtcNow;
        invitation.RevokedAt = now;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Unlike issue/accept (whose own audit rows are written inside WorkspaceMembershipService,
        // right alongside the write transaction they describe), revoke's whole write lives in this
        // service, so it writes its own row here, in the same already-open tenant scope.
        await auditWriter.WriteAsync(
            new AuditEntry(
                tenantId, revokedBy, "workspace.invitation.revoked", "WorkspaceInvitation",
                invitation.Id.Value.ToString(), now),
            cancellationToken).ConfigureAwait(false);

        return MembershipOperationStatus.Success;
    }

    /// <summary>ADR-010 w15 §2.3's resolution order: the bound <c>oid</c> → the <c>email</c> claim →
    /// (interim) the identity being the address itself → refuse. Never the <c>#EXT#</c> UPN.</summary>
    private static bool MatchesInvitedPerson(
        WorkspaceInvitation invitation, WorkspaceUser? invitedUser, string signedInIdentity, string? signedInEmail)
    {
        if (invitedUser?.ExternalSubjectId is { Length: > 0 } boundSubject)
        {
            return string.Equals(boundSubject, signedInIdentity, StringComparison.Ordinal);
        }

        if (!string.IsNullOrWhiteSpace(signedInEmail)
            && string.Equals(invitation.Email, signedInEmail.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return string.Equals(invitation.Email, signedInIdentity, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseToken(string? rawToken, out TenantId tenantId, out string secret)
    {
        tenantId = default;
        secret = string.Empty;

        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return false;
        }

        // Rule C4: Guid.TryParseExact on the prefix is the FIRST thing this method does with
        // caller-controlled text -- no ITenantContext.BeginScope call happens anywhere above this
        // line, in this method or any of its callers, so a parse failure truly enters no scope.
        var separatorIndex = rawToken.IndexOf('.');
        if (separatorIndex <= 0 || separatorIndex == rawToken.Length - 1)
        {
            return false;
        }

        var prefix = rawToken[..separatorIndex];
        if (!Guid.TryParseExact(prefix, "N", out var tenantGuid))
        {
            return false;
        }

        tenantId = new TenantId(tenantGuid);
        secret = rawToken[(separatorIndex + 1)..];
        return true;
    }

    /// <summary>SHA-256 of the secret half (Rule C2) -- lowercase hex, well inside
    /// <see cref="WorkspaceInvitation.TokenHash"/>'s <c>varchar(128)</c> bound (64 characters).</summary>
    private static string ComputeHash(string secret) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}

/// <summary>The 201's <c>deliveryOutcome</c> (ADR-026 w15 footer §8): one discriminant string, not
/// two booleans. <see cref="Sent"/> if and only if <c>mailDelivered</c>.</summary>
public enum InvitationDeliveryOutcome
{
    Sent,
    MailFailed,
    NoTransport,
}

public static class InvitationDeliveryOutcomeExtensions
{
    public static string ToWireValue(this InvitationDeliveryOutcome outcome) => outcome switch
    {
        InvitationDeliveryOutcome.Sent => "sent",
        InvitationDeliveryOutcome.MailFailed => "mail_failed",
        _ => "no_transport",
    };
}

/// <summary>Outcome of <see cref="WorkspaceInvitationService.IssueAsync"/>.</summary>
public sealed class InvitationIssueResult
{
    private InvitationIssueResult(
        MembershipOperationStatus status,
        WorkspaceInvitation? invitation,
        string? acceptUrl,
        bool mailDelivered,
        InvitationDeliveryOutcome deliveryOutcome,
        bool identityProvisioned,
        GuestProvisioningFailureReason? provisioningFailureReason,
        string? error)
    {
        Status = status;
        Invitation = invitation;
        AcceptUrl = acceptUrl;
        MailDelivered = mailDelivered;
        DeliveryOutcome = deliveryOutcome;
        IdentityProvisioned = identityProvisioned;
        ProvisioningFailureReason = provisioningFailureReason;
        Error = error;
    }

    public MembershipOperationStatus Status { get; }

    public bool IsSuccess => Status == MembershipOperationStatus.Success;

    public WorkspaceInvitation? Invitation { get; }

    /// <summary><c>{base}/invite/accept#&lt;token&gt;</c> when an accept base is configured, the w14
    /// site-relative form otherwise (ADR-025 Rule C9 / ADR-026 w15 footer §4). Present only on success.</summary>
    public string? AcceptUrl { get; }

    /// <summary>The mailer's own bool: accepted for delivery, never a receipt (ADR-025 §J.6e).</summary>
    public bool MailDelivered { get; }

    /// <summary>ADR-026 w15 footer §8: <c>sent</c> ⇔ <see cref="MailDelivered"/>.</summary>
    public InvitationDeliveryOutcome DeliveryOutcome { get; }

    /// <summary>ADR-026 w15 footer §1: true for a created or an already-present guest, false when
    /// provisioning is not configured.</summary>
    public bool IdentityProvisioned { get; }

    /// <summary>Set only for <see cref="MembershipOperationStatus.ProvisioningFailed"/>: the 502's reason.</summary>
    public GuestProvisioningFailureReason? ProvisioningFailureReason { get; }

    public string? Error { get; }

    public static InvitationIssueResult Success(
        WorkspaceInvitation invitation,
        string acceptUrl,
        bool mailDelivered,
        InvitationDeliveryOutcome deliveryOutcome,
        bool identityProvisioned) =>
        new(MembershipOperationStatus.Success, invitation, acceptUrl, mailDelivered, deliveryOutcome, identityProvisioned, null, null);

    public static InvitationIssueResult Failure(MembershipOperationStatus status, string? error) =>
        new(status, null, null, false, InvitationDeliveryOutcome.NoTransport, false, null, error);

    public static InvitationIssueResult ProvisioningFailed(GuestProvisioningFailureReason reason) =>
        new(MembershipOperationStatus.ProvisioningFailed, null, null, false, InvitationDeliveryOutcome.NoTransport, false, reason,
            "the company directory did not provision a guest for this address; no invitation was created.");
}

/// <summary>Outcome of <see cref="WorkspaceInvitationService.PreAcceptAsync"/> — ADR-025 Rule D.3e:
/// workspace name, offered role and expiry, and nothing else, ever.</summary>
public sealed class InvitationPreviewResult
{
    private InvitationPreviewResult(
        MembershipOperationStatus status, string? workspaceName, WorkspaceRoleName? role, DateTimeOffset? expiresAt)
    {
        Status = status;
        WorkspaceName = workspaceName;
        Role = role;
        ExpiresAt = expiresAt;
    }

    public MembershipOperationStatus Status { get; }

    public bool IsSuccess => Status == MembershipOperationStatus.Success;

    public string? WorkspaceName { get; }

    public WorkspaceRoleName? Role { get; }

    public DateTimeOffset? ExpiresAt { get; }

    public static InvitationPreviewResult Success(string workspaceName, WorkspaceRoleName role, DateTimeOffset expiresAt) =>
        new(MembershipOperationStatus.Success, workspaceName, role, expiresAt);

    public static InvitationPreviewResult Failure(MembershipOperationStatus status) => new(status, null, null, null);
}

/// <summary>Outcome of <see cref="WorkspaceInvitationService.AcceptAsync"/>.</summary>
public sealed class InvitationAcceptResult
{
    private InvitationAcceptResult(
        MembershipOperationStatus status, TenantId workspaceId, string? workspaceName, WorkspaceRoleName? role, string? error)
    {
        Status = status;
        WorkspaceId = workspaceId;
        WorkspaceName = workspaceName;
        Role = role;
        Error = error;
    }

    public MembershipOperationStatus Status { get; }

    public bool IsSuccess => Status == MembershipOperationStatus.Success;

    public TenantId WorkspaceId { get; }

    public string? WorkspaceName { get; }

    public WorkspaceRoleName? Role { get; }

    public string? Error { get; }

    public static InvitationAcceptResult Success(TenantId workspaceId, string workspaceName, WorkspaceRoleName role) =>
        new(MembershipOperationStatus.Success, workspaceId, workspaceName, role, null);

    public static InvitationAcceptResult Failure(MembershipOperationStatus status, string? error) =>
        new(status, default, null, null, error);
}
