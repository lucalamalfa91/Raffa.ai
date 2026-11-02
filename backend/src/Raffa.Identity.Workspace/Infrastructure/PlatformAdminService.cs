using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Raffa.SharedKernel;

namespace Raffa.Identity.Workspace.Infrastructure;

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

public sealed record PlatformInvitationIssued(Guid Id, string Email, string Token, DateTimeOffset ExpiresAt);

/// <summary>
/// Platform-level data for the portal admin (Raffa staff): the workspace metadata registry and the
/// prospect invitations that precede any workspace. Both tables live outside the tenant model (see
/// the <c>AddPlatformAdminTables</c> migration) and hold no customer content, so reading them needs no
/// tenant scope and no RLS exception. The hot-path checks (registry, suspension, spend) are no-ops on a
/// non-relational provider (the in-memory test host), where these tables do not exist. A prospect invitation is single-use, revocable, bound to one
/// address and expires after <see cref="ExpiryDays"/> days; only the SHA-256 of the secret is stored.
/// </summary>
public sealed class PlatformAdminService(IdentityWorkspaceDbContext db, IClock clock)
{
    public const int ExpiryDays = 7;

    public const string Active = "active";
    public const string Suspended = "suspended";

    // ----- workspace registry -----

    /// <summary>Idempotent: a workspace already registered is left exactly as it is.</summary>
    public Task<int> RegisterWorkspaceAsync(
        Guid tenantId, string name, string? adminEmail, DateTimeOffset createdAt, CancellationToken ct = default) =>
        !db.Database.IsRelational() ? Task.FromResult(0) : db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO platform_workspace (tenant_id, name, admin_email, created_at, status)
            VALUES ({tenantId}, {name}, {adminEmail}, {createdAt.ToUniversalTime()}, 'active')
            ON CONFLICT (tenant_id) DO NOTHING
            """, ct);

    public Task<List<PlatformWorkspaceRow>> ListWorkspacesAsync(CancellationToken ct = default) =>
        db.Database.SqlQuery<PlatformWorkspaceRow>(
            $"""
            SELECT tenant_id AS "TenantId", name AS "Name", admin_email AS "AdminEmail",
                   created_at AS "CreatedAt", status AS "Status"
            FROM platform_workspace ORDER BY created_at DESC
            """).ToListAsync(ct);

    /// <summary>True when the workspace is registered and suspended. Unregistered = not suspended.</summary>
    public async Task<bool> IsSuspendedAsync(Guid tenantId, CancellationToken ct = default) =>
        db.Database.IsRelational() && (await db.Database.SqlQuery<int>(
            $"SELECT 1 AS \"Value\" FROM platform_workspace WHERE tenant_id = {tenantId} AND status = 'suspended'")
            .ToListAsync(ct).ConfigureAwait(false)).Count > 0;

    /// <summary>False when no such workspace is registered.</summary>
    public async Task<bool> SetWorkspaceStatusAsync(Guid tenantId, string status, CancellationToken ct = default) =>
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE platform_workspace SET status = {status} WHERE tenant_id = {tenantId}", ct)
            .ConfigureAwait(false) == 1;

    // ----- prospect invitations -----

    /// <summary>Revokes any live invitation for the address and issues a fresh one, in one statement.
    /// The plaintext token exists only in the returned value.</summary>
    public async Task<PlatformInvitationIssued> IssueInvitationAsync(
        string email, string createdBy, CancellationToken ct = default)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var id = Guid.NewGuid();
        var secret = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var now = clock.UtcNow.ToUniversalTime();
        var expiresAt = now.AddDays(ExpiryDays);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            WITH revoked AS (
                UPDATE platform_invitation SET revoked_at = {now}
                WHERE lower(email) = {normalized} AND accepted_at IS NULL AND revoked_at IS NULL
            )
            INSERT INTO platform_invitation (id, email, token_hash, created_by, created_at, expires_at)
            VALUES ({id}, {normalized}, {Hash(secret)}, {createdBy}, {now}, {expiresAt})
            """, ct).ConfigureAwait(false);

        return new PlatformInvitationIssued(id, normalized, secret, expiresAt);
    }

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
        db.Database.IsRelational() && (await db.Database.SqlQuery<int>(
            $"""
            SELECT 1 AS "Value" FROM platform_invitation
            WHERE accepted_by_subject = {subject} AND accepted_at IS NOT NULL AND workspace_id IS NULL
            LIMIT 1
            """).ToListAsync(ct).ConfigureAwait(false)).Count > 0;

    /// <summary>Spends one accepted invitation on the workspace just created. Atomic and idempotent.</summary>
    public Task<int> SpendInvitationAsync(string subject, Guid tenantId, CancellationToken ct = default) =>
        !db.Database.IsRelational() ? Task.FromResult(0) : db.Database.ExecuteSqlInterpolatedAsync(
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
              (SELECT count(*) FROM platform_workspace)::int AS "TotalWorkspaces",
              (SELECT count(*) FROM platform_workspace WHERE status = 'active')::int AS "ActiveWorkspaces",
              (SELECT count(*) FROM platform_workspace WHERE status = 'suspended')::int AS "SuspendedWorkspaces",
              (SELECT count(*) FROM platform_invitation WHERE accepted_at IS NOT NULL)::int AS "RegisteredUsers",
              (SELECT count(*) FROM platform_invitation
                 WHERE accepted_at IS NULL AND revoked_at IS NULL AND expires_at > {now})::int AS "PendingInvitations"
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
        SELECT id AS "Id", email AS "Email", created_by AS "CreatedBy", created_at AS "CreatedAt",
               expires_at AS "ExpiresAt", accepted_at AS "AcceptedAt", accepted_by_subject AS "AcceptedBySubject",
               workspace_id AS "WorkspaceId", revoked_at AS "RevokedAt"
        FROM platform_invitation
        """;

    private static string Hash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}
