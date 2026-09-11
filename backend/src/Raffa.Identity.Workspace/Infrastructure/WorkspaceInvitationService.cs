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
/// </summary>
public sealed class WorkspaceInvitationService(
    IdentityWorkspaceDbContext db,
    ITenantContext tenantContext,
    IClock clock,
    IAuditWriter auditWriter,
    IInvitationMailer mailer,
    WorkspaceMembershipService membershipService)
{
    /// <summary>ADR-025 Rule C7: 7-day absolute expiry, evaluated server-side against <see cref="IClock"/>.</summary>
    public const int TokenExpiryDays = 7;

    /// <summary>256 bits (ADR-025 Rule C1).</summary>
    private const int SecretByteLength = 32;

    /// <summary>ADR-025 Rule C9: a URL <b>fragment</b>, never a path or query string.</summary>
    private const string AcceptRoutePrefix = "/invite/accept#";

    /// <summary>
    /// Issue (ADR-025 §D.1, ADR-026 §D5): mints a fresh token, delegates the
    /// <see cref="WorkspaceUser"/>/<see cref="WorkspaceInvitation"/> write to
    /// <see cref="WorkspaceMembershipService.InviteAsync"/>, then attempts delivery through
    /// <see cref="IInvitationMailer"/>. The plaintext token exists only in this call's own stack and
    /// in the returned <see cref="InvitationIssueResult.AcceptUrl"/> — never persisted (Rule C10),
    /// and the 201 response carries it exactly once.
    /// </summary>
    public async Task<InvitationIssueResult> IssueAsync(
        TenantId tenantId, string email, WorkspaceRoleName role, string invitedBy, CancellationToken cancellationToken = default)
    {
        var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(SecretByteLength));
        var token = $"{tenantId.Value:N}.{secret}";
        var tokenHash = ComputeHash(secret);
        var expiresAt = clock.UtcNow.AddDays(TokenExpiryDays);

        var inviteResult = await membershipService
            .InviteAsync(tenantId, email, role, invitedBy, tokenHash, expiresAt, cancellationToken)
            .ConfigureAwait(false);

        if (!inviteResult.IsSuccess)
        {
            return InvitationIssueResult.Failure(inviteResult.Status, inviteResult.Error);
        }

        var invitation = inviteResult.Invitation!;
        var acceptUrl = AcceptRoutePrefix + token;

        WorkspaceTenant? workspace;
        using (tenantContext.BeginScope(tenantId))
        {
            workspace = await db.Workspaces.AsNoTracking()
                .SingleOrDefaultAsync(w => w.TenantId == tenantId, cancellationToken)
                .ConfigureAwait(false);
        }

        // ADR-026 §D6: the bool result IS mailDelivered -- a server fact, never a convention. The
        // NullInvitationMailer this wave registers always returns false; a future transport is a DI
        // registration change here, not a redesign.
        var mailDelivered = await mailer
            .TrySendAsync(invitation.Email, workspace?.Name ?? string.Empty, role, acceptUrl, cancellationToken)
            .ConfigureAwait(false);

        return InvitationIssueResult.Success(invitation, acceptUrl, mailDelivered);
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
    /// 404; email mismatch → 403 with a reason that never echoes the invited address — before
    /// delegating the actual grant to <see cref="WorkspaceMembershipService.AcceptInvitationAsync"/>.
    /// </summary>
    public async Task<InvitationAcceptResult> AcceptAsync(
        string rawToken, string signedInIdentity, CancellationToken cancellationToken = default)
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

        if (invitation.AcceptedAt is not null)
        {
            // Rule D.3d: 409 for the same identity (idempotency signal), 404 for any other (the
            // same "one indistinguishable answer" posture as an unknown token) -- comparison is
            // case-insensitive, matching the same-address check below.
            return string.Equals(invitation.Email, signedInIdentity, StringComparison.OrdinalIgnoreCase)
                ? InvitationAcceptResult.Failure(MembershipOperationStatus.Conflict, "this invitation has already been accepted.")
                : InvitationAcceptResult.Failure(MembershipOperationStatus.NotFound, null);
        }

        if (invitation.ExpiresAt <= clock.UtcNow)
        {
            return InvitationAcceptResult.Failure(MembershipOperationStatus.Expired, null);
        }

        if (!string.Equals(invitation.Email, signedInIdentity, StringComparison.OrdinalIgnoreCase))
        {
            // Rule D.3b: never echo the invited address.
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

/// <summary>Outcome of <see cref="WorkspaceInvitationService.IssueAsync"/>.</summary>
public sealed class InvitationIssueResult
{
    private InvitationIssueResult(
        MembershipOperationStatus status, WorkspaceInvitation? invitation, string? acceptUrl, bool mailDelivered, string? error)
    {
        Status = status;
        Invitation = invitation;
        AcceptUrl = acceptUrl;
        MailDelivered = mailDelivered;
        Error = error;
    }

    public MembershipOperationStatus Status { get; }

    public bool IsSuccess => Status == MembershipOperationStatus.Success;

    public WorkspaceInvitation? Invitation { get; }

    /// <summary>Site-relative <c>/invite/accept#&lt;token&gt;</c> (ADR-025 Rule C9). Present only on success.</summary>
    public string? AcceptUrl { get; }

    public bool MailDelivered { get; }

    public string? Error { get; }

    public static InvitationIssueResult Success(WorkspaceInvitation invitation, string acceptUrl, bool mailDelivered) =>
        new(MembershipOperationStatus.Success, invitation, acceptUrl, mailDelivered, null);

    public static InvitationIssueResult Failure(MembershipOperationStatus status, string? error) =>
        new(status, null, null, false, error);
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
