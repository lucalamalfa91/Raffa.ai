using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Raffa.SharedKernel.Tenancy;

/// <summary>
/// EF Core connection interceptor that is the data-access-layer half of ADR-009's tenant
/// isolation, extended by the ADR-009 w14 footer (clauses 1-5) and ADR-025 §F.2 to also carry the
/// caller's identity. It sets two independent Postgres session GUCs once when a connection is
/// opened for the current request/worker job -- `app.tenant_id` from
/// <see cref="ITenantContext.Current"/> and `app.identity_subject` from
/// <see cref="ICallerIdentityContext.Current"/> -- and clears both when the connection is
/// returned to the pool, so a pooled connection can never leak either claim to whoever borrows it
/// next. Postgres Row-Level Security policies (`current_setting('app.tenant_id', true)`,
/// `current_setting('app.identity_subject', true)`) are the non-bypassable backstop these claims
/// feed; this interceptor never runs as `BYPASSRLS` and never widens access, it only ever narrows
/// it.
///
/// The two claims are deliberately independent (ADR-009 w14 footer clause 1): either may be set
/// without the other. <c>GET /api/workspaces</c> (NW-01) is exactly the case that needs
/// `app.identity_subject` set with **no** `app.tenant_id` at all, to discover which tenants the
/// caller belongs to before any tenant scope exists. When a claim's source
/// (<see cref="ITenantContext.Current"/> / <see cref="ICallerIdentityContext.Current"/>) is
/// <see langword="null"/> (or, for the identity, empty after normalisation), that claim is left
/// unset -- never an empty-string stand-in -- so RLS denies every row the corresponding policy
/// would otherwise widen. Fail closed, never fail open.
///
/// The `callerIdentityContext` constructor parameter is optional (defaults to
/// <see langword="null"/>) so every existing call site across every module's
/// `*DbContextOptions.Configure` (constructed today with only a tenant context) keeps compiling
/// and behaving exactly as before; only a caller that actually needs the identity claim supplies
/// one. Supplying <see langword="null"/> is equivalent to an identity scope that is never entered
/// -- the identity claim is simply never set on that DbContext's connections.
/// </summary>
public sealed class TenantRlsConnectionInterceptor(
    ITenantContext tenantContext,
    ICallerIdentityContext? callerIdentityContext = null) : DbConnectionInterceptor
{
    internal const string TenantSettingName = "app.tenant_id";
    internal const string IdentitySettingName = "app.identity_subject";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        SetTenantClaim(connection);
        SetIdentityClaim(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await SetTenantClaimAsync(connection, cancellationToken).ConfigureAwait(false);
        await SetIdentityClaimAsync(connection, cancellationToken).ConfigureAwait(false);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken).ConfigureAwait(false);
    }

    public override InterceptionResult ConnectionClosing(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        ClearTenantClaim(connection);
        ClearIdentityClaim(connection);
        return base.ConnectionClosing(connection, eventData, result);
    }

    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(
        DbConnection connection, ConnectionEventData eventData, InterceptionResult result)
    {
        await ClearTenantClaimAsync(connection).ConfigureAwait(false);
        await ClearIdentityClaimAsync(connection).ConfigureAwait(false);
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

    // --- Identity claim (`app.identity_subject`) -----------------------------------------------
    //
    // Deliberately asymmetric with the tenant claim above -- this is not an inconsistency to
    // "tidy up", it is the fix. `TenantId` wraps a `Guid`, whose "D" format
    // (`BuildSetCommandText` below) is constrained to hex digits and hyphens, so inlining it into
    // a `SET` statement carries no injection risk *for that type only*. An identity is
    // caller-controlled text (`X-User-Id`, ADR-025 §A) with no such guarantee: copying
    // `BuildSetCommandText`'s interpolation here would let an identity such as
    // `x'; SET app.tenant_id = '<victim>'; --` repoint the tenant claim on this same connection
    // for the rest of the request -- exactly the cross-tenant path ADR-009 (`:12-13`) forbids.
    // `SET`/custom GUCs cannot accept a bind parameter at all (see `BuildSetCommandText`'s own
    // comment), so the remedy is not "add a parameter to SET" -- it is a different statement form
    // that can bind one: `SELECT set_config(name, value, is_local)` is a regular statement, and
    // accepts `value` as a real bind parameter. That is what every write of
    // `app.identity_subject` below uses, exclusively. Do not "tidy" this back into `SET`: that
    // reopens the injection sink. This is the first `set_config` call in `backend/src` -- a
    // deliberate new pattern this claim specifically needs, not an oversight to unify with the
    // tenant claim's `SET`.
    //
    // The third argument is always `false` (session-scoped), never `true` (transaction-local):
    // each statement `ConnectionOpened`/`ConnectionOpenedAsync` issues here runs in its own
    // implicit single-statement transaction, so a transaction-local value would already have
    // reverted before the caller's own first query runs against this connection -- silently
    // vanishing before the discovery read that needs it (ADR-009 w14 footer clause 3, ADR-025
    // §F.2b).

    private void SetIdentityClaim(DbConnection connection)
    {
        var identity = callerIdentityContext?.Current;
        if (string.IsNullOrEmpty(identity))
        {
            // No identity scope active (or, defensively, an empty value survived normalisation):
            // leave the GUC genuinely unset rather than set it to ''. The `identity_self`
            // policy's own `nullif(current_setting(...), '') IS NOT NULL` guard would deny either
            // way, but "unset" is the honest signal for "no caller identity", not a stand-in.
            return;
        }

        using var command = connection.CreateCommand();
        PrepareSetIdentityCommand(command, identity);
        command.ExecuteNonQuery();
    }

    private async Task SetIdentityClaimAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        var identity = callerIdentityContext?.Current;
        if (string.IsNullOrEmpty(identity))
        {
            return;
        }

        var command = connection.CreateCommand();
        await using var _ = command.ConfigureAwait(false);
        PrepareSetIdentityCommand(command, identity);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Builds <c>SELECT set_config('app.identity_subject', @identity, false)</c> with
    /// <paramref name="identity"/> bound through a real <see cref="DbParameter"/> -- a genuine
    /// `NpgsqlParameter` at runtime, since this interceptor only ever runs against the Npgsql
    /// provider, obtained via the provider-agnostic <see cref="DbCommand.CreateParameter"/> so
    /// this shared-kernel file keeps no direct Npgsql package reference (see
    /// Raffa.SharedKernel.csproj's own comment). <see cref="IdentitySettingName"/> is our own
    /// constant, never caller input, so interpolating *it* into the command text is not the
    /// injection the type doc comment guards against -- <paramref name="identity"/> is the one
    /// caller-controlled value here, and it goes in exclusively through the bind parameter, never
    /// into <see cref="DbCommand.CommandText"/>.
    /// </summary>
    private static void PrepareSetIdentityCommand(DbCommand command, string identity)
    {
        command.CommandText = $"SELECT set_config('{IdentitySettingName}', @identity, false)";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "identity";
        parameter.Value = identity;
        command.Parameters.Add(parameter);
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

    private static void ClearIdentityClaim(DbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText = $"RESET {IdentitySettingName}";
        command.ExecuteNonQuery();
    }

    private static async Task ClearIdentityClaimAsync(DbConnection connection)
    {
        if (connection.State != ConnectionState.Open)
        {
            return;
        }

        var command = connection.CreateCommand();
        await using var _ = command.ConfigureAwait(false);
        command.CommandText = $"RESET {IdentitySettingName}";
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static string BuildSetCommandText(TenantId tenantId) =>
        // Postgres `SET`/custom GUCs do not accept bind parameters. TenantId wraps a Guid, whose
        // "D" format is constrained to hex digits and hyphens, so inlining it here carries no
        // injection risk. Contrast the identity claim above: an identity is caller-controlled
        // text with no such constraint, which is why it is set through a different statement form
        // (`SELECT set_config(..., @identity, false)`) instead of this one.
        $"SET {TenantSettingName} = '{tenantId.Value:D}'";
}
