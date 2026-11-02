using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Raffa.Identity.Workspace.Application;
using Raffa.SharedKernel;

namespace Raffa.Identity.Workspace.Infrastructure;

/// <remarks>These row types are unmapped (<c>SqlQuery</c>); the context uses the snake_case naming convention,
/// so every column a query returns must be aliased in snake_case (<c>created_at</c>, not <c>CreatedAt</c>).
/// Only the scalar <c>Value</c> column of <c>SqlQuery&lt;int&gt;</c> keeps its fixed name.</remarks>
public sealed class PlatformWorkspaceRow
{
    public Guid TenantId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? AdminEmail { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Status { get; set; } = "active";
}

public sealed class PlatformInvitationRow
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? AcceptedAt { get; set; }
    public string? AcceptedBySubject { get; set; }
    public Guid? WorkspaceId { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public sealed class PlatformOverview
{
    public int TotalWorkspaces { get; set; }
    public int ActiveWorkspaces { get; set; }
    public int SuspendedWorkspaces { get; set; }
    public int RegisteredUsers { get; set; }
    public int PendingInvitations { get; set; }
}

public enum PlatformInvitationAcceptStatus { Accepted, NotFound, Expired, Revoked, WrongAccount, AlreadyUsed }

public sealed record PlatformInvitationIssued(
    Guid Id, string Email, string AcceptUrl, DateTimeOffset ExpiresAt, bool IdentityProvisioned, string DeliveryOutcome);

/// <summary>Either the issued invitation, or the reason the directory would not prepare the prospect's
/// sign-in identity (in which case nothing was written).</summary>
public sealed record PlatformInvitationIssueResult(PlatformInvitationIssued? Issued, GuestProvisioningFailureReason? FailureReason)
{
    public bool IsSuccess => Issued is not null;
}

/// <summary>
/// Platform-level data for the portal admin (Raffa staff): the workspace metadata registry and the
/// prospect invitations that precede any workspace. Both tables live outside the tenant model (see
/// the <c>AddPlatformAdminTables</c> migration) and hold no customer content, so reading them needs no
/// tenant scope and no RLS exception. The hot-path checks (registry, suspension, spend) are no-ops on a
/// non-relational provider (the in-memory test host), where these tables do not exist. A prospect invitation is single-use, revocable, bound to one
/// address and expires after <see cref="ExpiryDays"/> days; only the SHA-256 of the secret is stored.
/// </summary>
public sealed class PlatformAdminService(
    IdentityWorkspaceDbContext db, IClock clock, IGuestProvisioner guestProvisioner, IInvitationMailer mailer, InvitationOptions invitationOptions)
{
    public const int ExpiryDays = 7;

    /// <summary>True only on the Npgsql provider. Not <c>Database.IsRelational()</c>: the in-memory test
    /// hosts swap the provider but keep the Npgsql options extension, so that check answers true there and
    /// the raw SQL below would throw. The provider name is what the context actually runs on.</summary>
    private bool IsPostgres => db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;

    public const string Active = "active";
    public const string Suspended = "suspended";

    // ----- workspace registry -----

    /// <summary>Idempotent: a workspace already registered is left exactly as it is.</summary>
    public Task<int> RegisterWorkspaceAsync(
        Guid tenantId, string name, string? adminEmail, DateTimeOffset createdAt, CancellationToken ct = default) =>
        !IsPostgres ? Task.FromResult(0) : db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO platform_workspace (tenant_id, name, admin_email, created_at, status)
            VALUES ({tenantId}, {name}, {adminEmail}, {createdAt.ToUniversalTime()}, 'active')
            ON CONFLICT (tenant_id) DO NOTHING
            """, ct);

    public Task<List<PlatformWorkspaceRow>> ListWorkspacesAsync(CancellationToken ct = default) =>
        db.Database.SqlQuery<PlatformWorkspaceRow>(
            $"""
            SELECT tenant_id AS "tenant_id", name AS "name", admin_email AS "admin_email",
                   created_at AS "created_at", status AS "status"
            FROM platform_workspace ORDER BY created_at DESC
            """).ToListAsync(ct);

    /// <summary>True when the workspace is registered and suspended. Unregistered = not suspended.</summary>
    public async Task<bool> IsSuspendedAsync(Guid tenantId, CancellationToken ct = default) =>
        IsPostgres && (await db.Database.SqlQuery<int>(
            $"SELECT 1 AS \"Value\" FROM platform_workspace WHERE tenant_id = {tenantId} AND status = 'suspended'")
            .ToListAsync(ct).ConfigureAwait(false)).Count > 0;

    /// <summary>False when no such workspace is registered.</summary>
    public async Task<bool> SetWorkspaceStatusAsync(Guid tenantId, string status, CancellationToken ct = default) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE platform_workspace SET status = {status} WHERE tenant_id = {tenantId}", ct)
            .ConfigureAwait(false) == 1;

    // ----- prospect invitations -----

    /// <summary>
    /// Issues a prospect invitation the way a workspace invitation is issued: the guest identity is
    /// prepared in the directory <b>first</b> (so any address, including a personal one, can sign in; a
    /// failure writes nothing), then the live invitation for the address is replaced by a fresh one in one
    /// transaction, then the mail is attempted. The plaintext secret exists only inside the returned link.
    /// </summary>
    public async Task<PlatformInvitationIssueResult> IssueInvitationAsync(
        string email, string createdBy, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();

        var provisioning = await guestProvisioner.EnsureGuestAsync(normalized, "Raffa.ai", ct).ConfigureAwait(false);
        if (provisioning.Status == GuestProvisioningStatus.Failed)
        {
            return new PlatformInvitationIssueResult(
                null, provisioning.FailureReason ?? GuestProvisioningFailureReason.ProvisioningFailed);
        }

        var id = Guid.NewGuid();
        var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var now = clock.UtcNow.ToUniversalTime();
        var expiresAt = now.AddDays(ExpiryDays);

        // Two statements in one transaction, in this order: revoke the live invitation for the address,
        // then insert the new one. A single data-modifying CTE does not work here: the order of its parts
        // is unspecified, and the INSERT can hit the one-live-invitation-per-address unique index before
        // the UPDATE has released it.
        await using (var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false))
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                UPDATE platform_invitation SET revoked_at = {now}
                WHERE lower(email) = {normalized} AND accepted_at IS NULL AND revoked_at IS NULL
                """, ct).ConfigureAwait(false);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO platform_invitation (id, email, token_hash, created_by, created_at, expires_at)
                VALUES ({id}, {normalized}, {Hash(secret)}, {createdBy}, {now}, {expiresAt})
                """, ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }

        var acceptUrl = ComposeProspectUrl(secret);
        var deliveryOutcome = "no_transport";
        if (mailer.IsConfigured)
        {
            deliveryOutcome = await mailer.TrySendProspectAsync(normalized, acceptUrl, expiresAt, ct).ConfigureAwait(false)
                ? "sent"
                : "mail_failed";
        }

        return new PlatformInvitationIssueResult(
            new PlatformInvitationIssued(id, normalized, acceptUrl, expiresAt, provisioning.IdentityProvisioned, deliveryOutcome),
            null);
    }

    /// <summary><c>{base}/prospect/accept#{secret}</c>: the secret rides in the fragment, never a path or
    /// query string, so it is not sent to any server or written to an access log. Site-relative when no
    /// usable base is configured.</summary>
    private string ComposeProspectUrl(string secret) =>
        (InvitationOptions.IsUsableBase(invitationOptions.AcceptUrlBase)
            ? invitationOptions.AcceptUrlBase!.TrimEnd('/')
            : string.Empty) + "/prospect/accept#" + secret;

    public Task<List<PlatformInvitationRow>> ListInvitationsAsync(CancellationToken ct = default) =>
        db.Database.SqlQueryRaw<PlatformInvitationRow>(InvitationSelect + " ORDER BY created_at DESC").ToListAsync(ct);

    public async Task<PlatformInvitationRow?> GetInvitationAsync(Guid id, CancellationToken ct = default) =>
        (await db.Database.SqlQueryRaw<PlatformInvitationRow>(InvitationSelect + " WHERE id = {0}", id)
            .ToListAsync(ct).ConfigureAwait(false)).SingleOrDefault();

    /// <summary>False when the invitation does not exist or is already accepted or revoked.</summary>
    public async Task<bool> RevokeInvitationAsync(Guid id, CancellationToken ct = default) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE platform_invitation SET revoked_at = {clock.UtcNow.ToUniversalTime()}
            WHERE id = {id} AND accepted_at IS NULL AND revoked_at IS NULL
            """, ct).ConfigureAwait(false) == 1;

    /// <summary>
    /// Accepts the invitation for the signed-in person. One conditional UPDATE decides it, so two
    /// concurrent accepts cannot both win; the address must match the invitation's. Repeating the
    /// accept as the same person is a no-op success.
    /// </summary>
    public async Task<PlatformInvitationAcceptStatus> AcceptInvitationAsync(
        string token, string subject, string? email, CancellationToken ct = default)
    {
        var hash = Hash(token.Trim());
        var now = clock.UtcNow.ToUniversalTime();
        var normalizedEmail = email?.Trim().ToLowerInvariant() ?? string.Empty;

        var updated = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE platform_invitation SET accepted_at = {now}, accepted_by_subject = {subject}
            WHERE token_hash = {hash} AND accepted_at IS NULL AND revoked_at IS NULL
              AND expires_at > {now} AND lower(email) = {normalizedEmail}
            """, ct).ConfigureAwait(false);
        if (updated == 1)
        {
            return PlatformInvitationAcceptStatus.Accepted;
        }

        var row = (await db.Database.SqlQueryRaw<PlatformInvitationRow>(InvitationSelect + " WHERE token_hash = {0}", hash)
            .ToListAsync(ct).ConfigureAwait(false)).SingleOrDefault();
        if (row is null)
        {
            return PlatformInvitationAcceptStatus.NotFound;
        }

        if (row.AcceptedAt is not null)
        {
            return string.Equals(row.AcceptedBySubject, subject, StringComparison.Ordinal)
                ? PlatformInvitationAcceptStatus.Accepted
                : PlatformInvitationAcceptStatus.AlreadyUsed;
        }

        if (row.RevokedAt is not null)
        {
            return PlatformInvitationAcceptStatus.Revoked;
        }

        if (row.ExpiresAt <= now)
        {
            return PlatformInvitationAcceptStatus.Expired;
        }

        return PlatformInvitationAcceptStatus.WrongAccount;
    }

    /// <summary>True when this person accepted an invitation that has not yet been spent on a workspace.</summary>
    public async Task<bool> HasUnspentInvitationAsync(string subject, CancellationToken ct = default) =>
        IsPostgres && (await db.Database.SqlQuery<int>(
            $"""
            SELECT 1 AS "Value" FROM platform_invitation
            WHERE accepted_by_subject = {subject} AND accepted_at IS NOT NULL AND workspace_id IS NULL
            LIMIT 1
            """).ToListAsync(ct).ConfigureAwait(false)).Count > 0;

    /// <summary>Spends one accepted invitation on the workspace just created. Atomic and idempotent.</summary>
    public Task<int> SpendInvitationAsync(string subject, Guid tenantId, CancellationToken ct = default) =>
        !IsPostgres ? Task.FromResult(0) : db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE platform_invitation SET workspace_id = {tenantId}
            WHERE id = (
                SELECT id FROM platform_invitation
                WHERE accepted_by_subject = {subject} AND accepted_at IS NOT NULL AND workspace_id IS NULL
                ORDER BY accepted_at LIMIT 1 FOR UPDATE SKIP LOCKED)
            """, ct);

    // ----- overview -----

    public async Task<PlatformOverview> GetOverviewAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow.ToUniversalTime();
        var rows = await db.Database.SqlQuery<PlatformOverview>(
            $"""
            SELECT
              (SELECT count(*) FROM platform_workspace)::int AS "total_workspaces",
              (SELECT count(*) FROM platform_workspace WHERE status = 'active')::int AS "active_workspaces",
              (SELECT count(*) FROM platform_workspace WHERE status = 'suspended')::int AS "suspended_workspaces",
              (SELECT count(*) FROM platform_invitation WHERE accepted_at IS NOT NULL)::int AS "registered_users",
              (SELECT count(*) FROM platform_invitation
                 WHERE accepted_at IS NULL AND revoked_at IS NULL AND expires_at > {now})::int AS "pending_invitations"
            """).ToListAsync(ct).ConfigureAwait(false);
        return rows.Single();
    }

    public static string StatusOf(PlatformInvitationRow row, DateTimeOffset now) =>
        row.AcceptedAt is not null ? "accepted"
        : row.RevokedAt is not null ? "revoked"
        : row.ExpiresAt <= now ? "expired"
        : "pending";

    private const string InvitationSelect =
        """
        SELECT id AS "id", email AS "email", created_by AS "created_by", created_at AS "created_at",
               expires_at AS "expires_at", accepted_at AS "accepted_at", accepted_by_subject AS "accepted_by_subject",
               workspace_id AS "workspace_id", revoked_at AS "revoked_at"
        FROM platform_invitation
        """;

    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
