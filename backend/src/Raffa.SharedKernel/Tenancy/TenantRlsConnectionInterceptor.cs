using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Raffa.SharedKernel.Tenancy;

/// <summary>
/// EF Core connection interceptor that is the data-access-layer half of ADR-009's tenant
/// isolation. It sets the Postgres session GUC `app.tenant_id` once when a connection is opened
/// for the current request/worker job — read from <see cref="ITenantContext.Current"/> — and
/// clears it when the connection is returned to the pool, so a pooled connection can never leak
/// one tenant's claim to whoever borrows it next. Postgres Row-Level Security policies
/// (`current_setting('app.tenant_id', true)`) are the non-bypassable backstop this claim feeds;
/// this interceptor never runs as `BYPASSRLS` and never widens access, it only ever narrows it.
/// When <see cref="ITenantContext.Current"/> is <see langword="null"/> (no active scope) the
/// claim is left unset, so RLS denies every tenant-scoped row on that connection — fail closed.
/// </summary>
public sealed class TenantRlsConnectionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    internal const string TenantSettingName = "app.tenant_id";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetTenantClaim(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SetTenantClaimAsync(connection, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    public override InterceptionResult ConnectionClosing(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        ClearTenantClaim(connection);
        return base.ConnectionClosing(connection, eventData, result);
    }

    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        await ClearTenantClaimAsync(connection).ConfigureAwait(false);
        return await base.ConnectionClosingAsync(connection, eventData, result).ConfigureAwait(false);
    }

    private void SetTenantClaim(DbConnection connection)
    {
        var tenantId = tenantContext.Current;
        if (tenantId is null)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = BuildSetCommandText(tenantId.Value);
        command.ExecuteNonQuery();
    }

    private async Task SetTenantClaimAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var tenantId = tenantContext.Current;
        if (tenantId is null)
        {
            return;
        }

        var command = connection.CreateCommand();
        await using var _ = command.ConfigureAwait(false);
        command.CommandText = BuildSetCommandText(tenantId.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void ClearTenantClaim(DbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"RESET {TenantSettingName}";
        command.ExecuteNonQuery();
    }

    private static async Task ClearTenantClaimAsync(DbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        var command = connection.CreateCommand();
        await using var _ = command.ConfigureAwait(false);
        command.CommandText = $"RESET {TenantSettingName}";
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static string BuildSetCommandText(TenantId tenantId) =>
        // Postgres `SET`/custom GUCs do not accept bind parameters. TenantId wraps a Guid, whose
        // "D" format is constrained to hex digits and hyphens, so inlining it here carries no
        // injection risk.
        $"SET {TenantSettingName} = '{tenantId.Value:D}'";
}
